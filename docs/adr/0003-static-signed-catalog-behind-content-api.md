# Module distribution is a static signed catalog behind a redirecting content API

There is no server-side module registry. Each source publishes one static, signed `catalog.json` that lists every package with its browse metadata and SHA-256. Every head reaches content only through the flavor site's `/api/content/...` routes, which are a 302 redirect (an Azure Function on Static Web Apps) into wherever the content is actually stored. Storage can then move, for example to Blob or R2 when the Static Web Apps size or bandwidth limits are reached, without an app update. We chose this over a registry service so that hosting stays free-tier and operations stay at "deploy static files".

## Consequences

New content routes must stay under `ContentApi.BasePath`. GitHub Releases can't serve packages to the browser because it sends no CORS headers on the final download, which is one reason packages are served from the site rather than from Releases.
