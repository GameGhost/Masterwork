# The official scenarios are hand-maintained after a one-time extraction

The three official scenarios were extracted from Cradle C# by `Masterwork.Extractor` and are now maintained by hand-editing their YAML in Masterwork-Modules. They are not re-extracted routinely. The extractor is at the end of active development and changes only if real play turns up an extraction bug. We froze extraction so that changes no extractor could produce (moving shared content into `mwf-common-assets`, placing achievement triggers) can be made directly, without first making every one of them survive a re-extraction.

## Consequences

`passages/` is still nominally extractor-owned. Re-extracting would now overwrite hand edits, so it needs a deliberate diff and merge, not a routine re-run.
