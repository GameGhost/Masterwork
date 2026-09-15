namespace Masterwork.App.Shared.Services;

/// <summary>Fetches bytes from a content URL — a catalog, its signature, or a package.</summary>
/// <remarks>
/// Every URL handed to this comes from a <see cref="ContentSource"/>, which builds it from a base
/// the build itself supplied and a relative path the catalog declared. Nothing remote ever names a
/// host, so there is no arbitrary-URL case here to guard against.
/// </remarks>
public interface IContentDownloader
{
    /// <summary>
    /// Downloads <paramref name="url"/> in full. <paramref name="progress"/> reports
    /// (bytes so far, total) where the total is <see langword="null"/> if the response declares no
    /// <c>Content-Length</c>.
    /// </summary>
    /// <exception cref="ContentDownloadException">The request failed.</exception>
    Task<byte[]> GetAsync(string url, IProgress<(long Done, long? Total)>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>A download that couldn't be completed. Carries a player-safe reason, not a raw transport error.</summary>
public sealed class ContentDownloadException(string message, Exception? inner = null) : Exception(message, inner);
