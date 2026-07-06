namespace Masterwork.ModuleFormat;

/// <summary>
/// Assembles a <see cref="LoadedModule"/> from either a directory of extractor output or
/// in-memory YAML/restext text.
/// </summary>
public interface IModuleLoader
{
    /// <summary>
    /// Loads a module from an extractor output directory: every <c>*.mws.yaml</c> passage file,
    /// plus <c>_variables.yaml</c> and <c>en-US.restext</c> if present.
    /// </summary>
    /// <param name="directoryPath">Path to the extractor output directory.</param>
    LoadedModule LoadFromDirectory(string directoryPath);

    /// <summary>
    /// Builds a module directly from in-memory YAML/restext text — the filesystem-free load path
    /// used by tests.
    /// </summary>
    /// <param name="passageYamls">Raw <c>.mws.yaml</c> text, one entry per passage.</param>
    /// <param name="variablesYaml">Raw <c>_variables.yaml</c> text, if any.</param>
    /// <param name="restextText">Raw <c>en-US.restext</c> text, if any.</param>
    LoadedModule LoadFromSources(IEnumerable<string> passageYamls, string? variablesYaml = null, string? restextText = null);

    /// <summary>
    /// Merges a dependency (typically an asset pack) into <paramref name="module"/> — its passages
    /// (e.g. a shared onboarding flow, reachable via the <c>module::entrypoint</c> dynamic target)
    /// and declared variables (e.g. <c>standard_variables</c> like <c>nameA</c>-<c>nameE</c>,
    /// <c>townname</c>) become part of the result. Entries already present on <paramref name="module"/>
    /// take precedence over the dependency's on id collision — a module can always override a
    /// dependency's passage or variable declaration by declaring its own with the same name.
    /// </summary>
    LoadedModule MergeDependency(LoadedModule module, LoadedModule dependency);
}
