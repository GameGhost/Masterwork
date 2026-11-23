# Voice packs attach to a module; the module doesn't depend on them

A voice pack names the module it voices (`for:` with the module id and a minimum version). The module never lists the pack, and nothing pins an exact version. Narration is resolved by slug, and a slug the pack has no file for just hides that `audio_track`. So a module update that adds or renames narration doesn't break an installed pack: it leaves a gap.

We chose this over treating voice packs like asset packs (exact-version `dependencies:`, ADR 0005). Voice tracks are optional, and they are likely to be the largest part of a module. They're split by language so a player installs only their own language's audio, and other people can make them after the module ships. A dependency pin would make each module release name every pack, and make every pack republish whenever its module did.

## Consequences

- The minimum version is enforced at install and update. A pack that needs a newer module is refused, and the app offers the module update. A ceiling at the next major version is reserved but not checked yet.
- The catalog lists only each pack's latest version. A pack update either stays compatible with older modules or is released with a new module version.
- Because there is no pin, the packer can't prove a pack is complete. It reports each track's coverage against a module at pack time instead.
- The official scenarios ship no built-in voices. Their English VO is a voice pack per scenario.
