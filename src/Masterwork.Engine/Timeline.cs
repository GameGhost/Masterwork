using System.Collections.Generic;

namespace Masterwork.Engine;

public enum SnapshotKind { GameStart, Choice, InputReceived, Checkpoint }

// Immutable point-in-time capture. Variables/SeedOccurrences always reflect state as of JUST
// BEFORE PassageId was rendered — restoring a snapshot and re-rendering its passage reproduces
// the exact same output deterministically (same starting state -> same assigns -> same randoms).
public sealed record SessionSnapshot
{
    public required string PassageId { get; init; }
    public required SnapshotKind Kind { get; init; }
    public required IReadOnlyDictionary<string, ExprValue> Variables { get; init; }
    public required IReadOnlyDictionary<string, int> SeedOccurrences { get; init; }
    public ExprValue? SubmittedInput { get; init; }
    public string? DisplayLabel { get; init; }
    public string? DiagnosticLabel { get; init; }
}

// Mutable, transient UI state. Discarded whenever the timeline position changes.
public sealed class SessionViewState
{
    public HashSet<string> ExpandedPopups { get; } = [];
    public HashSet<string> ConfirmedGates { get; } = [];
    public Dictionary<string, object> InputDrafts { get; } = [];

    public void Reset()
    {
        ExpandedPopups.Clear();
        ConfirmedGates.Clear();
        InputDrafts.Clear();
    }
}

public sealed record SessionSave(long MasterSeed, IReadOnlyList<SessionSnapshot> Timeline, int HistoryIndex);
