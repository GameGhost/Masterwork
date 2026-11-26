using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Services;

/// <summary>Why a voice pack can't be installed.</summary>
public enum VoicePackRefusalKind
{
    /// <summary>The pack needs a newer version of its module than the one installed.</summary>
    NeedsModuleVersion,

    /// <summary>Another installed pack for the same module already adds one of its tracks.</summary>
    TrackAlreadyInstalled,
}

/// <summary>A refused voice pack install.</summary>
/// <param name="ModuleVersion">For <see cref="VoicePackRefusalKind.NeedsModuleVersion"/>: the oldest module version the pack works with.</param>
/// <param name="Track">For <see cref="VoicePackRefusalKind.TrackAlreadyInstalled"/>: the track already installed.</param>
/// <param name="InstalledBy">For <see cref="VoicePackRefusalKind.TrackAlreadyInstalled"/>: the title of the pack that installed it.</param>
public sealed record VoicePackRefusal(VoicePackRefusalKind Kind, string? ModuleVersion = null, VoiceTrack? Track = null, string? InstalledBy = null);

/// <summary>Whether a voice pack may be installed or updated next to what is already on the device.</summary>
public static class VoicePackInstallRules
{
    /// <summary>
    /// <see langword="null"/> when <paramref name="candidate"/> may be installed, else why not. Its
    /// minimum module version must be met by <paramref name="module"/> (the installed copy of the
    /// module it voices, if any); the next major version isn't checked. No track of it may already be
    /// added by a different pack for the same module. An installed pack with the same id is the one
    /// being updated, so its tracks don't count.
    /// </summary>
    public static VoicePackRefusal? Check(VoicePackManifest candidate, InstalledModule? module, IReadOnlyList<VoicePackManifest> installed)
    {
        if (module is not null && !VoiceSelection.SupportsModuleVersion(candidate, module.Version))
        {
            return new VoicePackRefusal(VoicePackRefusalKind.NeedsModuleVersion, ModuleVersion: candidate.For.MinVersion);
        }

        foreach (var other in installed.Where(p => p.Id != candidate.Id && p.For.ModuleId == candidate.For.ModuleId))
        {
            var clash = candidate.Voices.FirstOrDefault(t => other.Voices.Any(o =>
                string.Equals(o.Language, t.Language, StringComparison.OrdinalIgnoreCase) && o.Id == t.Id));
            if (clash is not null)
            {
                return new VoicePackRefusal(VoicePackRefusalKind.TrackAlreadyInstalled, Track: clash, InstalledBy: other.Title);
            }
        }

        return null;
    }
}
