namespace Masterwork.Engine.Session;

/// <summary>A serializable snapshot of a <see cref="GameSession"/>'s full timeline, for save/load. See <see cref="GameSession.Serialize"/> and <see cref="GameSession.Restore"/>.</summary>
/// <param name="ActiveState">
/// The live edge's pending active state at save time, if any (see <see cref="Session.ActiveState"/>'s
/// remarks) — <see langword="null"/> for saves written before this existed, which restore exactly
/// as before. Included so resuming a save taken mid-tie-break (or any other chain of
/// non-state-affecting transitions) doesn't silently lose that progress back to the bare anchor. An
/// autosave that happens to fire mid-chain captures it too — harmless, since at worst it's
/// identical to whatever the last real snapshot already holds.
/// </param>
public sealed record SessionSave(long MasterSeed, IReadOnlyList<SessionSnapshot> Timeline, int HistoryIndex, ActiveState? ActiveState = null);
