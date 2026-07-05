namespace Masterwork.Engine;

/// <summary>The kind of event that produced a <see cref="SessionSnapshot"/>.</summary>
public enum SnapshotKind
{
    /// <summary>The very first snapshot, captured before the start passage renders.</summary>
    GameStart,

    /// <summary>Following a state-affecting <c>navigation</c> link.</summary>
    Choice,

    /// <summary>Submitting an <c>input</c> node's value.</summary>
    InputReceived,

    /// <summary>A named <c>checkpoint</c> node bookmark.</summary>
    Checkpoint,
}
