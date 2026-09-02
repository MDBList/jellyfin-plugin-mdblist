namespace Jellyfin.Plugin.MDBList.Sync;

/// <summary>
/// Result of a single-item live push -- replaces the Python addon's
/// False/{}/dict tri-state return, which once caused a real bug
/// (an unmappable item and a harmless already-in-sync no-op were both
/// treated as "not pushed", but a caller mistook one for success). An enum
/// makes that confusion unrepresentable.
/// </summary>
public enum PushOutcome
{
    /// <summary>
    /// The item has no id this addon can map to an MDBList provider (e.g. an
    /// anime movie identified only by a Kitsu id Jellyfin doesn't expose).
    /// </summary>
    NotMappable,

    /// <summary>
    /// Already in sync -- nothing needed pushing.
    /// </summary>
    NoOp,

    /// <summary>
    /// The item was pushed as newly active (watched/rated/collected).
    /// </summary>
    Added,

    /// <summary>
    /// The item was pushed as removed (unwatched/unrated/uncollected).
    /// </summary>
    Removed,
}
