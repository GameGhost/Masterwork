using System.Text;
using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Everything <see cref="IModuleStore.LoadAsync"/> produces for one module: the parsed
/// <see cref="Module"/> itself, its raw asset bytes (keyed by full in-package path, e.g.
/// <c>"assets/icons/village.png"</c> — same keys as <c>ModulePackageContents.Assets</c>) for
/// <see cref="IAssetResolver"/>'s bundle-local tier, and its stylesheet text (decoded from the
/// manifest's declared <c>style</c> path, UTF-8) for module-provided CSS injection. <see cref="Assets"/>
/// is empty and <see cref="StyleCss"/> is <see langword="null"/> for modules that don't have them
/// (e.g. the built-in demo module).
/// </summary>
public sealed record LoadedModuleContent(
    LoadedModule Module,
    IReadOnlyDictionary<string, byte[]> Assets,
    string? StyleCss
)
{
    /// <summary>
    /// Builds a <see cref="LoadedModuleContent"/> from a freshly-unzipped <c>.mwm</c> package and its
    /// already-parsed <see cref="LoadedModule"/> — shared by both platform <see cref="IModuleStore"/>
    /// implementations so the manifest-driven style-path resolution lives in one place.
    /// </summary>
    public static LoadedModuleContent FromPackage(ModulePackageContents contents, LoadedModule module)
    {
        string? styleCss = null;
        if (contents.ManifestYaml is not null)
        {
            var manifest = new ManifestParser().Parse(contents.ManifestYaml);
            if (contents.Assets.TryGetValue(manifest.StylePath, out var cssBytes))
            {
                styleCss = Encoding.UTF8.GetString(cssBytes);
            }
        }

        return new LoadedModuleContent(module, contents.Assets, styleCss);
    }
}
