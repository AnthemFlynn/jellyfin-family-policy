using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyPolicy.Core;
using MediaBrowser.Common.Configuration;
namespace FamilyPolicy.Plugin;

public sealed record AccountPolicy(Guid UserId, Policy Policy, string OriginalPolicy);
public sealed record AuditEntry(DateTimeOffset At, string Actor, Guid TargetId, string Action, long Revision, string TargetKind = "Account");
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TrustedLabels(string[]? Categories = null, string[]? Subjects = null, string[]? Franchises = null);
public sealed record StoreSnapshot(long Revision, AccountPolicy[] Accounts, AuditEntry[] Audit, Dictionary<Guid, TrustedLabels>? Labels = null);
/// <summary>Atomic on-disk commit before publication. Credential values never enter this store.</summary>
public sealed class PolicyStore
{
    private readonly object gate = new();
    private readonly string path;
    private StoreSnapshot snapshot;
    private Dictionary<Guid, PolicyEvaluator> evaluators = [];
    public PolicyStore(IApplicationPaths paths)
    {
        path = Path.Combine(paths.PluginConfigurationsPath, "family-policy-state.json");
        Directory.CreateDirectory(paths.PluginConfigurationsPath);
        snapshot = File.Exists(path) ? JsonSerializer.Deserialize<StoreSnapshot>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid policy state.") : new(0, [], []);
        foreach (var account in snapshot.Accounts) evaluators.Add(account.UserId, new(account.Policy));
    }
    public StoreSnapshot Read() { lock (gate) return snapshot; }
    public PolicyEvaluator? Evaluator(Guid id) { lock (gate) return evaluators.GetValueOrDefault(id); }
    public (PolicyEvaluator? Evaluator, Dictionary<Guid, TrustedLabels>? Labels) Context(Guid id) { lock (gate) return (evaluators.GetValueOrDefault(id), snapshot.Labels); }
    public void Set(Guid id, Policy? policy, string original, long expected, string actor)
    {
        lock (gate)
        {
            if (expected != snapshot.Revision) throw new RevisionConflictException();
            var evaluator = policy is null ? null : new PolicyEvaluator(policy);
            var entries = snapshot.Accounts.Where(a => a.UserId != id).ToList();
            if (policy is not null) entries.Add(new(id, JsonSerializer.Deserialize<Policy>(JsonSerializer.Serialize(policy))!, original));
            var next = new StoreSnapshot(snapshot.Revision + 1, entries.ToArray(), [.. snapshot.Audit.TakeLast(499), new AuditEntry(DateTimeOffset.UtcNow, actor, id, policy is null ? "restore" : "configure", snapshot.Revision + 1)], snapshot.Labels);
            Persist(next);
            var nextEvaluators = new Dictionary<Guid, PolicyEvaluator>(evaluators);
            if (evaluator is null) nextEvaluators.Remove(id); else nextEvaluators[id] = evaluator;
            evaluators = nextEvaluators;
        }
    }
    public void SetLabels(Guid item, TrustedLabels labels, long expected, string actor)
    {
        static void Check(string[]? values) { if (values is not null && (values.Length > 64 || values.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 128))) throw new ArgumentException("Invalid label values."); }
        Check(labels.Categories); Check(labels.Subjects); Check(labels.Franchises);
        lock (gate)
        {
            if (expected != snapshot.Revision) throw new RevisionConflictException();
            var annotations = new Dictionary<Guid, TrustedLabels>(snapshot.Labels ?? []) { [item] = JsonSerializer.Deserialize<TrustedLabels>(JsonSerializer.Serialize(labels))! };
            Persist(snapshot with { Revision = snapshot.Revision + 1, Labels = annotations, Audit = [.. snapshot.Audit.TakeLast(499), new(DateTimeOffset.UtcNow, actor, item, "label", snapshot.Revision + 1, "Media")] });
        }
    }
    private void Persist(StoreSnapshot next)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                JsonSerializer.Serialize(stream, next); stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }

        snapshot = next;
    }

}
public sealed class RevisionConflictException : Exception;
