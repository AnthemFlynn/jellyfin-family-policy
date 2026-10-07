using System.Security.Claims;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
namespace FamilyPolicy.Plugin;
/// <summary>Actor identity comes exclusively from validated Jellyfin claims.</summary>
public sealed class PlaybackFilter(PolicyStore store, PolicyService service, ILibraryManager library, GuardLease lease) : IAsyncResourceFilter, IAsyncResultFilter, IAsyncActionFilter
{
    private static readonly HashSet<string> StreamingControllers = new(StringComparer.OrdinalIgnoreCase) { "Videos", "Audio", "DynamicHls", "HlsSegment", "UniversalAudio", "Subtitle", "MediaInfo", "VideoAttachments", "Trickplay" };
    private static bool IsMediaRoute(FilterContext c) => c.ActionDescriptor is ControllerActionDescriptor d && (StreamingControllers.Contains(d.ControllerName) || d.ControllerName == "Library" && d.ActionName is "GetFile" or "GetDownload");
    private static Guid Actor(ClaimsPrincipal principal) => Guid.TryParse(principal.FindFirst("Jellyfin-UserId")?.Value, out var id) ? id : Guid.Empty;
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (IsMediaRoute(context) && http.User.Identity?.IsAuthenticated != true)
        {
            var auth = await http.AuthenticateAsync();
            if (!auth.Succeeded) { context.Result = new UnauthorizedResult(); return; }
            http.User = auth.Principal!;
        }
        var actor = Actor(http.User);
        if (store.Evaluator(actor) is null) { await next(); return; }
        if (!lease.Covers(actor)) { context.Result = new StatusCodeResult(503); return; }
        if (context.ActionDescriptor is ControllerActionDescriptor legacy && legacy.ControllerName == "HlsSegment" && legacy.ActionName.Contains("Legacy", StringComparison.Ordinal)) { context.Result = new StatusCodeResult(403); return; }
        // Native APIs may accept query/route user IDs; never let a managed caller select another identity.
        foreach (var candidate in new[] { context.RouteData.Values.GetValueOrDefault("userId")?.ToString(), http.Request.Query["userId"].ToString() })
            if (!string.IsNullOrEmpty(candidate) && (!Guid.TryParse(candidate, out var target) || target != actor)) { context.Result = new StatusCodeResult(403); return; }
        Guid itemId = Guid.Empty;
        foreach (var key in new[] { "itemId", "videoId", "routeItemId" })
            if (context.RouteData.Values.TryGetValue(key, out var value) && Guid.TryParse(value?.ToString(), out itemId)) break;
        var item = itemId == Guid.Empty ? null : library.GetItemById(itemId);
        var streaming = IsMediaRoute(context);
        if (item is null)
        {
            if (streaming) { context.Result = new StatusCodeResult(403); return; }
            await next(); return;
        }
        var decision = service.Decide(actor, item, DateTimeOffset.UtcNow);
        if (decision?.ContentAllowed != true || streaming && !decision.TimeAllowed)
        { context.Result = new StatusCodeResult(403); return; }
        if (!streaming) { await next(); return; }
        using var stop = new CancellationTokenSource();
        var monitor = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                while (await timer.WaitForNextTickAsync(stop.Token))
                    if (!lease.Covers(actor) || service.Decide(actor, item, DateTimeOffset.UtcNow)?.Allowed != true) { http.Abort(); break; }
            }
            catch (OperationCanceledException) { }
            catch { http.Abort(); } // Evaluation failure cannot leave a stream running.
        });
        try { await next(); } finally { await stop.CancelAsync(); await monitor; }
    }
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var actor = Actor(context.HttpContext.User);
        if (store.Evaluator(actor) is not null)
            foreach (var value in context.ActionArguments.Values)
            {
                var requested = value?.GetType().GetProperty("UserId")?.GetValue(value);
                if (requested is Guid id && id != Guid.Empty && id != actor) { context.Result = new StatusCodeResult(403); return; }
            }
        await next();
    }
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var actor = Actor(context.HttpContext.User);
        if (store.Evaluator(actor) is not null && context.Result is ObjectResult result)
        {
            bool Allowed(BaseItemDto dto)
            {
                var item = library.GetItemById(dto.Id);
                return item is not null && service.Decide(actor, item, DateTimeOffset.UtcNow)?.ContentAllowed == true;
            }
            if (result.Value is QueryResult<BaseItemDto> query)
            { var visible = query.Items.Where(Allowed).ToArray(); query.TotalRecordCount -= query.Items.Count - visible.Length; query.Items = visible; }
            else if (result.Value is BaseItemDto item && !Allowed(item)) context.Result = new NotFoundResult();
            else if (result.Value is IEnumerable<BaseItemDto> items) result.Value = items.Where(Allowed).ToArray();
        }
        await next();
    }
}
