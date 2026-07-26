namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IModuleAssetSource"/>
/// <remarks>
/// The "no module loaded yet" / "this module has no assets" stand-in — <see cref="GameSessionState"/>'s
/// default and post-<see cref="GameSessionState.Clear"/> value, replacing what used to be
/// <c>new Dictionary&lt;string, byte[]&gt;()</c>. A single shared instance is fine since it's stateless;
/// <see cref="AssetResolver"/>'s cache-invalidation keys off this reference (or a real module's own
/// asset source) changing, not off anything this type itself holds.
/// </remarks>
public sealed class EmptyModuleAssetSource : IModuleAssetSource
{
    public static readonly EmptyModuleAssetSource Instance = new();

    private EmptyModuleAssetSource()
    {
    }

    /// <inheritdoc/>
    public Task<byte[]?> GetAssetAsync(string assetPath) => Task.FromResult<byte[]?>(null);

    /// <inheritdoc/>
    public Task<string?> GetAssetUrlAsync(string assetPath, string mimeType) => Task.FromResult<string?>(null);

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ListAssetPathsAsync() => Task.FromResult<IReadOnlyList<string>>([]);
}
