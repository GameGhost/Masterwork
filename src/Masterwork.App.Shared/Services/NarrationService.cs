using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Chooses the voice track for the active session (<see cref="GameSessionState.Voice"/>) and lets the
/// play screen pause narration. The choice comes from <see cref="AppSettings"/> and is never part of
/// the session or its saves.
/// </summary>
public sealed class NarrationService(
    GameSessionState state, IInstalledVoicePacks voicePacks, IAppSettingsStore settingsStore, ILogger<NarrationService>? logger = null)
{
    private readonly ILogger<NarrationService> _logger = logger ?? NullLogger<NarrationService>.Instance;

    /// <summary>Raised by <see cref="PauseAll"/>; every playing <c>audio_track</c> pauses itself.</summary>
    public event Action? PauseRequested;

    /// <summary>Pauses every playing <c>audio_track</c>. Background music is unaffected.</summary>
    public void PauseAll() => PauseRequested?.Invoke();

    /// <summary>
    /// Collects the tracks the active module offers (its own and its installed voice packs') and
    /// selects one from the player's settings. A voice pack that can't be read is skipped and logged;
    /// it never blocks play.
    /// </summary>
    public async Task ActivateAsync()
    {
        if (state.Module is not { } module || state.ModuleId is not { } moduleId)
        {
            return;
        }

        IReadOnlyList<InstalledVoicePack> packs;
        try
        {
            packs = await voicePacks.ListForModuleAsync(moduleId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the voice packs installed for {ModuleId}; using the module's own voice tracks only", moduleId);
            packs = [];
        }

        var moduleVersion = state.ModuleVersion ?? "0.0.0";
        foreach (var pack in packs.Where(p => !VoiceSelection.SupportsModuleVersion(p.Manifest, moduleVersion)))
        {
            _logger.LogInformation("Voice pack {PackId} needs {ModuleId} {MinVersion} or later (installed: {ModuleVersion}); not offering its tracks",
                pack.Manifest.Id, moduleId, pack.Manifest.For.MinVersion, moduleVersion);
        }

        var offered = VoiceSelection.Offered(moduleVersion, module.Voices, state.Assets, packs);
        state.SetVoices(offered, null, null);
        await ReselectAsync();
    }

    /// <summary>Re-reads the settings and selects the matching track. Returns whether the selection changed.</summary>
    public async Task<bool> ReselectAsync()
    {
        if (state.ModuleId is not { } moduleId)
        {
            return false;
        }

        var settings = await settingsStore.LoadAsync();
        var previous = state.Voice;
        var track = VoiceSelection.Select(settings, moduleId, state.Language, [.. state.Voices.Select(v => v.Track)]);
        state.SetVoices(state.Voices, track?.Language, track?.Id);

        if (state.Voice?.Track != previous?.Track)
        {
            _logger.LogInformation("Narration for {ModuleId}: {Voice}", moduleId,
                track is null ? "none" : $"{track.Language}/{track.Id}");
            return true;
        }

        return false;
    }
}
