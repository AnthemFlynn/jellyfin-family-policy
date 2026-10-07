using FamilyPolicy.Core;
using Xunit;
namespace FamilyPolicy.Core.Tests;

public sealed class PolicyTests
{
    static Condition Leaf(Field f, int? n = null, params string[] values) => new() { Field = f, Number = n, Values = values };
    static Rule Allow(string id, Dimension d, int priority, Condition c) => new() { Id = id, Dimension = d, Priority = priority, Effect = Effect.Allow, When = c };
    static Policy Example() => Policy.Pg() with
    {
        TimeZone = "America/Los_Angeles",
        DefaultTimeAllowed = false,
        Rules = [
        Policy.Pg().Rules[0],
        Allow("soccer",Dimension.Content,10,new(){All=[Leaf(Field.Unrated),Leaf(Field.Subject,null,"Soccer")]}),
        Allow("star-wars",Dimension.Content,10,Leaf(Field.Franchise,null,"Star Wars")),
        Allow("daytime",Dimension.Time,0,new(){Field=Field.Window,Window=new(){Start=new(8,0),End=new(18,0)}}),
        Allow("morning-workout",Dimension.Time,10,new(){All=[Leaf(Field.Category,null,"Workout"),new(){Field=Field.Window,Window=new(){Start=new(0,0),End=new(8,0)}}]})]
    };
    static MediaFacts Media(int? rating = 10, string[]? subjects = null, string[]? categories = null, string[]? franchises = null) => new() { TitleId = "movie:1", Kind = "Movie", Rating = rating, Subjects = subjects, Categories = categories, Franchises = franchises };
    static DateTimeOffset At(int h, int m = 0) => new(2026, 10, 7, h, m, 0, TimeSpan.FromHours(-7));
    [Theory]
    [InlineData(0, true)]
    [InlineData(10, true)]
    [InlineData(13, false)]
    [InlineData(null, false)]
    public void PgPreset(int? rating, bool allowed) => Assert.Equal(allowed, new PolicyEvaluator(Policy.Pg()).Evaluate(Media(rating), At(12)).Allowed);
    [Fact]
    public void UnratedSoccerAllowedButUnknownAndOtherSubjectsDenied()
    { var e = new PolicyEvaluator(Example()); Assert.True(e.Evaluate(Media(null, ["Soccer"]), At(12)).Allowed); Assert.False(e.Evaluate(Media(null), At(12)).Allowed); Assert.False(e.Evaluate(Media(null, ["Horror"]), At(12)).Allowed); }
    [Fact]
    public void FranchiseExceptionDoesNotBypassBedtime()
    { var e = new PolicyEvaluator(Example()); Assert.True(e.Evaluate(Media(17, franchises: ["Star Wars"]), At(12)).Allowed); Assert.False(e.Evaluate(Media(17, franchises: ["Star Wars"]), At(7)).Allowed); }
    [Theory]
    [InlineData(7, true)]
    [InlineData(8, true)]
    [InlineData(17, true)]
    [InlineData(18, false)]
    [InlineData(19, false)]
    public void WorkoutHasMorningExceptionOnly(int h, bool expected) => Assert.Equal(expected, new PolicyEvaluator(Example()).Evaluate(Media(categories: ["Workout"]), At(h)).Allowed);
    [Fact] public void ScheduleExceptionDoesNotApproveContent() => Assert.False(new PolicyEvaluator(Example()).Evaluate(Media(17, categories: ["Workout"]), At(7)).Allowed);
    [Fact]
    public void SpecificBlockWinsAndReasonsExplain()
    { var p = Example(); p = p with { Rules = [.. p.Rules, new() { Id = "specific-block", Priority = 100, Effect = Effect.Deny, When = Leaf(Field.Title, null, "movie:1") }] }; var d = new PolicyEvaluator(p).Evaluate(Media(franchises: ["Star Wars"]), At(12)); Assert.False(d.Allowed); Assert.Equal(["specific-block"], d.ContentReasons); }
    [Fact]
    public void SamePriorityDenyWinsRegardlessOfOrder()
    { var a = Allow("allow", Dimension.Content, 1, Condition.Always()); var b = a with { Id = "deny", Effect = Effect.Deny }; foreach (var rules in new[] { new[] { a, b }, new[] { b, a } }) Assert.False(new PolicyEvaluator(new() { Rules = rules }).Evaluate(Media(), At(12)).Allowed); }
    [Fact]
    public void HardBlockCannotBeOverridden()
    { var a = Allow("allow", Dimension.Content, 999, Condition.Always()); var b = a with { Id = "hard", Priority = 0, Effect = Effect.Deny, HardBlock = true }; Assert.False(new PolicyEvaluator(new() { Rules = [a, b] }).Evaluate(Media(), At(12)).Allowed); }
    [Fact]
    public void NegatingUnknownSubjectCannotCreateGrant()
    { var p = new Policy { Rules = [Allow("not-horror", Dimension.Content, 0, new() { Not = Leaf(Field.Subject, null, "Horror") })] }; var d = new PolicyEvaluator(p).Evaluate(Media(subjects: null), At(12)); Assert.False(d.Allowed); Assert.Equal(Match.Unknown, d.Rules[0].Match); }
    [Fact]
    public void ExpiredAndDisabledGrantsDoNotMatch()
    { var r = Allow("grant", Dimension.Content, 0, Condition.Always()); foreach (var rule in new[] { r with { Enabled = false }, r with { ExpiresAt = At(12) } }) Assert.False(new PolicyEvaluator(new() { Rules = [rule] }).Evaluate(Media(), At(12)).Allowed); }
    [Fact]
    public void OvernightUsesStartingWeekday()
    { var p = new Policy { DefaultContentAllowed = true, DefaultTimeAllowed = false, Rules = [Allow("friday-night", Dimension.Time, 0, new() { Field = Field.Window, Window = new() { Start = new(22, 0), End = new(2, 0), Days = [DayOfWeek.Friday] } })] }; var e = new PolicyEvaluator(p); Assert.True(e.Evaluate(Media(), new(2026, 10, 10, 1, 0, 0, TimeSpan.Zero)).Allowed); Assert.False(e.Evaluate(Media(), new(2026, 10, 10, 2, 0, 0, TimeSpan.Zero)).Allowed); }
    [Fact]
    public void DstRepeatedHourUsesActualLocalTime()
    { var p = Example() with { Rules = [Policy.Pg().Rules[0], Allow("one-hour", Dimension.Time, 0, new() { Field = Field.Window, Window = new() { Start = new(1, 0), End = new(2, 0) } })] }; var e = new PolicyEvaluator(p); Assert.True(e.Evaluate(Media(), new(2026, 11, 1, 8, 30, 0, TimeSpan.Zero)).Allowed); Assert.True(e.Evaluate(Media(), new(2026, 11, 1, 9, 30, 0, TimeSpan.Zero)).Allowed); Assert.False(e.Evaluate(Media(), new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero)).Allowed); }
    [Fact]
    public void AgeRequiresExplicitConfiguredValue()
    { var p = new Policy { Rules = [Allow("age", Dimension.Content, 0, Leaf(Field.AgeAtLeast, 12))] }; Assert.False(new PolicyEvaluator(p).Evaluate(Media(), At(12)).Allowed); Assert.True(new PolicyEvaluator(p with { AgeYears = 12 }).Evaluate(Media(), At(12)).Allowed); }
    [Fact]
    public void SnapshotUnaffectedByCallerMutation()
    { var p = Policy.Pg(); var e = new PolicyEvaluator(p); p.Rules[0] = p.Rules[0] with { Effect = Effect.Deny }; Assert.True(e.Evaluate(Media(), At(12)).Allowed); }
    [Fact]
    public void ValidationRejectsAmbiguousOrUnsafeInputs()
    { Assert.Throws<ArgumentException>(() => new PolicyEvaluator(new() { SchemaVersion = 2 })); Assert.Throws<ArgumentException>(() => new PolicyEvaluator(new() { Rules = [Allow("empty", Dimension.Content, 0, new() { Any = [] })] })); Assert.Throws<ArgumentException>(() => new PolicyEvaluator(new() { Rules = [Allow("bad", Dimension.Time, 0, new() { Field = Field.Window, Window = new() })] })); }
    [Fact]
    public void SeriesGrantAndEpisodeDenyUseAncestorIdentity()
    {
        var policy = new Policy { Rules = [Allow("series", Dimension.Content, 100, Leaf(Field.Title, null, "series:1")), new() { Id = "episode-deny", Priority = 200, Effect = Effect.Deny, When = Leaf(Field.Title, null, "episode:2") }] };
        var e = new PolicyEvaluator(policy);
        Assert.True(e.Evaluate(Media() with { TitleId = "episode:1", AncestorTitleIds = ["series:1"] }, At(12)).Allowed);
        Assert.False(e.Evaluate(Media() with { TitleId = "episode:2", AncestorTitleIds = ["series:1"] }, At(12)).Allowed);
    }
    [Fact]
    public void PolicySerializationPreservesDecisions()
    {
        var p = Example(); var encoded = System.Text.Json.JsonSerializer.Serialize(p);
        var restored = System.Text.Json.JsonSerializer.Deserialize<Policy>(encoded)!;
        Assert.Equal(new PolicyEvaluator(p).Evaluate(Media(null, ["Soccer"]), At(12)).Allowed, new PolicyEvaluator(restored).Evaluate(Media(null, ["Soccer"]), At(12)).Allowed);
    }
    [Fact]
    public void ConditionBudgetRejectsExcessiveRequestComplexity()
    {
        var leaves = Enumerable.Range(0, 32).Select(_ => Condition.Always()).ToArray();
        var groups = Enumerable.Range(0, 32).Select(_ => new Condition { All = leaves }).ToArray();
        var root = new Condition { All = groups };
        var rules = Enumerable.Range(0, 5).Select(i => Allow("rule-" + i, Dimension.Content, 0, root)).ToArray();
        Assert.Throws<ArgumentException>(() => new PolicyEvaluator(new() { Rules = rules }));
    }
    [Fact]
    public void UnknownRestrictedSubjectFailsClosedEvenWhenRatingIsPg()
    {
        var policy = Policy.Pg() with { Rules = [Policy.Pg().Rules[0], new() { Id = "block-violence", Priority = 100, Effect = Effect.Deny, When = Leaf(Field.Subject, null, "Violence") }] };
        var e = new PolicyEvaluator(policy);
        Assert.False(e.Evaluate(Media(subjects: null), At(12)).Allowed);
        Assert.True(e.Evaluate(Media(subjects: ["Soccer"]), At(12)).Allowed);
        Assert.False(e.Evaluate(Media(subjects: ["Violence"]), At(12)).Allowed);
    }
    [Fact]
    public void UnknownJsonPropertiesAreRejectedInsteadOfSilentlyIgnored()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<Policy>("{\"DefaultContentAlowed\":true}"));
    }
    [Fact]
    public void ExplicitTitleBlockMatchesDashedAndCompactGuidForms()
    {
        var id = Guid.NewGuid(); var policy = Policy.Pg() with { Rules = [Policy.Pg().Rules[0], new() { Id = "blocked", Priority = 100, Effect = Effect.Deny, When = Leaf(Field.Title, null, id.ToString("D")) }] };
        Assert.False(new PolicyEvaluator(policy).Evaluate(Media() with { TitleId = id.ToString("N") }, At(12)).Allowed);
    }
}
