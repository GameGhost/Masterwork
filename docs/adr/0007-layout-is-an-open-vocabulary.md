# `layout` is an open string vocabulary owned by modules

The app never branches on a passage's or popup's `layout` value. It only adds a `layout-{value}` CSS class and composes optional chrome from a `layouts/{id}.mws.yaml` file. Shared layouts live in an asset pack (`mwf-common-assets`) as ordinary chrome files, not as a built-in registry. Earlier designs had built-in popup types needing bespoke components (`voting`, `bidding`). They were retired once a countdown-then-reveal turned out to be expressible as a nested popup, so new interaction patterns go into module content rather than app code.

## Considered Options

- **Built-in popup layout types with Razor components**: rejected. Every new pattern would need an app release, and module authors couldn't add their own.
