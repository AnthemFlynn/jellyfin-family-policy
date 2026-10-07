using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace FamilyPolicy.Feasibility;

// TEST ONLY: synthetic classifications, no production account authorization.
public sealed class SpikePlugin : BasePlugin<BasePluginConfiguration>
{
    public SpikePlugin(IApplicationPaths paths, MediaBrowser.Model.Serialization.IXmlSerializer serializer) : base(paths, serializer) { }
    public override string Name => "Family Policy Feasibility Spike - TEST ONLY";
    public override Guid Id => Guid.Parse("a5bc25f7-43f1-4e5f-b1d3-7ad0334da831");
}
public sealed class Registration : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost host)
    {
        services.AddScoped<SpikeFilter>();
        services.Configure<MvcOptions>(o => o.Filters.AddService<SpikeFilter>(int.MinValue));
    }
}
public sealed class SpikeFilter : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        http.Response.Headers["X-Family-Policy-Spike"] = "loaded";
        var path = http.Request.Path.Value ?? "";
        if (!path.StartsWith("/PolicySpike", StringComparison.OrdinalIgnoreCase))
        {
            // Probe actual Jellyfin controllers before their actions (requires valid auth).
            if (http.Request.Headers.ContainsKey("X-Spike-Deny") && (path.StartsWith("/Videos/", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/PlaybackInfo", StringComparison.OrdinalIgnoreCase)))
            { context.Result = new StatusCodeResult(403); return; }
            await next(); return;
        }
        if (http.Connection.RemoteIpAddress is null || !IPAddress.IsLoopback(http.Connection.RemoteIpAddress))
        { context.Result = new StatusCodeResult(403); return; }
        // Missing classification denies; workbook exception is independent of media availability.
        if (http.Request.Query["kind"] != "workout")
        { context.Result = new StatusCodeResult(403); return; }
        using var stop = new CancellationTokenSource();
        var timer = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(3), stop.Token); http.Abort(); }
            catch (OperationCanceledException) { }
        });
        try { await next(); }
        finally { await stop.CancelAsync(); await timer; }
    }
}
[ApiController, Route("PolicySpike")]
public sealed class SpikeController : ControllerBase
{
    [HttpGet("Decision")]
    public IActionResult Decision() => Ok(new { allowed = true, reason = "synthetic workout exception" });
    [HttpGet("PhysicalFile")]
    public IActionResult Physical() => PhysicalFile(Path.Combine(Path.GetTempPath(), "family-policy-spike.bin"), "application/octet-stream", enableRangeProcessing: true);
    [HttpGet("Stream")]
    public IActionResult Stream() => File(new SlowStream(), "application/octet-stream");
}
public sealed class SlowStream : Stream
{
    public override bool CanRead => true; public override bool CanWrite => false; public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException(); public override long Position { get => 0; set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    { await Task.Delay(200, ct); var size = Math.Min(buffer.Length, 4096); buffer.Span[..size].Clear(); return size; }
    public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
    public override void Flush() { } public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long n) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
}
