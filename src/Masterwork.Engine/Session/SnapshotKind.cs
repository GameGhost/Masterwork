using System.Text.Json.Serialization;

namespace Masterwork.Engine.Session;

/// <summary>The kind of event that produced a <see cref="SessionSnapshot"/>.</summary>
/// <remarks>
/// Written by name, not ordinal — <see cref="SessionSnapshot"/> is persisted to save files via
/// plain <c>System.Text.Json</c> (no custom converter registered at the call site), so a future
/// member addition/removal shifting ordinals can't silently change what a *newly written* save's
/// stored value means the way removing <c>InputReceived</c> during the MWS v0.4 rework did (a
/// pre-existing autosave's <c>Kind: 2</c>, meaning <c>InputReceived</c> under the old ordinals,
/// silently read back as <c>Checkpoint</c>). Note this does <em>not</em> retroactively protect
/// already-written int-based saves on read: <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/>
/// still accepts a raw JSON number on deserialize (it only restricts <em>writing</em> to strings),
/// so an old save's stale ordinal is silently reinterpreted exactly as before, with no exception.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SnapshotKind
{
    /// <summary>The very first snapshot, captured before the start passage renders.</summary>
    GameStart,

    /// <summary>Following a state-affecting <c>link</c>, or closing a state-affecting <c>popup</c> via Okay.</summary>
    Choice,

    /// <summary>A named <c>checkpoint</c> node bookmark.</summary>
    Checkpoint,
}
