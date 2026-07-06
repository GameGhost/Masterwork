namespace Masterwork.Engine.Session;

/// <summary>Mutable, transient UI state. Discarded whenever the timeline position changes.</summary>
public sealed class SessionViewState
{
    /// <summary>Action IDs of popups the player has opened.</summary>
    public HashSet<string> ExpandedPopups { get; } = [];

    /// <summary>In-progress (not yet submitted) input values, keyed by action ID.</summary>
    public Dictionary<string, object> InputDrafts { get; } = [];

    /// <summary>Clears all transient view state.</summary>
    public void Reset()
    {
        ExpandedPopups.Clear();
        InputDrafts.Clear();
    }
}
