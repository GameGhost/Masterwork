# App themes are build-time white-label flavors, not downloadable content

A flavor (theme, app name, content source, pinned publisher) is chosen by MSBuild when a head is built, and its theme is a compiled Razor Class Library. We designed runtime theme delivery (a `.mwtheme` package plus theme-authored screens) and shelved it: it made the store-readiness work too large, and the stores' rules on downloaded code (Apple 2.5.2, Google Play's dynamic-code policy) only allow downloaded *data* interpreted by code that already shipped. A native app's icon and name are fixed at build time on every platform anyway, so a downloaded theme could never fully re-brand the app.

## Considered Options

- **Runtime-downloadable themes**: shelved, as above. Revisit only if a real need for community themes appears.
- **Discovering the theme by a reflection scan**: rejected. .NET loads assemblies lazily and drops unused references, so the scan found nothing. The host instead carries a build-stamped `[assembly: ThemeModule(typeof(...))]`.
