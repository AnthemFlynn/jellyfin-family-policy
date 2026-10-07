namespace FamilyPolicy.Plugin;

public sealed class GuardLease
{
    private long expires;
    private HashSet<Guid> targets = [];
    public bool Healthy => System.Diagnostics.Stopwatch.GetTimestamp() < Interlocked.Read(ref expires);
    public bool Covers(Guid id) => Healthy && Volatile.Read(ref targets).Contains(id);
    public void Renew(Guid[] users) { Volatile.Write(ref targets, users.ToHashSet()); Interlocked.Exchange(ref expires, System.Diagnostics.Stopwatch.GetTimestamp() + 5 * System.Diagnostics.Stopwatch.Frequency); }
}
