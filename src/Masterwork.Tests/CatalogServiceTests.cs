using System.Text;
using System.Text.Json;
using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Masterwork.Tests;

public class CatalogServiceTests
{
    private static readonly ContentSource Source = new("https://example.com/.catalog/catalog.json", "https://example.com/dl");

    private static byte[] CatalogBytes(string title = "Test Source") => CatalogParser.Write(new CatalogDocument
    {
        Format = CatalogFormatVersion.Current,
        Title = title,
        Entries = [],
    });

    private sealed class StubDownloader : IContentDownloader
    {
        public Dictionary<string, byte[]> Files { get; } = [];
        public List<string> Requested { get; } = [];
        public bool Offline { get; set; }

        public Task<byte[]> GetAsync(string url, IProgress<(long Done, long? Total)>? progress = null, CancellationToken cancellationToken = default)
        {
            Requested.Add(url);
            if (Offline)
            {
                throw new ContentDownloadException("offline");
            }

            return Files.TryGetValue(url, out var bytes)
                ? Task.FromResult(bytes)
                : throw new ContentDownloadException($"404: {url}");
        }
    }

    /// <summary>Just enough of localStorage to exercise the cache, in-memory.</summary>
    private sealed class FakeLocalStorage : IJSRuntime
    {
        private readonly Dictionary<string, string> _items = [];

        public IEnumerable<string> Keys => _items.Keys;

        public string? Read(string key) => _items.GetValueOrDefault(key);

        public void Write(string key, string value) => _items[key] = value;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            var key = (string)args![0]!;
            switch (identifier)
            {
                case "localStorage.getItem":
                    _items.TryGetValue(key, out var value);
                    return ValueTask.FromResult((TValue)(object?)value!);
                case "localStorage.setItem":
                    _items[key] = (string)args[1]!;
                    return ValueTask.FromResult<TValue>(default!);
                default:
                    throw new JSException($"unexpected call: {identifier}");
            }
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private sealed class StubSettingsStore : IAppSettingsStore
    {
        public AppSettings Settings { get; set; } = AppSettings.Default;

        public Task<AppSettings> LoadAsync() => Task.FromResult(Settings);

        public Task SaveAsync(AppSettings settings)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private static (CatalogService Service, StubDownloader Downloader, FakeLocalStorage Storage) Build(FakeLocalStorage? storage = null)
    {
        var downloader = new StubDownloader();
        var js = storage ?? new FakeLocalStorage();
        var service = new CatalogService(
            downloader,
            new SignatureVerifier(js, new BrowserCrypto(js), NullLogger<SignatureVerifier>.Instance),
            new StubSettingsStore(),
            js,
            NullLogger<CatalogService>.Instance);
        return (service, downloader, js);
    }

    [Fact]
    public async Task RefreshIfDue_ColdStart_AlwaysFetches()
    {
        var (service, downloader, storage) = Build();
        downloader.Files[Source.CatalogUrl] = CatalogBytes();

        await service.RefreshIfDueAsync(Source, CatalogRefreshTrigger.ColdStart);
        downloader.Requested.Clear();

        // Even with a cache written moments ago, a cold start refetches.
        var second = await service.RefreshIfDueAsync(Source, CatalogRefreshTrigger.ColdStart);

        Assert.Contains(Source.CatalogUrl, downloader.Requested);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task RefreshIfDue_ReturnToMenu_UsesTheCacheWithinADay()
    {
        var (service, downloader, _) = Build();
        downloader.Files[Source.CatalogUrl] = CatalogBytes();

        await service.RefreshIfDueAsync(Source, CatalogRefreshTrigger.ColdStart);
        downloader.Requested.Clear();

        var snapshot = await service.RefreshIfDueAsync(Source, CatalogRefreshTrigger.ReturnToMenu);

        // Served entirely from cache — no network at all.
        Assert.Empty(downloader.Requested);
        Assert.Equal("Test Source", snapshot!.Catalog.Title);
    }

    [Fact]
    public async Task RefreshIfDue_FirstLookOfASession_RefetchesEvenWhenToldReturnToMenu()
    {
        // A cache written by an earlier run of an older build can otherwise be served for a whole
        // day — including one whose signature no longer verifies, which reads to the player as
        // "this catalog has been tampered with" when the published catalog is perfectly fine.
        var storage = new FakeLocalStorage();
        var first = Build(storage);
        first.Downloader.Files[Source.CatalogUrl] = CatalogBytes();
        await first.Service.RefreshIfDueAsync(Source, CatalogRefreshTrigger.ColdStart);

        // A new service over the same storage stands in for the next app launch.
        var next = Build(storage);
        next.Downloader.Files[Source.CatalogUrl] = CatalogBytes();
        await next.Service.RefreshIfDueAsync(Source, CatalogRefreshTrigger.ReturnToMenu);

        Assert.Contains(Source.CatalogUrl, next.Downloader.Requested);
    }

    [Fact]
    public async Task RefreshIfDue_CachedCatalogThatNoLongerVerifies_IsDiscardedAndRefetched()
    {
        var cert = SelfSignedCertificateGenerator.Create("Catalog Test Signer");
        var storage = new FakeLocalStorage();

        // Cache a catalog whose stored signature doesn't cover it — what an old-format signature
        // looks like to a newer build.
        var first = Build(storage);
        first.Downloader.Files[Source.CatalogUrl] = CatalogBytes();
        first.Downloader.Files[Source.SignatureUrl] = DetachedSignature.Sign(CatalogBytes("Something Else"), cert);
        await first.Service.RefreshIfDueAsync(Source, CatalogRefreshTrigger.ColdStart);

        var next = Build(storage);
        next.Downloader.Files[Source.CatalogUrl] = CatalogBytes();
        next.Downloader.Files[Source.SignatureUrl] = DetachedSignature.Sign(CatalogBytes(), cert);

        // Warm the session so the cold-start rule above isn't what's being measured.
        await next.Service.RefreshIfDueAsync(Source, CatalogRefreshTrigger.ColdStart);
        next.Downloader.Requested.Clear();

        // Re-poison the cache, then ask in a way that would normally be served from it.
        var poison = Build(storage);
        poison.Downloader.Files[Source.CatalogUrl] = CatalogBytes();
        poison.Downloader.Files[Source.SignatureUrl] = DetachedSignature.Sign(CatalogBytes("Something Else"), cert);
        await poison.Service.RefreshIfDueAsync(Source, CatalogRefreshTrigger.ColdStart);

        poison.Downloader.Files[Source.SignatureUrl] = DetachedSignature.Sign(CatalogBytes(), cert);
        poison.Downloader.Requested.Clear();
        var snapshot = await poison.Service.RefreshIfDueAsync(Source, CatalogRefreshTrigger.ReturnToMenu);

        Assert.Contains(Source.CatalogUrl, poison.Downloader.Requested);
        Assert.Equal(SignatureVerificationOutcome.Valid, snapshot!.Signature.Outcome);
    }

    [Fact]
    public async Task RefreshIfDue_ReturnToMenu_RefetchesOnceTheCacheIsADayOld()
    {
        var storage = new FakeLocalStorage();
        var (service, downloader, _) = Build(storage);
        downloader.Files[Source.CatalogUrl] = CatalogBytes();

        await service.RefreshIfDueAsync(Source, CatalogRefreshTrigger.ColdStart);
        BackdateCache(storage, TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));
        downloader.Requested.Clear();

        await service.RefreshIfDueAsync(Source, CatalogRefreshTrigger.ReturnToMenu);

        Assert.Contains(Source.CatalogUrl, downloader.Requested);
    }

    [Fact]
    public async Task RefreshIfDue_FallsBackToTheCacheWhenTheNetworkFails()
    {
        var (service, downloader, _) = Build();
        downloader.Files[Source.CatalogUrl] = CatalogBytes();
        await service.RefreshIfDueAsync(Source, CatalogRefreshTrigger.ColdStart);

        downloader.Offline = true;
        var snapshot = await service.RefreshIfDueAsync(Source, CatalogRefreshTrigger.ColdStart);

        // A catalog the player already has beats nothing at all.
        Assert.NotNull(snapshot);
        Assert.Equal("Test Source", snapshot.Catalog.Title);
    }

    [Fact]
    public async Task RefreshIfDue_NoCacheAndNoNetwork_ReturnsNull()
    {
        var (service, downloader, _) = Build();
        downloader.Offline = true;

        Assert.Null(await service.RefreshIfDueAsync(Source, CatalogRefreshTrigger.ColdStart));
    }

    [Fact]
    public async Task Fetch_UnsignedCatalog_IsReportedNotThrown()
    {
        // The signature URL simply isn't served. That's a fact about the source to report, not a
        // failure to fetch — the trust decision is what decides whether it's usable.
        var (service, downloader, _) = Build();
        downloader.Files[Source.CatalogUrl] = CatalogBytes();

        var snapshot = await service.FetchAsync(Source);

        Assert.Equal(SignatureVerificationOutcome.Unsigned, snapshot.Signature.Outcome);
        Assert.Equal(PackageTrustDecision.Unsigned, snapshot.Decision);
    }

    [Fact]
    public async Task Fetch_SignedCatalog_VerifiesAgainstTheExactBytesServed()
    {
        var cert = SelfSignedCertificateGenerator.Create("Catalog Test Signer");
        var bytes = CatalogBytes();
        var (service, downloader, _) = Build();
        downloader.Files[Source.CatalogUrl] = bytes;
        downloader.Files[Source.SignatureUrl] = DetachedSignature.Sign(bytes, cert);

        var snapshot = await service.FetchAsync(Source);

        Assert.Equal(SignatureVerificationOutcome.Valid, snapshot.Signature.Outcome);
        Assert.Equal("Catalog Test Signer", snapshot.Signature.CertificateCommonName);

        // Not pinned and not remembered, so it's an unrecognized signer — signed is not trusted.
        Assert.Equal(PackageTrustDecision.UnrecognizedSigner, snapshot.Decision);
    }

    [Fact]
    public async Task Fetch_TamperedCatalog_IsBlocked()
    {
        var cert = SelfSignedCertificateGenerator.Create("Catalog Test Signer");
        var signature = DetachedSignature.Sign(CatalogBytes(), cert);
        var (service, downloader, _) = Build();

        // Signed as one catalog, served as another.
        downloader.Files[Source.CatalogUrl] = CatalogBytes("Substituted Source");
        downloader.Files[Source.SignatureUrl] = signature;

        var snapshot = await service.FetchAsync(Source);

        Assert.Equal(SignatureVerificationOutcome.Invalid, snapshot.Signature.Outcome);
        Assert.Equal(PackageTrustDecision.Blocked, snapshot.Decision);
    }

    [Fact]
    public async Task Fetch_MalformedCatalog_Throws()
    {
        var (service, downloader, _) = Build();
        downloader.Files[Source.CatalogUrl] = Encoding.UTF8.GetBytes("{ not json");

        await Assert.ThrowsAsync<CatalogParseException>(() => service.FetchAsync(Source));
    }

    [Fact]
    public async Task GetCached_DoesNoNetworkAtAll()
    {
        var (service, downloader, _) = Build();
        downloader.Files[Source.CatalogUrl] = CatalogBytes();
        await service.RefreshIfDueAsync(Source, CatalogRefreshTrigger.ColdStart);
        downloader.Requested.Clear();

        var snapshot = await service.GetCachedAsync(Source);

        Assert.NotNull(snapshot);
        Assert.Empty(downloader.Requested);
    }

    // Rewrites the cached entry's timestamp so the daily policy can be exercised without waiting.
    private static void BackdateCache(FakeLocalStorage storage, TimeSpan age)
    {
        var key = storage.Keys.Single(k => k.StartsWith("masterwork.catalog.", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(storage.Read(key)!);

        var rewritten = new Dictionary<string, object?>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            rewritten[property.Name] = property.NameEquals("FetchedAt")
                ? DateTimeOffset.UtcNow - age
                : property.Value.Clone();
        }

        storage.Write(key, JsonSerializer.Serialize(rewritten));
    }
}
