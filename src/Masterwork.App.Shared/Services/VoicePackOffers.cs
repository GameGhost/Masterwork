using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Services;

/// <summary>What installing an offered voice pack would do.</summary>
public enum VoicePackOfferState
{
    /// <summary>The pack isn't installed.</summary>
    Available,

    /// <summary>An older version of the pack is installed.</summary>
    UpdateAvailable,

    /// <summary>The pack needs a newer version of its module than the one installed, so it can't be installed yet.</summary>
    NeedsModuleUpdate,
}

/// <summary>A catalog voice pack for an installed module.</summary>
/// <param name="Snapshot">The trusted catalog listing it, which installing goes through.</param>
/// <param name="ModuleUpdate">For <see cref="VoicePackOfferState.NeedsModuleUpdate"/>: the same catalog's listing of a module version the pack works with, if it has one.</param>
public sealed record VoicePackOffer(
    CatalogSnapshot Snapshot, CatalogEntry Entry, InstalledModule Module, VoicePackOfferState State, CatalogEntry? ModuleUpdate = null)
{
    /// <summary>The oldest module version the pack works with.</summary>
    public string MinModuleVersion => Entry.For!.MinVersion;

    /// <summary>Whether the pack is listed with <paramref name="language"/> among its voice languages (ignoring case).</summary>
    public bool HasLanguage(string language) => Entry.Languages.Contains(language, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Which catalog voice packs the player can install, and which one comes with a module install.</summary>
public static class VoicePackOffers
{
    /// <summary>
    /// The voice packs in trusted <paramref name="snapshots"/> for installed <paramref name="modules"/>,
    /// in catalog order. A pack that is installed at the listed version or a newer one isn't offered,
    /// since installed packs are never downgraded. When several catalogs list one pack id, the first
    /// one's listing is used, the precedence modules have too.
    /// </summary>
    public static IReadOnlyList<VoicePackOffer> From(
        IEnumerable<CatalogSnapshot> snapshots, IReadOnlyList<InstalledModule> modules, IReadOnlyList<VoicePackManifest> installed)
    {
        var offers = new List<VoicePackOffer>();
        var seen = new HashSet<string>();
        foreach (var snapshot in snapshots.Where(s => s.Decision == PackageTrustDecision.Trusted))
        {
            foreach (var entry in snapshot.Catalog.VoicePacks)
            {
                if (!seen.Add(entry.Id) || modules.FirstOrDefault(m => m.ModuleId == entry.For!.ModuleId) is not { } module)
                {
                    continue;
                }

                var current = installed.FirstOrDefault(p => p.Id == entry.Id);
                if (current is not null && SemVer.Compare(current.Version, entry.Version) >= 0)
                {
                    continue;
                }

                if (SemVer.Compare(module.Version, entry.For!.MinVersion) < 0)
                {
                    var update = snapshot.Catalog.Modules
                        .Where(m => m.Id == module.ModuleId && SemVer.Compare(m.Version, entry.For.MinVersion) >= 0)
                        .MaxBy(m => m.Version, Comparer<string>.Create(SemVer.Compare));
                    offers.Add(new VoicePackOffer(snapshot, entry, module, VoicePackOfferState.NeedsModuleUpdate, update));
                    continue;
                }

                offers.Add(new VoicePackOffer(snapshot, entry, module,
                    current is null ? VoicePackOfferState.Available : VoicePackOfferState.UpdateAvailable));
            }
        }

        return offers;
    }

    /// <summary>
    /// The pack to install along with <paramref name="module"/>: one in the player's effective voice
    /// language for it, chosen the way playback chooses (<see cref="VoiceSelection.SelectVoiceLanguage"/>)
    /// among the languages the module, its <paramref name="installed"/> packs and the installable
    /// <paramref name="offers"/> narrate in. <see langword="null"/> under None, or when that language
    /// is already narrated by what's installed, or when no installable pack has it.
    /// </summary>
    public static VoicePackOffer? ForModuleInstall(
        AppSettings settings, InstalledModule module, IReadOnlyList<VoicePackOffer> offers, IReadOnlyList<VoicePackManifest> installed)
    {
        var installable = offers
            .Where(o => o.Module.ModuleId == module.ModuleId && o.State != VoicePackOfferState.NeedsModuleUpdate)
            .ToList();
        var narrated = module.VoiceLanguages
            .Concat(installed
                .Where(p => p.For.ModuleId == module.ModuleId && VoiceSelection.SupportsModuleVersion(p, module.Version))
                .SelectMany(p => p.Voices.Select(v => v.Language)))
            .ToList();

        var textLanguage = VoiceSelection.SelectTextLanguage(module.AvailableLanguages, settings.PreferredModuleLanguage, settings.UiLocale, module.DefaultLocale);
        var language = VoiceSelection.SelectVoiceLanguage(
            settings.VoiceLanguage, textLanguage, settings.UiLocale, module.DefaultLocale,
            narrated.Concat(installable.SelectMany(o => o.Entry.Languages)));

        if (language is null || narrated.Contains(language, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return installable.FirstOrDefault(o => o.HasLanguage(language));
    }
}
