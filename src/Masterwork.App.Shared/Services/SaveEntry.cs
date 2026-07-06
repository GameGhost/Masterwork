namespace Masterwork.App.Shared.Services;

/// <summary>
/// Metadata for one save, listable independently of the full <see cref="Masterwork.Engine.Session.SessionSave"/>
/// JSON blob it describes — see <see cref="ISaveStore.ListAsync"/>. Every module has at most one
/// <see cref="IsAutosave"/> entry (id <see cref="SaveIds.Autosave"/>), continuously overwritten
/// while playing; named saves are created only via the in-game "Save and Quit" flow and are never
/// touched automatically again.
/// </summary>
public sealed record SaveEntry(
    string Id,
    string ModuleId,
    string ModuleVersion,
    bool IsAutosave,
    string? DisplayName,
    string? LastStateLabel,
    DateTimeOffset LastPlayedUtc
);
