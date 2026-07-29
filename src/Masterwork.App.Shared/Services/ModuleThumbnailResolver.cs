namespace Masterwork.App.Shared.Services;

/// <summary>
/// Resolves a manifest's <c>thumbnail.image</c> <c>image://slug</c> ref to a real, displayable URL
/// against one specific module's own assets — used by <see cref="IModuleStore.ListAsync"/>
/// implementations to give the Start New Game carousel a real thumbnail without loading the whole
/// module (no <see cref="IModuleLoader"/> pass, no <see cref="PreloadedModuleAssetSource"/>). Same
/// "assets/images/{slug}{ext}" extension-probing convention <see cref="AssetResolver"/> uses for a
/// loaded module's own image:// refs, just pointed at an arbitrary <see cref="IModuleAssetSource"/>
/// instead of the live session's — kept as its own tiny static helper rather than reusing
/// <see cref="AssetResolver"/> itself, since that type is coupled to <see cref="GameSessionState"/>
/// and handles icon://font:// schemes/fallback tiers this call site never needs.
/// </summary>
public static class ModuleThumbnailResolver
{
    private const string ImageScheme = "image://";

    private static readonly (string Ext, string MimeType)[] ImageExtensions =
    [
        (".png", "image/png"),
        (".svg", "image/svg+xml"),
        (".jpg", "image/jpeg"),
        (".jpeg", "image/jpeg"),
    ];

    /// <summary>
    /// Resolves <paramref name="imageUri"/> (expected form: <c>"image://slug"</c>) against
    /// <paramref name="assets"/>, or returns <see langword="null"/> if it's absent, malformed, or the
    /// asset doesn't exist under any of the common extensions.
    /// </summary>
    public static async Task<string?> ResolveAsync(IModuleAssetSource assets, string? imageUri)
    {
        if (imageUri is null || !imageUri.StartsWith(ImageScheme, StringComparison.Ordinal))
        {
            return null;
        }

        var slug = imageUri[ImageScheme.Length..];
        foreach (var (ext, mime) in ImageExtensions)
        {
            var url = await assets.GetAssetUrlAsync($"assets/images/{slug}{ext}", mime);
            if (url is not null)
            {
                return url;
            }
        }

        return null;
    }
}
