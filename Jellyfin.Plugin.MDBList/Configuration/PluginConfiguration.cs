using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MDBList.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the linked users -- one entry per Jellyfin user who has
    /// connected their own MDBList account, see <see cref="UserSyncConfig"/>.
    ///
    /// Settable rather than the usual get-only-collection pattern: the
    /// config page saves settings through Jellyfin's own
    /// <c>POST /Plugins/{id}/Configuration</c> endpoint (via
    /// <c>ApiClient.updatePluginConfiguration</c>), which deserializes the
    /// request body with System.Text.Json using Jellyfin's own default
    /// options -- no <c>PreferredObjectCreationHandling.Populate</c>, and
    /// unlike SyncStateStore/MDBListApiClient's own JSON paths, this one
    /// isn't ours to configure. Without a setter, that round-trip silently
    /// deserializes an empty collection and erases every linked user on the
    /// next save -- confirmed live against the dev server, not assumed.
    /// </summary>
    [SuppressMessage("Design", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Must be settable for System.Text.Json to populate it via Jellyfin's own config API -- see remarks above.")]
    public Collection<UserSyncConfig> Users { get; set; } = new();
}
