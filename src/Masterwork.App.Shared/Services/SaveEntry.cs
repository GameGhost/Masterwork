namespace Masterwork.App.Shared.Services;

/// <summary>
/// Metadata for one save, listable independently of the full <see cref="Masterwork.Engine.Session.SessionSave"/>
/// JSON blob it describes — see <see cref="ISaveStore.ListAsync"/>. Every module has at most one
/// <see cref="IsAutosave"/> entry (id <see cref="SaveIds.Autosave"/>), continuously overwritten
/// while playing; named saves are created only via the in-game "Save and Quit" flow and are never
/// touched automatically again.
/// </summary>
/// <param name="Voice">The playthrough's voice, or <see langword="null"/> for a save made before voices were saved.</param>
public sealed record SaveEntry(
    string Id,
    string ModuleId,
    string ModuleVersion,
    bool IsAutosave,
    string? DisplayName,
    string? LastStateLabel,
    DateTimeOffset LastPlayedUtc,
    string? Language,
    SessionVoice? Voice = null
);
