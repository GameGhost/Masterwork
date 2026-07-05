namespace Masterwork.Engine.Session;

/// <summary>A serializable snapshot of a <see cref="GameSession"/>'s full timeline, for save/load. See <see cref="GameSession.Serialize"/> and <see cref="GameSession.Restore"/>.</summary>
public sealed record SessionSave(long MasterSeed, IReadOnlyList<SessionSnapshot> Timeline, int HistoryIndex);
