using System.Text.Json.Serialization;

namespace FamilyPolicy.Core;

[JsonConverter(typeof(JsonStringEnumConverter<Dimension>))]
public enum Dimension { Content, Time }
[JsonConverter(typeof(JsonStringEnumConverter<Effect>))]
public enum Effect { Allow, Deny }
[JsonConverter(typeof(JsonStringEnumConverter<Match>))]
public enum Match { False, True, Unknown }
[JsonConverter(typeof(JsonStringEnumConverter<Field>))]
public enum Field { Always, RatingAtMost, Unrated, MediaKind, Category, Subject, Franchise, Title, AgeAtLeast, Window }

/// <summary>Trusted metadata only. Unknown information is represented by null.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MediaFacts
{
    public required string TitleId { get; init; }
    public string? Kind { get; init; }
    public string[] AncestorTitleIds { get; init; } = [];
    public int? Rating { get; init; }
    public string[]? Categories { get; init; }
    public string[]? Subjects { get; init; }
    public string[]? Franchises { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Window
{
    public TimeOnly Start { get; init; }
    public TimeOnly End { get; init; }
    public DayOfWeek[] Days { get; init; } = Enum.GetValues<DayOfWeek>();
}
/// <summary>Leaves, All, Any and Not are exclusive forms. Unknown stays unknown through Not.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Condition
{
    public Field? Field { get; init; }
    public string[] Values { get; init; } = [];
    public int? Number { get; init; }
    public Window? Window { get; init; }
    public Condition[]? All { get; init; }
    public Condition[]? Any { get; init; }
    public Condition? Not { get; init; }
    public static Condition Always() => new() { Field = Core.Field.Always };
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Rule
{
    public required string Id { get; init; }
    public Dimension Dimension { get; init; }
    public Effect Effect { get; init; } = Effect.Deny;
    public int Priority { get; init; }
    public bool HardBlock { get; init; }
    public bool Enabled { get; init; } = true;
    public DateTimeOffset? ExpiresAt { get; init; }
    public required Condition When { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Policy
{
    public int SchemaVersion { get; init; } = 1;
    public string TimeZone { get; init; } = "UTC";
    public int? AgeYears { get; init; }
    public bool DefaultContentAllowed { get; init; }
    public bool DefaultTimeAllowed { get; init; } = true;
    public Rule[] Rules { get; init; } = [];
    public static Policy Pg() => new() { Rules = [new Rule { Id = "pg-default", Effect = Effect.Allow, When = new Condition { Field = Field.RatingAtMost, Number = 10 } }] };
}
public sealed record RuleResult(string Id, Dimension Dimension, Effect Effect, int Priority, bool HardBlock, Match Match);
public sealed record Decision(bool ContentAllowed, bool TimeAllowed, RuleResult[] Rules, string[] ContentReasons, string[] TimeReasons)
{
    public bool Allowed => ContentAllowed && TimeAllowed;
}

/// <summary>Validated immutable snapshot. Evaluation uses the caller's server clock, never a device clock.</summary>
public sealed class PolicyEvaluator
{
    private readonly Policy policy;
    private readonly TimeZoneInfo zone;
    public PolicyEvaluator(Policy policy)
    {
        Validate(policy);
        // Clone the complete policy so configuration changes cannot mutate an active snapshot.
        this.policy = System.Text.Json.JsonSerializer.Deserialize<Policy>(System.Text.Json.JsonSerializer.Serialize(policy))!;
        zone = TimeZoneInfo.FindSystemTimeZoneById(policy.TimeZone);
    }
    public Decision Evaluate(MediaFacts media, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(media);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var results = policy.Rules.Where(r => r.Enabled && (r.ExpiresAt is null || now < r.ExpiresAt))
            .Select(r => new RuleResult(r.Id, r.Dimension, r.Effect, r.Priority, r.HardBlock, Matches(r.When, media, local))).ToArray();
        var content = Resolve(Dimension.Content, policy.DefaultContentAllowed, results);
        var time = Resolve(Dimension.Time, policy.DefaultTimeAllowed, results);
        return new Decision(content.allowed, time.allowed, results, content.reasons, time.reasons);
    }
    private static (bool allowed, string[] reasons) Resolve(Dimension dimension, bool fallback, RuleResult[] results)
    {
        var hits = results.Where(r => r.Dimension == dimension && (r.Match == Match.True || r.Effect == Effect.Deny && r.Match == Match.Unknown)).ToArray();
        var hard = hits.Where(r => r.HardBlock).ToArray();
        if (hard.Length > 0) return (false, hard.Select(r => r.Id).Order(StringComparer.Ordinal).ToArray());
        if (hits.Length == 0) return (fallback, ["default"]);
        var top = hits.Where(r => r.Priority == hits.Max(r => r.Priority)).ToArray();
        return (!top.Any(r => r.Effect == Effect.Deny), top.Select(r => r.Id).Order(StringComparer.Ordinal).ToArray());
    }
    private Match Matches(Condition c, MediaFacts m, DateTimeOffset local)
    {
        if (c.All is not null) { var v = c.All.Select(x => Matches(x, m, local)).ToArray(); return v.Contains(Match.False) ? Match.False : v.Contains(Match.Unknown) ? Match.Unknown : Match.True; }
        if (c.Any is not null) { var v = c.Any.Select(x => Matches(x, m, local)).ToArray(); return v.Contains(Match.True) ? Match.True : v.Contains(Match.Unknown) ? Match.Unknown : Match.False; }
        if (c.Not is not null) return Matches(c.Not, m, local) switch { Match.True => Match.False, Match.False => Match.True, _ => Match.Unknown };
        return c.Field switch
        {
            Field.Always => Match.True,
            Field.RatingAtMost => m.Rating is null ? Match.Unknown : Bool(m.Rating <= c.Number),
            Field.Unrated => Bool(m.Rating is null),
            Field.MediaKind => Contains(m.Kind is null ? null : [m.Kind], c.Values),
            Field.Category => Contains(m.Categories, c.Values),
            Field.Subject => Contains(m.Subjects, c.Values),
            Field.Franchise => Contains(m.Franchises, c.Values),
            Field.Title => IdentifierMatch([m.TitleId, .. m.AncestorTitleIds], c.Values),
            Field.AgeAtLeast => policy.AgeYears is null ? Match.Unknown : Bool(policy.AgeYears >= c.Number),
            Field.Window => InWindow(c.Window!, local),
            _ => Match.Unknown
        };
    }
    private static Match Bool(bool? x) => x == true ? Match.True : Match.False;
    private static Match Contains(string[]? actual, string[] wanted) => actual is null ? Match.Unknown : Bool(actual.Intersect(wanted, StringComparer.OrdinalIgnoreCase).Any());
    private static Match IdentifierMatch(string[] actual, string[] wanted)
    {
        bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase) || Guid.TryParse(a, out var first) && Guid.TryParse(b, out var second) && first == second;
        return Bool(actual.Any(a => wanted.Any(b => Same(a, b))));
    }
    private static Match InWindow(Window w, DateTimeOffset local)
    {
        var time = TimeOnly.FromDateTime(local.DateTime);
        // Overnight windows belong to their starting day. Start inclusive, end exclusive.
        if (w.Start < w.End) return Bool(w.Days.Contains(local.DayOfWeek) && time >= w.Start && time < w.End);
        var day = time < w.End ? local.AddDays(-1).DayOfWeek : local.DayOfWeek;
        return Bool(w.Days.Contains(day) && (time >= w.Start || time < w.End));
    }
    public static void Validate(Policy p)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (p.SchemaVersion != 1) throw new ArgumentException("Unsupported policy schema.");
        if (p.Rules is null || p.Rules.Length > 256) throw new ArgumentException("At most 256 rules are supported.");
        if (p.AgeYears is < 0 or > 125) throw new ArgumentException("Invalid age.");
        _ = TimeZoneInfo.FindSystemTimeZoneById(p.TimeZone);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var budget = 4096;
        foreach (var r in p.Rules)
        {
            if (r is null || string.IsNullOrWhiteSpace(r.Id) || r.Id.Length > 128 || !ids.Add(r.Id)) throw new ArgumentException("Unique nonempty rule IDs are required.");
            if (!Enum.IsDefined(r.Dimension) || !Enum.IsDefined(r.Effect) || r.HardBlock && r.Effect != Effect.Deny) throw new ArgumentException("Invalid effect or dimension.");
            ValidateCondition(r.When, 0, ref budget);
        }
    }
    private static void ValidateCondition(Condition c, int depth, ref int budget)
    {
        if (c is null || depth > 8 || --budget < 0) throw new ArgumentException("Invalid or excessively nested condition.");
        if (c.Values is null) throw new ArgumentException("Match values cannot be null.");
        var forms = (c.Field is null ? 0 : 1) + (c.All is null ? 0 : 1) + (c.Any is null ? 0 : 1) + (c.Not is null ? 0 : 1);
        if (forms != 1) throw new ArgumentException("Use exactly one condition form.");
        if (c.All is not null || c.Any is not null)
        {
            if (c.Number is not null || c.Window is not null || c.Values.Length != 0) throw new ArgumentException("Group conditions cannot have leaf operands.");
            var children = c.All ?? c.Any!;
            if (children.Length is < 1 or > 32) throw new ArgumentException("Condition groups require 1–32 children.");
            foreach (var child in children) ValidateCondition(child, depth + 1, ref budget);
            return;
        }
        if (c.Not is not null) { if (c.Number is not null || c.Window is not null || c.Values.Length != 0) throw new ArgumentException("Not cannot have leaf operands."); ValidateCondition(c.Not, depth + 1, ref budget); return; }
        if (!Enum.IsDefined(c.Field!.Value)) throw new ArgumentException("Unknown condition field.");
        if (c.Field is Field.RatingAtMost or Field.AgeAtLeast && (c.Number is null || c.Number < 0 || c.Number > 125)) throw new ArgumentException("A valid numeric limit is required.");
        if (c.Field is Field.Title or Field.MediaKind or Field.Category or Field.Subject or Field.Franchise && (c.Values is null || c.Values.Length is < 1 or > 128 || c.Values.Any(string.IsNullOrWhiteSpace))) throw new ArgumentException("Nonempty match values are required.");
        if (c.Field == Field.Window && (c.Window is null || c.Window.Start == c.Window.End || c.Window.Days is null || c.Window.Days.Length == 0 || c.Window.Days.Any(d => !Enum.IsDefined(d)))) throw new ArgumentException("Valid nonempty window required; equal boundaries are ambiguous.");
    }
}
