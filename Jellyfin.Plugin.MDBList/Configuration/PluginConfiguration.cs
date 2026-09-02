using System.Collections.ObjectModel;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MDBList.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets the linked users. V1 only ever holds one entry -- see
    /// <see cref="UserSyncConfig"/>. Mutate via Add/Remove on the existing
    /// collection, never reassign -- XmlSerializer round-trips this by
    /// populating the getter's instance, not by calling a setter.
    /// </summary>
    public Collection<UserSyncConfig> Users { get; } = new();
}
