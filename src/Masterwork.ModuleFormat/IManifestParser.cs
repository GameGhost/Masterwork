namespace Masterwork.ModuleFormat;

/// <summary>Parses a module or asset pack's <c>manifest.yaml</c> into a <see cref="ModuleManifest"/>.</summary>
public interface IManifestParser
{
    /// <summary>Parses a manifest from raw YAML text.</summary>
    /// <param name="yamlText">The full contents of a <c>manifest.yaml</c> file.</param>
    /// <param name="warnings">Collector for unmatched/wrong-shaped field warnings. Pass <see langword="null"/> to discard them.</param>
    ModuleManifest Parse(string yamlText, ModuleWarnings? warnings = null);
}
