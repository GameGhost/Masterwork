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

    /// <summary>Picks which locale's restext text to resolve a module's passages against.</summary>
    public static string? SelectRestext(IReadOnlyDictionary<string, string> restextByLocale, string? preferredLocale)
    {
        if (preferredLocale is not null && restextByLocale.TryGetValue(preferredLocale, out var preferred))
        {
            return preferred;
        }

        if (restextByLocale.TryGetValue(Default, out var moduleDefault))
        {
            return moduleDefault;
        }

        return restextByLocale.Values.FirstOrDefault();
    }

    /// <summary>All locales a package actually has content for, sorted for stable display — never empty, since a module with no <c>.restext</c> at all is implicitly single-language (its passage text is inline, not <c>restext://</c>-referenced).</summary>
    public static IReadOnlyList<string> SortedLocales(IReadOnlyDictionary<string, string> restextByLocale) =>
        restextByLocale.Count == 0
            ? [Default]
            : [.. restextByLocale.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)];
}
