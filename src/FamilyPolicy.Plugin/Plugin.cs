using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace FamilyPolicy.Plugin;

public sealed class Plugin : BasePlugin<BasePluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer) { }
    public override string Name => "Family Policy";
    public override string Description => "Administrator-managed content and viewing-time policies. Experimental preview.";
    public override Guid Id => Guid.Parse("fcfb1739-6a6e-47c5-9448-bbc1c1c9fcb7");
    public IEnumerable<PluginPageInfo> GetPages() => [new() { Name = "FamilyPolicy", EmbeddedResourcePath = "FamilyPolicy.Plugin.Web.settings.html" }];
}
public sealed class Registration : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost host)
    {
        services.AddSingleton<PolicyStore>();
        services.AddSingleton<GuardLease>();
        services.AddSingleton<PolicyService>();
        services.AddScoped<PlaybackFilter>();
        services.Configure<MvcOptions>(o => o.Filters.AddService<PlaybackFilter>(int.MinValue));
        services.AddHostedService<ReconcileWorker>();
    }
}
