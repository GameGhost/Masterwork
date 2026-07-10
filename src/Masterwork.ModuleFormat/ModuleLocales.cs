namespace Masterwork.ModuleFormat;

/// <summary>
/// Module-content locale fallback (masterwork-plan Section 11): player's preferred module language
/// → the module's own default (<see cref="Default"/>) → whatever's actually there. Distinct from
/// the app-shell's own UI language (Section 13/Milestone A.1), which is a separate setting entirely.
/// </summary>
public static class ModuleLocales
{
    /// <summary>The module-content default locale — matches the extractor's own convention.</summary>
    public const string Default = "en-US";

    /// <summary>
    /// Picks which locale key a module's content should resolve against: <paramref name="preferredLocale"/>
    /// if the module has content for it, else <see cref="Default"/>, else whatever's actually there.
    /// Use the returned key to look up both <c>RestextByLocale</c> and <c>RestextOverridesByLocale</c>
    /// (see <see cref="ModulePackage"/>) so overrides are applied for the same locale that got picked.
    /// </summary>
    public static string? SelectLocale(IReadOnlyDictionary<string, string> restextByLocale, string? preferredLocale)
    {
        if (preferredLocale is not null && restextByLocale.ContainsKey(preferredLocale))
        {
            return preferredLocale;
        }

        if (restextByLocale.ContainsKey(Default))
        {
            return Default;
        }

        return restextByLocale.Keys.FirstOrDefault();
    }

    /// <summary>Picks which locale's restext text to resolve a module's passages against.</summary>
    public static string? SelectRestext(IReadOnlyDictionary<string, string> restextByLocale, string? preferredLocale)
    {
        var locale = SelectLocale(restextByLocale, preferredLocale);
        return locale is not null ? restextByLocale[locale] : null;
    }

    /// <summary>All locales a package actually has content for, sorted for stable display — never empty, since a module with no <c>.restext</c> at all is implicitly single-language (its passage text is inline, not <c>restext://</c>-referenced).</summary>
    public static IReadOnlyList<string> SortedLocales(IReadOnlyDictionary<string, string> restextByLocale) =>
        restextByLocale.Count == 0
            ? [Default]
            : [.. restextByLocale.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)];
}
