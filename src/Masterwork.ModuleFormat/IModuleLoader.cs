namespace Masterwork.ModuleFormat;

/// <summary>
/// Assembles a <see cref="LoadedModule"/> from either a directory of extractor output or
/// in-memory YAML/restext text.
/// </summary>
public interface IModuleLoader
{
    /// <summary>
    /// Loads a module from a directory: extractor-owned passages plus <c>_variables.yaml</c> and
    /// a restext locale file if present, with any hand-authored passage and restext overrides
    /// layered on top.
    /// </summary>
    /// <remarks>
    /// Base passages are read from a <c>passages/</c> subfolder (or, for older extractor output
    /// with no such subfolder, from <paramref name="directoryPath"/> itself). Passage overrides —
    /// new or replacement passages that are hand-maintained rather than extractor output — are
    /// read from a <c>passages-override/</c> subfolder if one exists and applied after the base
    /// passages, so a matching <c>passage_id</c> in the override wins. Both folder names can be
    /// redirected via optional <c>passages</c>/<c>passages_override</c> string fields in
    /// <c>manifest.yaml</c>. The restext locale file is whichever <c>{culture}.restext</c> is
    /// present at <paramref name="directoryPath"/> (preferring <c>en-US.restext</c> if more than
    /// one exists); a sibling <c>{culture}.overrides.restext</c>, if present, is merged on top with
    /// the same add/override-by-key semantics as passage overrides.
    /// </remarks>
    /// <param name="directoryPath">Path to the module directory.</param>
    LoadedModule LoadFromDirectory(string directoryPath);

    /// <summary>
    /// Builds a module directly from in-memory YAML/restext text — the filesystem-free load path
    /// used by tests.
    /// </summary>
    /// <param name="passageYamls">Raw <c>.mws.yaml</c> text, one entry per base passage.</param>
    /// <param name="variablesYaml">Raw <c>_variables.yaml</c> text, if any.</param>
    /// <param name="restextText">Raw base <c>{culture}.restext</c> text, if any.</param>
    /// <param name="overridePassageYamls">
    /// Raw <c>.mws.yaml</c> text for hand-authored passage overrides, applied after
    /// <paramref name="passageYamls"/>: a matching <c>passage_id</c> replaces the base version,
    /// a new one is added.
    /// </param>
    /// <param name="restextOverrideText">
    /// Raw <c>{culture}.overrides.restext</c> text, if any, merged into
    /// <paramref name="restextText"/>'s entries after parsing: a matching key replaces the base
    /// value, a new key is added.
    /// </param>
    /// <param name="layoutChromeYamls">
    /// Raw <c>layouts/*.yaml</c> text, one entry per layout chrome file, keyed by each file's own
    /// <c>layout_id</c> (not filename) once loaded.
    /// </param>
    LoadedModule LoadFromSources(
        IEnumerable<string> passageYamls, string? variablesYaml = null, string? restextText = null,
        IEnumerable<string>? overridePassageYamls = null, string? restextOverrideText = null,
        IEnumerable<string>? layoutChromeYamls = null);

    /// <summary>
    /// Merges a dependency (typically an asset pack) into <paramref name="module"/> — its passages
    /// (e.g. a shared onboarding flow, reachable via the <c>module::entrypoint</c> dynamic target)
    /// and declared variables become part of the result. Entries already present on
    /// <paramref name="module"/> take precedence over the dependency's on id collision — a module
    /// can always override a dependency's passage or variable declaration by declaring its own with
    /// the same name.
    /// </summary>
    LoadedModule MergeDependency(LoadedModule module, LoadedModule dependency);
}
