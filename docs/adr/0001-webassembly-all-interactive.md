# The web app runs fully client-side (WebAssembly, all-interactive)

The web heads use Blazor WebAssembly with every component interactive in the browser, not the `maui-blazor-web` template's default Server/SignalR circuit. The engine and all UI run client-side on every head, so gameplay never makes a server round-trip and the production web app can be plain static files (`Masterwork.App.Web.Static` on Azure Static Web Apps) with no custom server.

## Consequences

Browser-runtime constraints apply to all shared code: no `X509Certificates`, managed SHA-256 roughly a second per megabyte, and `Task.Yield()` that doesn't yield to the event loop. CLAUDE.md's "Cryptography under Blazor WASM" section and the `Task.Delay(1)` yielding pattern exist because of this decision.
