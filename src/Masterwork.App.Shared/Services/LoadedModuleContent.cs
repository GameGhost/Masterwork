using System.Text;
using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Everything <see cref="IModuleStore.LoadAsync"/> produces for one module: the parsed
/// <see cref="Module"/> itself, an <see cref="IModuleAssetSource"/> for <see cref="IAssetResolver"/>'s
/// bundle-local tier, and its stylesheet text (decoded from the manifest's declared <c>style</c>
/// path, UTF-8) for module-provided CSS injection. <see cref="StyleCss"/> is <see langword="null"/>
/// for a module that doesn't have one.
/// </summary>
public sealed record LoadedModuleContent(
    LoadedModule Module,
    IModuleAssetSource Assets,
    string? StyleCss
)
{
    /// <summary>
    /// Builds a <see cref="LoadedModuleContent"/> from an already-parsed <see cref="LoadedModule"/>
    /// and the <see cref="IModuleAssetSource"/> a store's own <c>LoadAsync</c> just constructed for
    /// it — shared by both platform <see cref="IModuleStore"/> implementations so the manifest-driven
    /// style-path resolution lives in one place. Fetches only the one asset the manifest names
    /// (<paramref name="manifestYaml"/>'s <c>style</c> path, default <c>"assets/style.css"</c>) rather
    /// than the whole asset set — the point of <see cref="IModuleAssetSource"/> being lazy in the
    /// first place.
    /// </summary>
    public static async Task<LoadedModuleContent> BuildAsync(LoadedModule module, string? manifestYaml, IModuleAssetSource assets)
    {
        string? styleCss = null;
        if (manifestYaml is not null)
        {
            var manifest = new ManifestParser().Parse(manifestYaml);
            var cssBytes = await assets.GetAssetAsync(manifest.StylePath);
            if (cssBytes is not null)
            {
                styleCss = Encoding.UTF8.GetString(cssBytes);
            }
        }

        return new LoadedModuleContent(module, assets, styleCss);
    }

    /// <summary>
    /// Returns a copy of this content with <see cref="Assets"/> replaced by a
    /// <see cref="PreloadedModuleAssetSource"/> that has already fetched every asset the module
    /// ships. Called by the Play/Resume flow before navigating so a passage's images/fonts are
    /// already resident on first render rather than popping in as lazy fetches complete.
    /// </summary>
    public async Task<LoadedModuleContent> WithPreloadedAssetsAsync(IProgress<(int Done, int Total)>? progress = null)
    {
        var preloaded = new PreloadedModuleAssetSource(Assets);
        await preloaded.PreloadAllAsync(progress);
        return this with { Assets = preloaded };
    }
}
