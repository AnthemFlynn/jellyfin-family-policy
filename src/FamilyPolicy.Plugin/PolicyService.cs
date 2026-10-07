using System.Text.Json;
using FamilyPolicy.Core;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Users;
using Jellyfin.Database.Implementations.Enums;
namespace FamilyPolicy.Plugin;

public sealed class PolicyService(PolicyStore store, ILibraryManager library, IUserManager users)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public MediaFacts Facts(BaseItem item) => Facts(item, store.Read().Labels);
    private MediaFacts Facts(BaseItem item, Dictionary<Guid, TrustedLabels>? annotations)
    {
        var trusted = new[] { item.Id }.Concat(item.GetParents().Select(p => p.Id)).Select(id => annotations?.GetValueOrDefault(id)).Where(a => a is not null).ToArray();
        string[]? Values(Func<TrustedLabels, string[]?> select)
        {
            var known = trusted.Select(a => select(a!)).Where(a => a is not null).ToArray();
            return known.Length == 0 ? null : known.SelectMany(a => a!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        return new()
        {
            TitleId = item.Id.ToString("N"),
            Kind = item.GetType().Name,
            Rating = item.GetParentalRatingScore()?.Score,
            AncestorTitleIds = item.GetParents().Select(p => p.Id.ToString("N")).ToArray(),
            Categories = Values(a => a.Categories),
            Subjects = Values(a => a.Subjects),
            Franchises = Values(a => a.Franchises)
        };
    }
    public Decision? Decide(Guid id, BaseItem item, DateTimeOffset now)
    {
        var (evaluator, annotations) = store.Context(id); if (evaluator is null) return null;
        var decision = evaluator.Evaluate(Facts(item, annotations), now);
        if (item.IsFolder && !decision.ContentAllowed)
        {
            var descendants = library.GetItemList(new InternalItemsQuery { ParentId = item.Id, Recursive = true });
            if (descendants.Any(child => !child.IsFolder && evaluator.Evaluate(Facts(child, annotations), now).ContentAllowed))
                decision = decision with { ContentAllowed = true, ContentReasons = ["contains-allowed-media"] };
        }
        return decision;
    }
    public async Task Configure(Guid id, Policy policy, long revision, string actor, CancellationToken ct)
    {
        _ = new PolicyEvaluator(policy);
        await gate.WaitAsync(ct);
        try
        {
            var user = users.GetUserById(id) ?? throw new ArgumentException("Unknown user.");
            var native = users.GetUserDto(user).Policy;
            if (native.IsAdministrator) throw new ArgumentException("Administrator accounts cannot be managed.");
            var original = store.Read().Accounts.FirstOrDefault(a => a.UserId == id)?.OriginalPolicy ?? JsonSerializer.Serialize(native);
            // Persist enrollment first; the stream filter denies according to the new policy immediately.
            store.Set(id, policy, original, revision, actor);
            await ReconcileAccount(id, ct);
        }
        finally { gate.Release(); }
    }
    public async Task Restore(Guid id, long revision, string actor, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var account = store.Read().Accounts.FirstOrDefault(a => a.UserId == id) ?? throw new ArgumentException("User is not managed.");
            if (store.Read().Revision != revision) throw new RevisionConflictException();
            // Restore the previous native security policy before removing plugin enforcement.
            await users.UpdatePolicyAsync(id, JsonSerializer.Deserialize<UserPolicy>(account.OriginalPolicy)!);
            store.Set(id, null, "", revision, actor);
        }
        finally { gate.Release(); }
    }
    public async Task Reconcile(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { foreach (var account in store.Read().Accounts) await ReconcileAccount(account.UserId, ct); }
        finally { gate.Release(); }
    }
    private async Task ReconcileAccount(Guid id, CancellationToken ct)
    {
        var user = users.GetUserById(id); if (user is null) return;
        var native = users.GetUserDto(user).Policy;
        var allow = "family-policy:allow:" + id.ToString("N");
        var deny = "family-policy:deny:" + id.ToString("N");
        // Install an always-nonempty allowlist first. Failure narrows access instead of opening it.
        native.AllowedTags = [allow];
        native.BlockedTags = [.. native.BlockedTags.Where(t => t != deny), deny];
        native.MaxParentalRating = null; native.MaxParentalSubRating = null; native.BlockUnratedItems = [];
        native.AccessSchedules = []; native.EnableContentDownloading = false; native.EnablePublicSharing = false;
        native.EnableRemoteControlOfOtherUsers = false; native.EnableSharedDeviceControl = false; native.EnableLiveTvAccess = false;
        native.EnableCollectionManagement = false; native.EnableSubtitleManagement = false; native.SyncPlayAccess = SyncPlayUserAccessType.None;
        await users.UpdatePolicyAsync(id, native);
        var items = library.GetItemList(new InternalItemsQuery { Recursive = true, IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode, BaseItemKind.Season, BaseItemKind.Video, BaseItemKind.Audio] });
        var now = DateTimeOffset.UtcNow;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            var decision = Decide(id, item, now);
            var allowed = decision?.ContentAllowed == true;
            var tag = allowed ? allow : deny;
            var opposite = allowed ? deny : allow;
            var tags = item.Tags.Where(t => t != opposite).ToList();
            // Never place an inherited deny tag on a container: a narrower child override
            // may allow an episode. The evaluator handles ancestor title decisions.
            if (!allowed && item.IsFolder) tags.RemoveAll(t => t == deny);
            else if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) tags.Add(tag);
            if (item.Tags.SequenceEqual(tags)) continue;
            item.Tags = tags.ToArray();
            await library.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, ct);
        }
    }
}
