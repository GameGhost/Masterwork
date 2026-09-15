using System.Text;
using System.Text.Json;
using Masterwork.ModuleFormat;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <summary>Why a catalog refresh is being considered — see <see cref="CatalogService.RefreshIfDueAsync"/>.</summary>
public enum CatalogRefreshTrigger
{
    /// <summary>The app just started. Always refreshes.</summary>
    ColdStart,

    /// <summary>The player came back to the main menu during an already-running session. Refreshes at most once a day.</summary>
    ReturnToMenu,
}

/// <summary>
/// A catalog as fetched, with what verifying its signature found. <see cref="Decision"/> is the
/// caller's cue: only <see cref="PackageTrustDecision.Trusted"/> is safe to install from without
/// asking, because an entry's hash is only as trustworthy as the catalog that carried it.
/// </summary>
public sealed record CatalogSnapshot(
    ContentSource Source,
    CatalogDocument Catalog,
    SignatureVerificationResult Signature,
    PackageTrustDecision Decision,
    DateTimeOffset FetchedAt);

/// <summary>
/// Fetches, verifies, and caches a source's catalog.
///
/// The cache is a convenience, not a store of record — everything in it is re-fetchable — so it
/// lives in the WebView's own <c>localStorage</c> on both heads rather than following the
/// native-storage split the module/save stores use. Losing it costs one fetch.
/// </summary>
public sealed class CatalogService(
    IContentDownloader downloader,
    IAppSettingsStore settingsStore,
    IJSRuntime js,
    ILogger<CatalogService> logger)
{
    private const string CacheKeyPrefix = "masterwork.catalog.";

    private sealed record CachedCatalog(string CatalogJson, string? SignatureJson, DateTimeOffset FetchedAt);

    /// <summary>
    /// Refreshes if the policy says it's due, otherwise returns what's cached. A failed refresh
    /// falls back to the cache rather than surfacing as an error: a catalog the player already has
    /// is more useful than nothing when the network is down.
    /// </summary>
    public async Task<CatalogSnapshot?> RefreshIfDueAsync(
        ContentSource source,
        CatalogRefreshTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        var cached = await ReadCacheAsync(source);

        if (!IsRefreshDue(trigger, cached?.FetchedAt))
        {
            return cached is null ? null : await ToSnapshotAsync(source, cached);
        }

        try
        {
            return await FetchAsync(source, cancellationToken);
        }
        catch (Exception ex) when (ex is ContentDownloadException or CatalogParseException)
        {
            logger.LogWarning(ex, "Catalog refresh from {Url} failed; using cache if present", source.CatalogUrl);
            return cached is null ? null : await ToSnapshotAsync(source, cached);
        }
    }

    /// <summary>Returns the cached catalog without any network access, or <see langword="null"/> if none is cached.</summary>
    public async Task<CatalogSnapshot?> GetCachedAsync(ContentSource source)
    {
        var cached = await ReadCacheAsync(source);
        return cached is null ? null : await ToSnapshotAsync(source, cached);
    }

    /// <summary>Fetches and verifies unconditionally, updating the cache on success.</summary>
    public async Task<CatalogSnapshot> FetchAsync(ContentSource source, CancellationToken cancellationToken = default)
    {
        var catalogBytes = await downloader.GetAsync(source.CatalogUrl, progress: null, cancellationToken);

        // A source serving no signature at all is a fact to report, not a failure to fetch — the
        // trust decision below is what decides whether it's usable.
        byte[]? signatureBytes = null;
        try
        {
            signatureBytes = await downloader.GetAsync(source.SignatureUrl, progress: null, cancellationToken);
        }
        catch (ContentDownloadException ex)
        {
            logger.LogWarning(ex, "No catalog signature at {Url}", source.SignatureUrl);
        }

        // Parsed only after the bytes are in hand, and always from the same bytes the signature
        // covers — re-serializing before verifying would break the whole point of signing bytes.
        var catalog = CatalogParser.Parse(catalogBytes);
        var verification = DetachedSignature.Verify(catalogBytes, signatureBytes);

        await WriteCacheAsync(source, new CachedCatalog(
            Encoding.UTF8.GetString(catalogBytes),
            signatureBytes is null ? null : Encoding.UTF8.GetString(signatureBytes),
            DateTimeOffset.UtcNow));

        var snapshot = new CatalogSnapshot(source, catalog, verification, await DecideAsync(verification), DateTimeOffset.UtcNow);
        logger.LogInformation(
            "Catalog from {Url}: {EntryCount} entries, signature {Outcome}, trust {Decision}",
            source.CatalogUrl, catalog.Entries.Count, verification.Outcome, snapshot.Decision);
        return snapshot;
    }

    // Cold start always refreshes; a warm session refreshes at most daily, and there is deliberately
    // no manual "refresh now" beyond returning to the menu.
    private static bool IsRefreshDue(CatalogRefreshTrigger trigger, DateTimeOffset? lastFetched) => trigger switch
    {
        CatalogRefreshTrigger.ColdStart => true,
        _ => lastFetched is null || DateTimeOffset.UtcNow - lastFetched.Value >= TimeSpan.FromDays(1),
    };

    private async Task<PackageTrustDecision> DecideAsync(SignatureVerificationResult verification)
    {
        var settings = await settingsStore.LoadAsync();
        return PackageTrustEvaluator.Evaluate(verification, WhiteLabelConfig.PublisherThumbprint, settings.TrustedPublisherThumbprints);
    }

    private async Task<CatalogSnapshot> ToSnapshotAsync(ContentSource source, CachedCatalog cached)
    {
        var catalogBytes = Encoding.UTF8.GetBytes(cached.CatalogJson);
        var signatureBytes = cached.SignatureJson is null ? null : Encoding.UTF8.GetBytes(cached.SignatureJson);

        // Re-verified on every read rather than caching the verdict: the pinned anchor can change
        // under a cached catalog (an app update), and so can the player's trusted-publisher list.
        var verification = DetachedSignature.Verify(catalogBytes, signatureBytes);

        return new CatalogSnapshot(
            source,
            CatalogParser.Parse(catalogBytes),
            verification,
            await DecideAsync(verification),
            cached.FetchedAt);
    }

    private static string CacheKey(ContentSource source) => CacheKeyPrefix + Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(source.CatalogUrl)))[..16];

    private async Task<CachedCatalog?> ReadCacheAsync(ContentSource source)
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", CacheKey(source));
            return json is null ? null : JsonSerializer.Deserialize<CachedCatalog>(json);
        }
        catch (Exception ex) when (ex is JsonException or JSException)
        {
            logger.LogWarning(ex, "Discarding unreadable cached catalog for {Url}", source.CatalogUrl);
            return null;
        }
    }

    private async Task WriteCacheAsync(ContentSource source, CachedCatalog cached)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", CacheKey(source), JsonSerializer.Serialize(cached));
        }
        catch (JSException ex)
        {
            // Storage can be full or blocked; the catalog still works this session.
            logger.LogWarning(ex, "Could not cache catalog for {Url}", source.CatalogUrl);
        }
    }
}
