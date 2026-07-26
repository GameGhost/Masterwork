namespace Masterwork.App.Shared.Services;

/// <summary>
/// Maps an asset's file extension to its MIME type, purely from the path it already has — used when
/// <see cref="PreloadedModuleAssetSource"/> preloads a whole module's assets by real path.
/// <see cref="AssetResolver"/>'s own extension tables are a different, related concept: an ordered
/// list of *candidate* extensions to try against a bare slug (since the slug alone doesn't say which
/// extension the module actually shipped) — this type instead reads the extension a real, known file
/// already has.
/// </summary>
public static class AssetMimeTypes
{
    public static string ResolveMimeType(string assetPath) =>
        Path.GetExtension(assetPath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".woff2" => "font/woff2",
            ".woff" => "font/woff",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".mp3" => "audio/mpeg",
            ".ogg" => "audio/ogg",
            ".wav" => "audio/wav",
            _ => "application/octet-stream",
        };
}
