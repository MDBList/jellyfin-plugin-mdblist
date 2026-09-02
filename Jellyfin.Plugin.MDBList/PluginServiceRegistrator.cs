using Jellyfin.Plugin.MDBList.Api;
using Jellyfin.Plugin.MDBList.Events;
using Jellyfin.Plugin.MDBList.Sync;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MDBList;

/// <summary>
/// Registers this plugin's services with Jellyfin's DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<MDBListApiClient>();
        serviceCollection.AddSingleton<OAuthService>();
        serviceCollection.AddSingleton<SyncStateStore>();
        serviceCollection.AddSingleton<SyncOrchestrator>();
        serviceCollection.AddSingleton<SyncPayloadBuilder>();
        serviceCollection.AddSingleton<WatchedSync>();
        serviceCollection.AddSingleton<RatingsSync>();
        serviceCollection.AddSingleton<LiveSyncService>();
        serviceCollection.AddHostedService<MDBListEventHostedService>();
    }
}
