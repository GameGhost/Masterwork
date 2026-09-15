using Microsoft.Extensions.Logging;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// The one downloader every head uses. What differs between heads is the <see cref="ContentSource"/>
/// they resolve URLs from, not how a response is read — so streaming, progress reporting, and the
/// size cap are written once here.
/// </summary>
public sealed class HttpContentDownloader(
    IHttpClientFactory httpClientFactory,
    ILogger<HttpContentDownloader> logger) : IContentDownloader
{
    /// <summary>Name of the <see cref="HttpClient"/> each head registers for content downloads.</summary>
    public const string HttpClientName = "masterwork-content";

    // Well clear of the largest real module (~104MB) while still refusing to buffer something
    // absurd into memory on a phone. Applies to what the response actually delivers, not just to a
    // declared Content-Length, since that header can lie or be absent.
    private const long MaxBytes = 512L * 1024 * 1024;

    /// <inheritdoc />
    public async Task<byte[]> GetAsync(string url, IProgress<(long Done, long? Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        var http = httpClientFactory.CreateClient(HttpClientName);

        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new ContentDownloadException($"Download failed with status {(int)response.StatusCode}.");
            }

            var declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength > MaxBytes)
            {
                throw new ContentDownloadException($"Download is larger than the {MaxBytes / (1024 * 1024)}MB limit.");
            }

            return await ReadAllAsync(response, declaredLength, progress, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Download of {Url} failed", url);
            throw new ContentDownloadException("Download failed — check the connection and try again.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Download of {Url} timed out", url);
            throw new ContentDownloadException("Download timed out.", ex);
        }
    }

    private static async Task<byte[]> ReadAllAsync(
        HttpResponseMessage response,
        long? declaredLength,
        IProgress<(long Done, long? Total)>? progress,
        CancellationToken cancellationToken)
    {
        using var source = await response.Content.ReadAsStreamAsync(cancellationToken);

        // Pre-sized when the length is known so a 100MB package doesn't repeatedly reallocate and
        // copy its way up from 256 bytes.
        using var buffer = declaredLength is { } length
            ? new MemoryStream(checked((int)length))
            : new MemoryStream();

        var chunk = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            done += read;
            if (done > MaxBytes)
            {
                throw new ContentDownloadException($"Download exceeded the {MaxBytes / (1024 * 1024)}MB limit.");
            }

            buffer.Write(chunk, 0, read);
            progress?.Report((done, declaredLength));
        }

        return buffer.ToArray();
    }
}
