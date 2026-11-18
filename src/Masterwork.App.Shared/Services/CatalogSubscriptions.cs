using Masterwork.ModuleFormat;
using Microsoft.Extensions.Logging;

namespace Masterwork.App.Shared.Services;

/// <summary>Why adding a source didn't work, in terms a player can act on.</summary>
public enum AddSourceFailure
{
    /// <summary>Not an absolute https URL.</summary>
    NotAValidUrl,

    /// <summary>Already subscribed, or it's the build's own source.</summary>
    AlreadySubscribed,

    /// <summary>Nothing answered, or the host refused the browser (see <see cref="AddSourceResult.MayBeCors"/>).</summary>
    Unreachable,

    /// <summary>Something answered, but it isn't a catalog this build understands.</summary>
    NotACatalog,

    /// <summary>A catalog, but unsigned or signed by someone this build has no reason to trust.</summary>
    NotTrusted,
}

/// <summary>The outcome of trying to add a source.</summary>
/// <param name="Failure">Null when the source was added.</param>
/// <param name="Detail">Extra context for <see cref="AddSourceFailure.NotACatalog"/>, straight from the parser.</param>
/// <param name="MayBeCors">
/// Set when a fetch failed on the web head, where the likeliest cause is the source's host not
/// permitting browser access rather than the source being down. Worth saying out loud, because the
/// same URL works on a native build and the player would otherwise have no way to tell the two
/// apart.
/// </param>
public sealed record AddSourceResult(AddSourceFailure? Failure = null, string? Detail = null, bool MayBeCors = false)
{
    /// <summary>Whether the source was added.</summary>
    public bool Succeeded => Failure is null;
}

/// <summary>
/// Adds and removes the catalog sources a player subscribes to themselves.
///
/// A source is fetched and verified before it's saved, so a typo, an unsigned catalog, or a host a
/// browser can't reach is reported while the player is still looking at the field they typed it
/// into — rather than becoming a row that silently never produces anything.
/// </summary>
public sealed class CatalogSubscriptions(
    CatalogService catalog,
    IAppSettingsStore settingsStore,
    ILogger<CatalogSubscriptions> logger)
{
    /// <summary>Validates <paramref name="catalogUrl"/> by actually fetching it, and subscribes on success.</summary>
    public async Task<AddSourceResult> AddAsync(string catalogUrl, ContentSource primary, CancellationToken cancellationToken = default)
    {
        var url = catalogUrl.Trim();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return new AddSourceResult(AddSourceFailure.NotAValidUrl);
        }

        var settings = await settingsStore.LoadAsync();
        if (settings.CustomCatalogSources.Contains(url, StringComparer.OrdinalIgnoreCase)
            || string.Equals(url, primary.CatalogUrl, StringComparison.OrdinalIgnoreCase))
        {
            return new AddSourceResult(AddSourceFailure.AlreadySubscribed);
        }

        var source = ContentSource.ForAddedSource(url);

        CatalogSnapshot snapshot;
        try
        {
            snapshot = await catalog.FetchAsync(source, cancellationToken);
        }
        catch (ContentDownloadException ex)
        {
            logger.LogWarning(ex, "Could not reach candidate source {Url}", url);
            return new AddSourceResult(AddSourceFailure.Unreachable, MayBeCors: OperatingSystem.IsBrowser());
        }
        catch (CatalogParseException ex)
        {
            logger.LogWarning(ex, "Candidate source {Url} is not a usable catalog", url);
            return new AddSourceResult(AddSourceFailure.NotACatalog, ex.Message);
        }

        // Subscribing to a source nothing can be installed from would just be a row that never
        // works, so the trust check happens here rather than at install time.
        if (snapshot.Decision != PackageTrustDecision.Trusted)
        {
            logger.LogWarning("Candidate source {Url} is {Decision}", url, snapshot.Decision);
            return new AddSourceResult(AddSourceFailure.NotTrusted, snapshot.Signature.CertificateCommonName);
        }

        await settingsStore.SaveAsync(settings with { CustomCatalogSources = [.. settings.CustomCatalogSources, url] });
        logger.LogInformation("Subscribed to source {Url} ({EntryCount} entries)", url, snapshot.Catalog.Entries.Count);
        return new AddSourceResult();
    }

    /// <summary>
    /// Unsubscribes from a source. Purely a subscription change: content already installed from it
    /// stays exactly as installed, it's just no longer checked for updates.
    /// </summary>
    public async Task RemoveAsync(string catalogUrl)
    {
        var settings = await settingsStore.LoadAsync();
        await settingsStore.SaveAsync(settings with
        {
            CustomCatalogSources = [.. settings.CustomCatalogSources.Where(u => !string.Equals(u, catalogUrl, StringComparison.OrdinalIgnoreCase))],
        });

        logger.LogInformation("Unsubscribed from source {Url}", catalogUrl);
    }

    /// <summary>
    /// Hides or shows one source's listing of one module. Never installs or removes anything.
    ///
    /// Hiding a listing doesn't only remove it — because sources resolve in subscription order, it
    /// promotes the next source's listing of the same module, if there is one. That's the mechanism
    /// for moving a single module onto a pre-release source while the rest stay on the stable one.
    /// </summary>
    public async Task SetHiddenAsync(string sourceKey, string moduleId, bool hidden)
    {
        var settings = await settingsStore.LoadAsync();
        var current = settings.HiddenCatalogListings;
        var alreadyHidden = current.Any(h => h.SourceKey == sourceKey && h.ModuleId == moduleId);

        if (hidden == alreadyHidden)
        {
            return;
        }

        await settingsStore.SaveAsync(settings with
        {
            HiddenCatalogListings = hidden
                ? [.. current, new HiddenCatalogListing(sourceKey, moduleId)]
                : [.. current.Where(h => h.SourceKey != sourceKey || h.ModuleId != moduleId)],
        });

        logger.LogInformation(
            "{Action} {ModuleId} from source {SourceKey}", hidden ? "Hid" : "Unhid", moduleId, sourceKey);
    }
}
