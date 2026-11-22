# Modules depend on asset packs by exact version, installed side by side

A module's `dependencies:` entry pins one exact asset-pack version, not a range, and several versions of the same pack can be installed at once. Only one module is ever active in a session, so there's no conflict to resolve: loading just needs that exact version present, fetched if it's missing. Packs installed automatically are removed when no installed module still references that exact id and version. That check scans manifests when a module is deleted rather than keeping a reference count that could drift.
