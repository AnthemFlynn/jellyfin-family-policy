using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace FamilyPolicy.Plugin;

public sealed class ReconcileWorker(PolicyService service, PolicyStore store, GuardLease lease, ISessionManager sessions, ITranscodeManager transcodes, ILogger<ReconcileWorker> logger, ILibraryManager library) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var nextReconcile = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                foreach (var session in sessions.Sessions.ToArray())
                {
                    var id = session.UserId;
                    if (store.Evaluator(id) is null || session.NowPlayingItem is null) continue;
                    var item = serviceItem(session.NowPlayingItem.Id);
                    if (!lease.Healthy || item is null || service.Decide(id, item, DateTimeOffset.UtcNow)?.Allowed != true)
                    {
                        await sessions.SendPlaystateCommand(session.Id, session.Id, new() { Command = PlaystateCommand.Stop }, ct);
                        if (!string.IsNullOrEmpty(session.DeviceId)) await transcodes.KillTranscodingJobs(session.DeviceId, null, _ => true);
                    }
                }
                if (DateTimeOffset.UtcNow >= nextReconcile)
                { await service.Reconcile(ct); nextReconcile = DateTimeOffset.UtcNow.AddSeconds(30); }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Family Policy background enforcement failed; request checks remain active."); }
        }
    }
    private MediaBrowser.Controller.Entities.BaseItem? serviceItem(Guid id) => library.GetItemById(id);
}
