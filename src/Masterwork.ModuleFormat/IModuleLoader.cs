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
    /// <param name="additionalVariableYamls">
    /// Raw <c>variables/*.yaml</c> text, one entry per hand-authored variable file — same schema
    /// as <paramref name="variablesYaml"/> (<c>variables:</c>/legacy <c>standard_variables:</c>),
    /// parsed and merged on top of it after <paramref name="variablesYaml"/>: a matching variable
    /// name is replaced, a new one is added. Lets a module declare variables that survive
    /// re-extraction without editing the extractor-owned <c>_variables.yaml</c>, and lets authors
    /// split declarations across multiple files by concern.
    /// </param>
    /// <param name="defaultRestextText">
    /// Raw <c>{ModuleLocales.Default}.restext</c> text, if the caller has already resolved a
    /// *different*, non-default locale for <paramref name="restextText"/> — enables genuine
    /// per-key fallback: a key present in <paramref name="restextText"/> (after
    /// <paramref name="restextOverrideText"/> is merged on top of it) always wins, but a key
    /// missing from it falls back to this dictionary's value instead of resolving to a raw,
    /// unresolved <c>restext://Key</c> string. Omit (or pass the same text as
    /// <paramref name="restextText"/>) when there's nothing to fall back to — e.g.
    /// <see cref="LoadFromDirectory"/> never passes this, since it only ever resolves a single
    /// locale to begin with.
    /// </param>
    /// <param name="defaultRestextOverrideText">
    /// Raw <c>{ModuleLocales.Default}.overrides.restext</c> text, if any — merged into
    /// <paramref name="defaultRestextText"/>'s entries the same way <paramref name="restextOverrideText"/>
    /// merges into <paramref name="restextText"/>'s, before that dictionary is used as the fallback.
    /// </param>
    /// <param name="dependencyRestexts">
    /// One entry per dependency asset pack this module declares, in the same order as the module's
    /// own <c>dependencies:</c> list — see <see cref="DependencyRestext"/> for how key collisions
    /// between dependencies resolve. Sits <em>underneath</em> everything
    /// <paramref name="restextText"/>/<paramref name="defaultRestextText"/> already resolve to: a
    /// key the module's own restext already has always wins; only a key genuinely absent from it
    /// falls through to a dependency.
    /// </param>
    LoadedModule LoadFromSources(
        IEnumerable<string> passageYamls, string? variablesYaml = null, string? restextText = null,
        IEnumerable<string>? overridePassageYamls = null, string? restextOverrideText = null,
        IEnumerable<string>? layoutChromeYamls = null, IEnumerable<string>? additionalVariableYamls = null,
        string? defaultRestextText = null, string? defaultRestextOverrideText = null,
        IEnumerable<DependencyRestext>? dependencyRestexts = null);
}
