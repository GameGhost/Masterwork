namespace Masterwork.ModuleFormat;

/// <summary>Parses an asset pack's <c>manifest.yaml</c> into an <see cref="AssetPackManifest"/>.</summary>
public interface IAssetPackManifestParser
{
    /// <summary>Parses a manifest from raw YAML text.</summary>
    /// <param name="yamlText">The full contents of a <c>manifest.yaml</c> file.</param>
    /// <param name="warnings">Collector for unmatched/wrong-shaped field warnings. Pass <see langword="null"/> to discard them.</param>
    AssetPackManifest Parse(string yamlText, ModuleWarnings? warnings = null);
}
