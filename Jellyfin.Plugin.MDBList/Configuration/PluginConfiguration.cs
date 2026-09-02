using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MDBList.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the config page round-trips correctly.
    /// Placeholder for Phase 1 -- replaced by real settings (auth, sync toggles) in later phases.
    /// </summary>
    public bool DevPlaceholderEnabled { get; set; }
}
