using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Masterwork.Tests;

/// <summary>
/// How several subscribed sources resolve into one library, and what hiding a listing does. These
/// run the real <see cref="ModuleLibrary"/> over a real <see cref="CatalogService"/>, with only the
/// network and the stores stubbed — the merge rules are the thing under test, so reimplementing them
/// here would test nothing.
/// </summary>
public class ModuleLibraryMergeTests
{
    private const string PrimaryUrl = "https://primary.example.com/.catalog/catalog.json";
    private const string AddedUrl = "https://beta.example.com/.catalog/catalog.json";

    private static readonly ContentSource Primary = new(PrimaryUrl, "https://primary.example.com/dl");

    private static byte[] Catalog(string title, params (string Id, string Version)[] modules) =>
        CatalogParser.Write(new CatalogDocument
        {
            Format = CatalogFormatVersion.Current,
            Title = title,
            Entries =
            [
                .. modules.Select(m => new CatalogEntry
                {
                    Type = "module",
                    Id = m.Id,
                    Title = m.Id,
                    Version = m.Version,
                    Path = $"v{m.Version}/{m.Id}.mwm",
                    Sha256 = new string('a', 64),
                    Size = 1,
                }),
            ],
        });

    private sealed class StubDownloader : IContentDownloader
    {
        public Dictionary<string, byte[]> Files { get; } = [];

        public Task<byte[]> GetAsync(string url, IProgress<(long Done, long? Total)>? progress = null, CancellationToken cancellationToken = default) =>
            Files.TryGetValue(url, out var bytes)
                ? Task.FromResult(bytes)
                : throw new ContentDownloadException($"404: {url}");
    }

    private sealed class StubModuleStore(params InstalledModule[] installed) : IModuleStore
    {
        public Task<IReadOnlyList<InstalledModule>> ListAsync() => Task.FromResult<IReadOnlyList<InstalledModule>>(installed);

        public Task<InstalledModule> InstallAsync(byte[] mwmBytes, string sha256, IProgress<(int Done, int Total)>? progress = null) =>
            throw new NotSupportedException();

        public Task<LoadedModuleContent> LoadAsync(string moduleId, string? locale = null) => throw new NotSupportedException();

        public Task DeleteAsync(string moduleId) => throw new NotSupportedException();
    }

    private sealed class StubSettingsStore(AppSettings settings) : IAppSettingsStore
    {
        public AppSettings Settings { get; private set; } = settings;

        public Task<AppSettings> LoadAsync() => Task.FromResult(Settings);

        public Task SaveAsync(AppSettings value)
        {
            Settings = value;
            return Task.CompletedTask;
        }
    }

    private sealed class NoStorage : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            // No cache, and nothing to write to — every load goes to the stub downloader.
            identifier == "localStorage.getItem"
                ? ValueTask.FromResult((TValue)(object?)null!)
                : ValueTask.FromResult<TValue>(default!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private static async Task<ModuleLibrary.Result> LoadAsync(
        StubDownloader downloader, AppSettings settings, params InstalledModule[] installed)
    {
        var js = new NoStorage();
        var settingsStore = new StubSettingsStore(settings);
        var catalog = new CatalogService(
            downloader,
            new SignatureVerifier(js, new BrowserCrypto(js), NullLogger<SignatureVerifier>.Instance),
            settingsStore,
            js,
            NullLogger<CatalogService>.Instance);

        var library = new ModuleLibrary(
            catalog, new StubModuleStore(installed), settingsStore, NullLogger<ModuleLibrary>.Instance);

        return await library.LoadAsync(Primary);
    }

    private static StubDownloader TwoSources() => new()
    {
        Files =
        {
            [PrimaryUrl] = Catalog("Stable", ("shared.module", "1.0.0"), ("only.primary", "1.0.0")),
            [AddedUrl] = Catalog("Beta", ("shared.module", "2.0.0"), ("only.beta", "1.0.0")),
        },
    };

    private static AppSettings WithBeta(params HiddenCatalogListing[] hidden) => AppSettings.Default with
    {
        CustomCatalogSources = [AddedUrl],
        HiddenCatalogListings = hidden,
    };

    [Fact]
    public async Task ModulesFromEverySource_AppearOnce()
    {
        var result = await LoadAsync(TwoSources(), WithBeta());

        Assert.Equal(
            ["only.beta", "only.primary", "shared.module"],
            result.Entries.Select(e => e.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ASharedModule_ResolvesToThePrimarySource_NotTheHigherVersion()
    {
        // Beta publishes 2.0.0; the primary's 1.0.0 still wins, because precedence is subscription
        // order rather than version number.
        var result = await LoadAsync(TwoSources(), WithBeta());

        Assert.Equal("1.0.0", result.Entries.Single(e => e.Id == "shared.module").Catalog!.Version);
    }

    [Fact]
    public async Task HidingThePrimaryListing_PromotesTheNextSourcesListing()
    {
        // The point of per-source hiding: moving one module onto the pre-release source without
        // touching where anything else comes from.
        var result = await LoadAsync(
            TwoSources(),
            WithBeta(new HiddenCatalogListing(CatalogSourceKeys.Primary, "shared.module")));

        Assert.Equal("2.0.0", result.Entries.Single(e => e.Id == "shared.module").Catalog!.Version);

        // And only that module moved.
        Assert.Equal("1.0.0", result.Entries.Single(e => e.Id == "only.primary").Catalog!.Version);
    }

    [Fact]
    public async Task HidingEverySourcesListing_RemovesAnUninstalledModuleEntirely()
    {
        var result = await LoadAsync(
            TwoSources(),
            WithBeta(
                new HiddenCatalogListing(CatalogSourceKeys.Primary, "shared.module"),
                new HiddenCatalogListing(AddedUrl, "shared.module")));

        Assert.DoesNotContain(result.Entries, e => e.Id == "shared.module");
    }

    [Fact]
    public async Task HidingEverySourcesListing_StillLeavesAnInstalledModulePlayable()
    {
        // Hiding says where updates come from, not whether something already installed can be
        // played — so it must never take a playable module off the New Game list.
        var result = await LoadAsync(
            TwoSources(),
            WithBeta(
                new HiddenCatalogListing(CatalogSourceKeys.Primary, "shared.module"),
                new HiddenCatalogListing(AddedUrl, "shared.module")),
            new InstalledModule("shared.module", "1.0.0", "Shared", "", [], "sha"));

        var entry = result.Entries.Single(e => e.Id == "shared.module");
        Assert.Equal(LibraryEntryState.Installed, entry.State);
        Assert.Null(entry.Catalog);
    }

    [Fact]
    public async Task HidingOnlyTheBetaListing_LeavesThePrimaryResolutionAlone()
    {
        var result = await LoadAsync(
            TwoSources(),
            WithBeta(new HiddenCatalogListing(AddedUrl, "shared.module")));

        Assert.Equal("1.0.0", result.Entries.Single(e => e.Id == "shared.module").Catalog!.Version);
    }

    [Fact]
    public async Task AnUnreachableSource_DoesNotTakeTheLibraryDownWithIt()
    {
        var downloader = TwoSources();
        downloader.Files.Remove(AddedUrl);

        var result = await LoadAsync(downloader, WithBeta());

        Assert.Contains(result.Entries, e => e.Id == "only.primary");
        Assert.DoesNotContain(result.Entries, e => e.Id == "only.beta");

        // The source is still listed, so the UI can say it couldn't be reached rather than silently
        // dropping it from the player's subscriptions.
        Assert.Null(result.Sources.Single(s => !s.IsPrimary).Snapshot);
    }

    [Fact]
    public async Task InstalledButUnpublished_StillAppears()
    {
        // A manual upload nothing publishes.
        var result = await LoadAsync(
            TwoSources(), WithBeta(), new InstalledModule("side.loaded", "1.0.0", "Side", "", [], "sha"));

        var entry = result.Entries.Single(e => e.Id == "side.loaded");
        Assert.Equal(LibraryEntryState.Installed, entry.State);
        Assert.Null(entry.Source);
    }

    [Fact]
    public async Task AssetPacks_AreNeverLibraryEntries()
    {
        var downloader = new StubDownloader
        {
            Files =
            {
                [PrimaryUrl] = CatalogParser.Write(new CatalogDocument
                {
                    Format = CatalogFormatVersion.Current,
                    Title = "Stable",
                    Entries =
                    [
                        new CatalogEntry
                        {
                            Type = "assets", Id = "shared.pack", Title = "Pack", Version = "1.0.0",
                            Path = "v1/pack.mwassets", Sha256 = new string('a', 64), Size = 1,
                        },
                    ],
                }),
            },
        };

        var result = await LoadAsync(downloader, AppSettings.Default);

        // Published so a module's dependency can be resolved, never browsed on its own.
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task Entries_AreOrderedBySourceThenCatalogOrder_NotByTitle()
    {
        // Catalog order is deliberately not alphabetical, so this can't pass by accident.
        var downloader = new StubDownloader
        {
            Files =
            {
                [PrimaryUrl] = Catalog("Stable", ("charlie", "1.0.0"), ("alpha", "1.0.0"), ("bravo", "1.0.0")),
                [AddedUrl] = Catalog("Beta", ("zulu", "1.0.0"), ("alpha", "2.0.0")),
            },
        };

        var result = await LoadAsync(
            downloader, WithBeta(), new InstalledModule("manual.upload", "1.0.0", "Manual", "", [], "sha"));

        // The primary's modules in its own order; then the beta source's (alpha is already claimed
        // by the primary, so only zulu); then anything installed that no source ranks.
        Assert.Equal(["charlie", "alpha", "bravo", "zulu", "manual.upload"], result.Entries.Select(e => e.Id));
    }

    [Fact]
    public async Task APromotedListing_TakesItsPositionFromTheSourceThatNowSuppliesIt()
    {
        var downloader = new StubDownloader
        {
            Files =
            {
                [PrimaryUrl] = Catalog("Stable", ("alpha", "1.0.0"), ("bravo", "1.0.0")),
                [AddedUrl] = Catalog("Beta", ("alpha", "2.0.0")),
            },
        };

        // Hiding the primary's alpha hands it to the beta source, so it now sorts with the beta
        // source's modules rather than holding its old slot.
        var result = await LoadAsync(
            downloader, WithBeta(new HiddenCatalogListing(CatalogSourceKeys.Primary, "alpha")));

        Assert.Equal(["bravo", "alpha"], result.Entries.Select(e => e.Id));
    }

    [Fact]
    public async Task SourceKeys_IdentifyThePrimaryByConstant_AndAddedSourcesByUrl()
    {
        // The primary's URL is the deployment's own origin on the web head, so keying hidden
        // listings by it would drop them whenever the app moved address.
        var result = await LoadAsync(TwoSources(), WithBeta());

        Assert.Equal(CatalogSourceKeys.Primary, result.Sources.Single(s => s.IsPrimary).Key);
        Assert.Equal(AddedUrl, result.Sources.Single(s => !s.IsPrimary).Key);
    }
}
