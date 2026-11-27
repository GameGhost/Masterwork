namespace Masterwork.App.Shared.Services;

/// <summary>
/// The voice language and voice track one playthrough is narrated in, chosen when it starts and
/// changed from in-game Options. Kept with its saves, apart from the global
/// <see cref="AppSettings.VoiceLanguage"/>, which only preselects the choice for a new game.
/// </summary>
/// <param name="Language">The voice language, or <see langword="null"/> for no narration.</param>
/// <param name="TrackId">The track picked in that language, or <see langword="null"/> for the one remembered for the module (<see cref="VoiceSelection.PreferredTrackId"/>).</param>
public sealed record SessionVoice(string? Language, string? TrackId = null)
{
    /// <summary>No narration.</summary>
    public static SessionVoice Off { get; } = new((string?)null);
}
