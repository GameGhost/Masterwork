using System.Text.Json;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// The file format written by <c>ContinueList.razor</c>'s Export button and read back by its Import
/// button — bundles the <see cref="SaveEntry"/> metadata a bare <see cref="Masterwork.Engine.Session.SessionSave"/>
/// blob doesn't carry (module id/version, display name, language, ...) alongside the session JSON
/// itself, so an imported file can be validated against whatever module is installed on the
/// importing machine without the importer needing any other context. <see cref="Session"/> is kept
/// as a raw <see cref="JsonElement"/> (not re-parsed into <see cref="Masterwork.Engine.Session.SessionSave"/>
/// here) purely to avoid a round-trip through that type at export time — <c>ISaveStore</c> only ever
/// deals in save JSON as opaque strings.
/// </summary>
public sealed record SaveExport(
    string ModuleId,
    string ModuleVersion,
    string? DisplayName,
    string? LastStateLabel,
    DateTimeOffset LastPlayedUtc,
    string? Language,
    JsonElement Session
);
