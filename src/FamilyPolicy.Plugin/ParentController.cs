using FamilyPolicy.Core;
using System.Text.Json.Serialization;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FamilyPolicy.Plugin;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfigureRequest(long ExpectedRevision, Policy Policy);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LabelsRequest(long ExpectedRevision, TrustedLabels Labels);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HeartbeatRequest(Guid[] UserIds);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RestoreRequest(long ExpectedRevision);
[ApiController, Route("FamilyPolicy"), Authorize(Policy = Policies.RequiresElevation)]
public sealed class ParentController(PolicyStore store, PolicyService service, ILibraryManager library, GuardLease lease) : ControllerBase
{
    private string Actor => User.FindFirst("Jellyfin-IsApiKey")?.Value == "True" ? "api-key" : User.FindFirst("Jellyfin-UserId")?.Value ?? "unknown";
    [HttpPost("Heartbeat")] public IActionResult Heartbeat(HeartbeatRequest request) { if (request.UserIds is null || request.UserIds.Length is < 1 or > 100 || request.UserIds.Contains(Guid.Empty)) return BadRequest(); lease.Renew(request.UserIds); return Ok(new { healthy = true }); }
    [HttpGet("Health")] public IActionResult Health() => Ok(new { loaded = true, guardHealthy = lease.Healthy, version = "0.1.0-preview" });
    [HttpGet("State")]
    public IActionResult State()
    {
        var snapshot = store.Read();
        return Ok(new { snapshot.Revision, Accounts = snapshot.Accounts.Select(a => new { a.UserId, a.Policy }), snapshot.Audit, snapshot.Labels });
    }
    [HttpPut("Labels/{itemId:guid}")]
    public IActionResult Labels(Guid itemId, LabelsRequest request)
    {
        if (library.GetItemById(itemId) is null) return NotFound();
        try { store.SetLabels(itemId, request.Labels, request.ExpectedRevision, Actor); return Ok(new { store.Read().Revision }); }
        catch (RevisionConflictException) { return Conflict(); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }
    [HttpPut("Accounts/{userId:guid}")]
    public async Task<IActionResult> Configure(Guid userId, ConfigureRequest request, CancellationToken ct)
    {
        if (!lease.Covers(userId)) return StatusCode(503, new { error = "Start the external health guard before enrolling accounts." });
        try { await service.Configure(userId, request.Policy, request.ExpectedRevision, Actor, ct); return Ok(new { store.Read().Revision }); }
        catch (RevisionConflictException) { return Conflict(new { error = "Configuration changed; reload and retry." }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (TimeZoneNotFoundException) { return BadRequest(new { error = "Unknown timezone." }); }
    }
    [HttpPost("Accounts/{userId:guid}/Restore")]
    public async Task<IActionResult> Restore(Guid userId, RestoreRequest request, CancellationToken ct)
    {
        try { await service.Restore(userId, request.ExpectedRevision, Actor, ct); return Ok(new { store.Read().Revision }); }
        catch (RevisionConflictException) { return Conflict(); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }
    [HttpPost("Preview/{itemId:guid}")]
    public IActionResult Preview(Guid itemId, Policy policy)
    {
        var item = library.GetItemById(itemId); if (item is null) return NotFound();
        try { return Ok(new PolicyEvaluator(policy).Evaluate(service.Facts(item), DateTimeOffset.UtcNow)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (TimeZoneNotFoundException) { return BadRequest(new { error = "Unknown timezone." }); }
    }
    [HttpPost("Reconcile")]
    public async Task<IActionResult> Reconcile(CancellationToken ct) { await service.Reconcile(ct); return NoContent(); }
}
