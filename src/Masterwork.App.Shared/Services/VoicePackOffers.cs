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

                if (!VoiceSelection.SupportsModuleVersion(entry.For!, module.Version))
                {
                    var update = NewestModuleListing(snapshot.Catalog, module.ModuleId, entry.For!.MinVersion);
                    offers.Add(new VoicePackOffer(snapshot, entry, module, VoicePackOfferState.NeedsModuleUpdate, update));
                    continue;
                }

                offers.Add(new VoicePackOffer(snapshot, entry, module,
                    current is null ? VoicePackOfferState.Available : VoicePackOfferState.UpdateAvailable));
            }
        }

        return offers;
    }

    /// <summary>The newest listing in <paramref name="catalog"/> of <paramref name="moduleId"/> at <paramref name="minVersion"/> or later, or <see langword="null"/>.</summary>
    public static CatalogEntry? NewestModuleListing(CatalogDocument catalog, string moduleId, string minVersion) =>
        catalog.Modules
            .Where(m => m.Id == moduleId && SemVer.Compare(m.Version, minVersion) >= 0)
            .MaxBy(m => m.Version, Comparer<string>.Create(SemVer.Compare));

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
        var textLanguage = VoiceSelection.SelectTextLanguage(module.AvailableLanguages, settings.PreferredModuleLanguage, settings.UiLocale, module.DefaultLocale);
        var language = VoiceSelection.SelectVoiceLanguage(
            settings.VoiceLanguage, textLanguage, settings.UiLocale, module.DefaultLocale, VoiceLanguages(module, offers, installed));

        return NeedFor(module, language, offers, installed) is { Kind: VoicePackNeedKind.Missing } need ? need.Offer : null;
    }

    /// <summary>
    /// The languages <paramref name="module"/> can be narrated in: those its own tracks and its
    /// <paramref name="installed"/> packs narrate, then those an installable offer would add, each once.
    /// </summary>
    public static IReadOnlyList<string> VoiceLanguages(
        InstalledModule module, IReadOnlyList<VoicePackOffer> offers, IReadOnlyList<VoicePackManifest> installed) =>
        [.. NarratedLanguages(module, installed)
            .Concat(Installable(module, offers).SelectMany(o => o.Entry.Languages))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// What playing <paramref name="module"/> narrated in <paramref name="language"/> needs first:
    /// nothing, the download of an offered pack, or nothing possible. When an installed pack already
    /// narrates it, the update that pack has on offer, if any. <see langword="null"/> is no narration.
    /// </summary>
    public static VoicePackNeed NeedFor(
        InstalledModule module, string? language, IReadOnlyList<VoicePackOffer> offers, IReadOnlyList<VoicePackManifest> installed)
    {
        if (language is null)
        {
            return new VoicePackNeed(VoicePackNeedKind.Ready);
        }

        var narratingPacks = Usable(module, installed).Where(p => p.Voices.Any(v => Same(v.Language, language))).ToList();
        if (narratingPacks.Count > 0 || module.VoiceLanguages.Any(l => Same(l, language)))
        {
            var update = offers.FirstOrDefault(o => o.State == VoicePackOfferState.UpdateAvailable && narratingPacks.Any(p => p.Id == o.Entry.Id));
            return update is null ? new VoicePackNeed(VoicePackNeedKind.Ready) : new VoicePackNeed(VoicePackNeedKind.UpdateAvailable, update);
        }

        if (Installable(module, offers).FirstOrDefault(o => o.HasLanguage(language)) is { } missing)
        {
            return new VoicePackNeed(VoicePackNeedKind.Missing, missing);
        }

        return new VoicePackNeed(VoicePackNeedKind.Unavailable,
            offers.FirstOrDefault(o => o.Module.ModuleId == module.ModuleId && o.HasLanguage(language)));
    }

    private static IEnumerable<string> NarratedLanguages(InstalledModule module, IReadOnlyList<VoicePackManifest> installed) =>
        module.VoiceLanguages.Concat(Usable(module, installed).SelectMany(p => p.Voices.Select(v => v.Language)));

    private static IEnumerable<VoicePackManifest> Usable(InstalledModule module, IReadOnlyList<VoicePackManifest> installed) =>
        installed.Where(p => p.For.ModuleId == module.ModuleId && VoiceSelection.SupportsModuleVersion(p, module.Version));

    private static IEnumerable<VoicePackOffer> Installable(InstalledModule module, IReadOnlyList<VoicePackOffer> offers) =>
        offers.Where(o => o.Module.ModuleId == module.ModuleId && o.State != VoicePackOfferState.NeedsModuleUpdate);

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>What <see cref="VoicePackOffers.NeedFor"/> found playing with a voice needs.</summary>
public enum VoicePackNeedKind
{
    /// <summary>Nothing: no narration, or what's installed narrates it and has no update on offer.</summary>
    Ready,

    /// <summary>What's installed narrates it, and an update of the pack doing so is on offer.</summary>
    UpdateAvailable,

    /// <summary>Nothing installed narrates it, but an offered pack would.</summary>
    Missing,

    /// <summary>Nothing installed narrates it, and no pack that can be installed would.</summary>
    Unavailable,
}

/// <summary>What playing with a voice needs, with the offer that would provide it.</summary>
/// <param name="Offer">The update (<see cref="VoicePackNeedKind.UpdateAvailable"/>) or pack (<see cref="VoicePackNeedKind.Missing"/>) to install; for <see cref="VoicePackNeedKind.Unavailable"/>, a pack that needs a newer module, if one is listed.</param>
public sealed record VoicePackNeed(VoicePackNeedKind Kind, VoicePackOffer? Offer = null);
