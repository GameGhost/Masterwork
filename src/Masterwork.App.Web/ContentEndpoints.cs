namespace Masterwork.App.Web;

/// <summary>How this site hands package bytes to the browser.</summary>
public enum ContentDeliveryMode
{
    /// <summary>
    /// Stream the upstream response through this origin. Works with any upstream, including one
    /// that sends no CORS headers — which is why it's the default over GitHub Releases. Costs this
    /// site the bandwidth of every download.
    /// </summary>
    Proxy,

    /// <summary>
    /// Redirect the browser to the upstream URL.
    /// <b>Only valid when the upstream sends its own <c>Access-Control-Allow-Origin</c></b> — a
    /// redirect doesn't exempt the final response from CORS, so pointing this at GitHub Releases
    /// would simply fail in the browser. This is the mode to switch to once a CDN fronts the
    /// content.
    /// </summary>
    Redirect,
}

/// <summary>Where this site fetches content from, and how it serves it. Bound from the <c>Content</c> configuration section.</summary>
public sealed class ContentOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Content";

    /// <summary>Upstream catalog URL. Its <c>.sig</c> sibling is derived, never configured separately.</summary>
    public string CatalogUrl { get; set; } = Masterwork.App.Shared.Services.WhiteLabelConfig.CatalogUrl;

    /// <summary>Upstream base that catalog entry paths resolve against.</summary>
    public string PackageBaseUrl { get; set; } = Masterwork.App.Shared.Services.WhiteLabelConfig.PackageBaseUrl;

    /// <summary>Proxy (default) or redirect — see <see cref="ContentDeliveryMode"/>.</summary>
    public ContentDeliveryMode Mode { get; set; } = ContentDeliveryMode.Proxy;
}

/// <summary>
/// This site's own content endpoints, and the only content route its WebAssembly client knows.
///
/// The client asks this origin for <c>/content/catalog.json</c> and
/// <c>/content/packages/{path}</c> and nothing else — no upstream URL is ever passed in, by query
/// string or otherwise, so there is no way to make this site fetch an arbitrary address. Where those
/// two routes actually point is this site's configuration (<see cref="ContentOptions"/>), matching
/// what a native build compiles in.
///
/// The client only comes here for its *primary* source. A source the player adds themselves is
/// fetched by the browser directly, which means that source has to serve its own CORS headers —
/// third-party hosting is the third party's responsibility.
/// </summary>
public static class ContentEndpoints
{
    /// <summary>Name of the redirect-less <see cref="HttpClient"/> these endpoints use.</summary>
    public const string HttpClientName = "content-upstream";

    private const int MaxRedirects = 5;

    /// <summary>Binds <see cref="ContentOptions"/> and registers the upstream <see cref="HttpClient"/>.</summary>
    public static IServiceCollection AddContentEndpoints(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ContentOptions>(configuration.GetSection(ContentOptions.SectionName));

        services.AddHttpClient(HttpClientName)
            // Followed manually below so a redirect chain can't quietly exceed a sane hop count, and
            // so proxy mode streams the final response rather than a 302 body.
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        return services;
    }

    /// <summary>Maps the fixed content routes.</summary>
    public static void MapContentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The catalog is always streamed, never redirected, regardless of Mode: it's small, it's
        // re-fetched far more often than packages, and the client must read its exact bytes to
        // check the detached signature over them.
        endpoints.MapGet("/content/catalog.json", (
            Microsoft.Extensions.Options.IOptions<ContentOptions> options,
            HttpContext context,
            IHttpClientFactory factory,
            ILoggerFactory loggers,
            CancellationToken ct) => ServeAsync(options.Value.CatalogUrl, forceProxy: true, options.Value, context, factory, loggers, ct));

        endpoints.MapGet("/content/catalog.json.sig", (
            Microsoft.Extensions.Options.IOptions<ContentOptions> options,
            HttpContext context,
            IHttpClientFactory factory,
            ILoggerFactory loggers,
            CancellationToken ct) => ServeAsync(options.Value.CatalogUrl + ".sig", forceProxy: true, options.Value, context, factory, loggers, ct));

        endpoints.MapGet("/content/packages/{**path}", async (
            string path,
            Microsoft.Extensions.Options.IOptions<ContentOptions> options,
            HttpContext context,
            IHttpClientFactory factory,
            ILoggerFactory loggers,
            CancellationToken ct) =>
        {
            // The same rule the catalog parser applies to an entry's path, re-applied to whatever
            // actually arrived on the wire — this route is reachable without a catalog.
            if (!Masterwork.ModuleFormat.CatalogPaths.IsSafeRelative(path))
            {
                return Results.BadRequest("Not a valid content path.");
            }

            var upstream = options.Value.PackageBaseUrl.TrimEnd('/') + "/" + path;
            return await ServeAsync(upstream, forceProxy: false, options.Value, context, factory, loggers, ct);
        });
    }

    private static async Task<IResult> ServeAsync(
        string upstreamUrl,
        bool forceProxy,
        ContentOptions options,
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!forceProxy && options.Mode == ContentDeliveryMode.Redirect)
        {
            return Results.Redirect(upstreamUrl, permanent: false);
        }

        var logger = loggerFactory.CreateLogger(typeof(ContentEndpoints));
        var http = httpClientFactory.CreateClient(HttpClientName);
        var target = new Uri(upstreamUrl);

        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode is System.Net.HttpStatusCode.Moved
                or System.Net.HttpStatusCode.Found
                or System.Net.HttpStatusCode.TemporaryRedirect
                or System.Net.HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                response.Dispose();

                if (location is null)
                {
                    logger.LogWarning("Upstream {Url} redirected with no location", target);
                    return Results.StatusCode(StatusCodes.Status502BadGateway);
                }

                target = location.IsAbsoluteUri ? location : new Uri(target, location);
                continue;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Upstream {Url} returned {Status}", target, (int)response.StatusCode);
                    return Results.StatusCode((int)response.StatusCode);
                }

                // Only these two headers cross over — everything else upstream sends (cookies, auth,
                // storage metadata) stays on this side.
                if (response.Content.Headers.ContentLength is { } length)
                {
                    context.Response.ContentLength = length;
                }

                context.Response.ContentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

                await using var upstream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await upstream.CopyToAsync(context.Response.Body, cancellationToken);
                return Results.Empty;
            }
        }

        logger.LogWarning("Upstream {Url} exceeded {MaxRedirects} redirects", upstreamUrl, MaxRedirects);
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }
}
