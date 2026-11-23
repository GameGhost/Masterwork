# Masterwork

A companion app that plays scenario modules for a narrative board game, plus the format and tools those modules are built with.

## Language

### Languages

**UI language**:
The language of the app's own screens, separate from any module's content.
_Avoid_: app language, locale (on its own)

**Text language**:
The language a playthrough's module text is shown in. It is fixed for the whole playthrough.
_Avoid_: content language, module language, display language

**Voice language**:
The language of a module's spoken narration, or None. It never applies to music or sound effects.
_Avoid_: audio language

**Voice track**:
One reader's (or one generated voice's) recordings of a module's narration in a single voice language. It may cover only some of the narrated passages.
_Avoid_: voice, narrator, reading, VO set

**Voice pack**:
An optional package that adds voice tracks to one specific module. The module doesn't depend on it.
_Avoid_: audio pack, VO module, narration pack

**Asset pack**:
A package of shared content (styles, fonts, images, music, effects) that modules depend on by exact version.
_Avoid_: common assets, shared module
