namespace Masterwork.App.Shared.Services;

/// <summary>
/// A handle to one loaded module's <c>assets/</c> subtree, backing <see cref="IAssetResolver"/>'s
/// bundle-local tier. Each <see cref="IModuleStore"/> implementation's
/// <see cref="IModuleStore.LoadAsync"/> returns one of these (via <see cref="LoadedModuleContent"/>).
/// <see cref="GetAssetUrlAsync"/> resolves an asset directly to a displayable URI (a <c>data:</c> URI
/// on MAUI, a <c>blob:</c> object URL on web) rather than raw bytes — an implementation that reads
/// from IndexedDB can build the object URL entirely JS-side (<c>URL.createObjectURL</c>) without ever
/// marshaling the (often large) asset bytes across the JS/.NET boundary into managed memory, which
/// measurably matters: per-asset <c>byte[]</c> JSInterop marshaling was the dominant cost behind
/// visible multi-second stalls and multi-hundred-MB memory spikes when preloading a module with many
/// sizable images. <see cref="PreloadedModuleAssetSource"/> wraps one of these to resolve every asset
/// (<see cref="ListAssetPathsAsync"/>) up front instead of per-passage-reference lazily, with
/// progress — see its own remarks for why.
/// </summary>
public interface IModuleAssetSource
{
    /// <summary>
    /// Fetches one asset's raw bytes by full in-package path — for content that needs to be read as
    /// data rather than referenced by URI (currently just <see cref="LoadedModuleContent.BuildAsync"/>'s
    /// <c>style.css</c> fetch, injected as literal text into a <c>&lt;style&gt;</c> tag). Called once
    /// per module load, never per-render, so the JSInterop marshaling cost this incurs on
    /// <see cref="IndexedDbModuleAssetSource"/> is a non-issue at this call volume — display assets
    /// (images/fonts) should go through <see cref="GetAssetUrlAsync"/> instead, which avoids it.
    /// </summary>
    Task<byte[]?> GetAssetAsync(string assetPath);

    /// <summary>Resolves one asset by full in-package path (e.g. <c>"assets/icons/village.png"</c>) to a displayable URI, or <see langword="null"/> if not found.</summary>
    Task<string?> GetAssetUrlAsync(string assetPath, string mimeType);

    /// <summary>Every asset path this module ships (full in-package paths, same form as <see cref="GetAssetUrlAsync"/> expects) — used to preload the whole set with progress.</summary>
    Task<IReadOnlyList<string>> ListAssetPathsAsync();
}
