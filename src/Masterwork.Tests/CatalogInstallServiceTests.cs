using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Masterwork.Tests;

public class CatalogInstallServiceTests
{
    private static readonly ContentSource Source = new("https://example.com/.catalog/catalog.json", "https://example.com/dl");

    // Minimal but real: ModuleHasher and the store both read actual package bytes.
    private static byte[] Package(string id, string type = "module")
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = archive.CreateEntry("manifest.yaml").Open();
            stream.Write(Encoding.UTF8.GetBytes($"type: '{type}'\nid: '{id}'\ntitle: '{id}'\nversion: '1.0.0'\n"));
        }

        return buffer.ToArray();
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class StubDownloader : IContentDownloader
    {
        public Dictionary<string, byte[]> Files { get; } = [];
        public List<string> Requested { get; } = [];

        public Task<byte[]> GetAsync(string url, IProgress<(long Done, long? Total)>? progress = null, CancellationToken cancellationToken = default)
        {
            Requested.Add(url);
            return Files.TryGetValue(url, out var bytes)
                ? Task.FromResult(bytes)
                : throw new ContentDownloadException($"404: {url}");
        }
    }

    private sealed class StubModuleStore : IModuleStore
    {
        public List<InstalledModule> Installed { get; } = [];

        public Task<IReadOnlyList<InstalledModule>> ListAsync() => Task.FromResult<IReadOnlyList<InstalledModule>>(Installed);

        public Task<InstalledModule> InstallAsync(byte[] mwmBytes, string sha256, IProgress<(int Done, int Total)>? progress = null)
        {
            var installed = new InstalledModule("installed", "1.0.0", "Installed", "", [], sha256);
            Installed.Add(installed);
            return Task.FromResult(installed);
        }

        public Task<LoadedModuleContent> LoadAsync(string moduleId, string? locale = null) => throw new NotSupportedException();

        public Task DeleteAsync(string moduleId) => throw new NotSupportedException();
    }

    private sealed class StubAssetPackStore : IAssetPackStore
    {
        public List<InstalledAssetPack> Installed { get; } = [];
        public List<AssetPackInstallSource> InstallSources { get; } = [];

        public Task<IReadOnlyList<InstalledAssetPack>> ListAsync() => Task.FromResult<IReadOnlyList<InstalledAssetPack>>(Installed);

        public Task<InstalledAssetPack> InstallAsync(byte[] bytes, string sha256, AssetPackInstallSource source, IProgress<(int Done, int Total)>? progress = null)
        {
            InstallSources.Add(source);
            var pack = new InstalledAssetPack("dep.pack", "0.1.0", "Dep", sha256, source);
            Installed.Add(pack);
            return Task.FromResult(pack);
        }

        public Task<AssetPackPackageContents?> LoadContentAsync(string assetPackId, string version) => throw new NotSupportedException();

        public Task DeleteAsync(string assetPackId, string version) => throw new NotSupportedException();

        public Task<IModuleAssetSource> GetAssetSourceAsync(string assetPackId, string version) => throw new NotSupportedException();
    }

    private static CatalogSnapshot Snapshot(PackageTrustDecision decision, params CatalogEntry[] entries) =>
        new(Source,
            new CatalogDocument { Format = CatalogFormatVersion.Current, Title = "Test", Entries = entries },
            new SignatureVerificationResult(SignatureVerificationOutcome.Valid, "CN=Test", "AA", "Test"),
            decision,
            DateTimeOffset.UtcNow);

    private static CatalogEntry Entry(string id, byte[] bytes, string path, string type = "module", params ModuleDependency[] dependencies) =>
        new()
        {
            Type = type,
            Id = id,
            Title = id,
            Version = "1.0.0",
            Path = path,
            Sha256 = Sha256(bytes),
            Size = bytes.LongLength,
            Dependencies = dependencies,
        };

    private static (CatalogInstallService Service, StubDownloader Downloader, StubModuleStore Modules, StubAssetPackStore Packs) Build()
    {
        var downloader = new StubDownloader();
        var modules = new StubModuleStore();
        var packs = new StubAssetPackStore();
        var js = new NullJsRuntime();
        var service = new CatalogInstallService(
            downloader, modules, packs, js, new BrowserCrypto(js), NullLogger<CatalogInstallService>.Instance);

        return (service, downloader, modules, packs);
    }

    [Fact]
    public async Task InstallAsync_DownloadsFromTheSourcesBase_AndInstalls()
    {
        var (service, downloader, modules, _) = Build();
        var bytes = Package("test.module");
        downloader.Files["https://example.com/dl/v1/test.mwm"] = bytes;

        var entry = Entry("test.module", bytes, "v1/test.mwm");
        await service.InstallAsync(Snapshot(PackageTrustDecision.Trusted, entry), entry);

        // The URL came from the source's compiled-in base plus the entry's relative path — the
        // catalog never named a host.
        Assert.Equal("https://example.com/dl/v1/test.mwm", Assert.Single(downloader.Requested));
        Assert.Single(modules.Installed);
    }

    [Fact]
    public async Task InstallAsync_HashMismatch_IsARefusal_NotAPrompt()
    {
        var (service, downloader, modules, _) = Build();
        var bytes = Package("test.module");
        downloader.Files["https://example.com/dl/v1/test.mwm"] = bytes;

        // The catalog claims a different hash than what the download actually is.
        var entry = Entry("test.module", bytes, "v1/test.mwm") with { Sha256 = new string('a', 64) };

        var ex = await Assert.ThrowsAsync<CatalogInstallException>(
            () => service.InstallAsync(Snapshot(PackageTrustDecision.Trusted, entry), entry));

        Assert.Contains("didn't match", ex.Message);
        Assert.Empty(modules.Installed);
    }

    [Theory]
    [InlineData(PackageTrustDecision.Unsigned)]
    [InlineData(PackageTrustDecision.UnrecognizedSigner)]
    [InlineData(PackageTrustDecision.Blocked)]
    public async Task InstallAsync_RefusesAnUntrustedCatalog_WithoutDownloadingAnything(PackageTrustDecision decision)
    {
        // An entry's hash is only as trustworthy as the signature over the catalog carrying it, so
        // this has to fail before any bytes are fetched, not after.
        var (service, downloader, modules, _) = Build();
        var entry = Entry("test.module", Package("test.module"), "v1/test.mwm");

        await Assert.ThrowsAsync<CatalogInstallException>(
            () => service.InstallAsync(Snapshot(decision, entry), entry));

        Assert.Empty(downloader.Requested);
        Assert.Empty(modules.Installed);
    }

    [Fact]
    public async Task InstallAsync_AutoInstallsAMissingDependency_BeforeTheModule()
    {
        var (service, downloader, modules, packs) = Build();
        var packBytes = Package("dep.pack", "assets");
        var moduleBytes = Package("test.module");
        downloader.Files["https://example.com/dl/v1/dep.mwassets"] = packBytes;
        downloader.Files["https://example.com/dl/v1/test.mwm"] = moduleBytes;

        var packEntry = Entry("dep.pack", packBytes, "v1/dep.mwassets", "assets") with { Version = "0.1.0" };
        var moduleEntry = Entry("test.module", moduleBytes, "v1/test.mwm", "module",
            new ModuleDependency { Id = "dep.pack", Version = "0.1.0" });

        await service.InstallAsync(Snapshot(PackageTrustDecision.Trusted, packEntry, moduleEntry), moduleEntry);

        // Dependency first, and recorded as Auto so removing the last dependent module cleans it up.
        Assert.Equal(
            ["https://example.com/dl/v1/dep.mwassets", "https://example.com/dl/v1/test.mwm"],
            downloader.Requested);
        Assert.Equal(AssetPackInstallSource.Auto, Assert.Single(packs.InstallSources));
        Assert.Single(modules.Installed);
    }

    [Fact]
    public async Task InstallAsync_SkipsADependencyThatIsAlreadyInstalled()
    {
        var (service, downloader, _, packs) = Build();
        packs.Installed.Add(new InstalledAssetPack("dep.pack", "0.1.0", "Dep", "x", AssetPackInstallSource.Manual));

        var moduleBytes = Package("test.module");
        downloader.Files["https://example.com/dl/v1/test.mwm"] = moduleBytes;
        var moduleEntry = Entry("test.module", moduleBytes, "v1/test.mwm", "module",
            new ModuleDependency { Id = "dep.pack", Version = "0.1.0" });

        await service.InstallAsync(Snapshot(PackageTrustDecision.Trusted, moduleEntry), moduleEntry);

        // Only the module was fetched, and the manually-installed pack wasn't demoted to Auto.
        Assert.Equal("https://example.com/dl/v1/test.mwm", Assert.Single(downloader.Requested));
        Assert.Empty(packs.InstallSources);
    }

    [Fact]
    public async Task InstallAsync_DependencyTheSourceDoesntPublish_FailsBeforeInstalling()
    {
        var (service, downloader, modules, _) = Build();
        var moduleBytes = Package("test.module");
        downloader.Files["https://example.com/dl/v1/test.mwm"] = moduleBytes;
        var moduleEntry = Entry("test.module", moduleBytes, "v1/test.mwm", "module",
            new ModuleDependency { Id = "absent.pack", Version = "9.9.9" });

        var ex = await Assert.ThrowsAsync<CatalogInstallException>(
            () => service.InstallAsync(Snapshot(PackageTrustDecision.Trusted, moduleEntry), moduleEntry));

        Assert.Contains("absent.pack", ex.Message);
        Assert.Empty(modules.Installed);
    }

    [Fact]
    public async Task InstallAsync_AnAssetPackRequestedDirectly_IsManualNotAuto()
    {
        var (service, downloader, _, packs) = Build();
        var packBytes = Package("dep.pack", "assets");
        downloader.Files["https://example.com/dl/v1/dep.mwassets"] = packBytes;
        var entry = Entry("dep.pack", packBytes, "v1/dep.mwassets", "assets");

        await service.InstallAsync(Snapshot(PackageTrustDecision.Trusted, entry), entry);

        Assert.Equal(AssetPackInstallSource.Manual, Assert.Single(packs.InstallSources));
    }
}

/// <summary>ModuleHasher only uses JS for its crypto.subtle fast path; failing the call exercises its managed fallback.</summary>
file sealed class NullJsRuntime : IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        throw new JSException("no JS host in tests");

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        throw new JSException("no JS host in tests");
}
