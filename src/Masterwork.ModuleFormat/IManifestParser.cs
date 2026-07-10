namespace Masterwork.ModuleFormat;

/// <summary>Parses a module or asset pack's <c>manifest.yaml</c> into a <see cref="ModuleManifest"/>.</summary>
public interface IManifestParser
{
    /// <summary>Parses a manifest from raw YAML text.</summary>
    /// <param name="yamlText">The full contents of a <c>manifest.yaml</c> file.</param>
    /// <param name="warnings">Collector for unmatched/wrong-shaped field warnings. Pass <see langword="null"/> to discard them.</param>
    /// <param name="preferredLocale">
    /// Locale to resolve localized fields (<c>title</c>, <c>description</c>, <c>info.playtime</c>)
    /// against, e.g. <c>"es"</c>. Falls back to <see cref="ModuleLocales.Default"/>, then to
    /// whichever locale the field actually has, if <see langword="null"/> or not present.
    /// </param>
    ModuleManifest Parse(string yamlText, ModuleWarnings? warnings = null, string? preferredLocale = null);
}
