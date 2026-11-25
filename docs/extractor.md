# MasterWork Extractor

`MasterWork.Extractor` is the CLI tool that converts Cradle C# scenario files into MWS v0.4 YAML passages, ready for the game engine.

**Status: nearing the end of active development.** All three official scenarios (*The Cost of
Disease*, *A Time of War*, *Fear of the Unknown*) have been extracted cleanly (0 unknown nodes in
two of the three; see Current Extraction Results below) and shipped in public releases. Further
extractor changes are expected only if real play of the shipped modules surfaces a genuine bug, not
from new Cradle patterns still needing support. Once that risk is judged low, maintenance of the
three modules' content shifts from "routinely re-extracted" to hand-editing the already-extracted
YAML directly — the same way the fourth, fully hand-authored module (`my-fathers-work-template`)
already works.

---

## What It Does

The three original *My Father's Work* scenarios were built with **Cradle 2.0**, a Unity plugin that transpiles Twine/Harlowe `.html` story files into C# coroutine code. Each scenario is a single large `.cs` file (~30,000–35,000 lines, ~300–380 passages).

The extractor:
1. Parses the C# source with Roslyn (no regex — full AST)
2. Converts each passage into MWS v0.4 YAML nodes
3. Extracts all human-readable strings into an `en-US.restext` locale file (replacing them with `restext://Key` references in the YAML)
4. Assigns deterministic PRNG seed keys to all random calls
5. Writes one `.mws.yaml` file per passage, plus a variables manifest and extraction report

---

## Usage

```
dotnet run --project src/Masterwork.Extractor -- <input> <passages-out-dir> [options]
```

### Required Arguments

| Argument | Description |
|---|---|
| `<input>` | Path to a `.cs` source file, or a directory containing `.cs` files |
| `<passages-out-dir>` | Directory to write `.mws.yaml` passage files into (created if it does not exist) |

### Options

| Option | Description |
|---|---|
| `--module-title <title>` | Human-readable module title (used in the extraction report header). If omitted, derived from the source filename by splitting on capital letters. |
| `--module-id <id>` | Module identifier string (reserved for future use in the manifest). |
| `--sprite-map <json>` | Path to a `TheCostOfDisease_ItemObtain.json`-style file mapping sprite indices to asset slugs. Required for The Cost of Disease; not needed for the other scenarios. |
| `--progress-map <json>` | Path to a `{ "PassageName": { "layout": "...", "progress": N, "end_of_round_body": "...", "end_of_round_body2": "..." }, ... }` JSON map. `layout` overrides `InferLayout`'s tag-based result for that passage. `progress`/`end_of_round_body` together drive what happens at a matching `PassageTracker.instance.CheckProgress(passageName, ...)` call: if the entry has end-of-round body text, the link that calls `CheckProgress` becomes a `layout: end_of_round` popup (label/target carried over from the original link, `okay` fixed to the reference app's own "End of Round" button caption, `content` the two body strings, `onclose` the `_ProgressRound` assign) instead of a bare navigation link — matching the reference app's `ViewEndOfRound.SetEndOfRound` acknowledgement popup, which the source's `CheckProgress` call site alone doesn't represent (there is no Cradle passage for it; see `Masterwork-Modules/cost-of-disease/.source/TheCostofDisease_Eng_v10.cs`'s `ReminderroundEnd` passage, explicitly commented as a prototype-only stand-in never used by final app logic). If the entry has `progress` but no end-of-round body text, only the synthetic `_ProgressRound` assign is emitted (unchanged, plain-link behavior). A `CheckProgress` call whose current-passage name has no entry at all in the map is reported as a warning. Optional — omitting `--progress-map` entirely leaves layout inference and `CheckProgress` handling unchanged. `_ProgressRound` reflects rounds *completed so far* — it's `0` throughout round 1 and only reaches the mapped round's own value once the player has clicked past it *and* dismissed the end-of-round popup, not while that round's content is being displayed. See `Masterwork-Modules/progress-map.json` and `docs/mws-format-latest.md` §7 (`end_of_round`/`end_of_generation` popup examples) and §8 (including its timing note) for how a module turns `_ProgressRound` into an actual progress-bar display via `layouts/*.mws.yaml` chrome. |
| `--audio-map <json>` | Path to a `{ "PassageName": { "slug": "...", "title": "..." }, ... }` JSON map. For a matching passage, prepends a synthesized `audio_track` node — `asset: 'audio://vo/<slug>'` (a plain slug; the player's voice track picks the file), `title` from the map entry, `autoplay: true`, `bgm_behavior: 'pause'` — so real voice-over narration doesn't need a hand-authored `passages-override/` file. `title` is restext-eligible like any other node title. A passage with no map entry is unaffected (no warning — most passages have no narration). Optional — omitting `--audio-map` entirely leaves node synthesis off. See `Masterwork.Extractor.AudioMapper`. |
| `--variables-out <dir>` | Where `_variables.yaml` is written. Defaults to `<passages-out-dir>`. |
| `--restext-out <dir>` | Where `en-US.restext` is written. Defaults to `<passages-out-dir>`. |
| `--common-restext <file>` | Path to a manually curated `Key=Value` restext file. When a string is promoted to a Common key (used in 2+ passages), a matching curated ID (by exact text) is used instead of an auto-generated `Common_NNN` one, so override/manually-written passages have a stable name to reference instead of one that can shift on every re-extraction. Curated IDs never matched during extraction are omitted from the output restext file and reported as warnings. Purely an extractor-time input — `ModuleLoader` never reads this file. |
| `--include-debug` | Include passages gated behind the `devpage` debug flag. Excluded by default. |
| `--dry-run` | Parse and report without writing any output files. |
| `--seed-analysis` | Emit a seed key dependency report alongside the extraction output. |

The extractor no longer accepts hand-authored overrides — see [Module Overrides](#module-overrides-passages-override) below.

---

## Extraction Commands for the Three Scenarios

Run from this repo's root directory. Extracted modules live in the standalone `Masterwork-Modules`
repo — a sibling of this one, checked out alongside it. **All three scenarios are now fully
migrated**: each holds its own canonical Cradle source under `Masterwork-Modules/{module}/.source/`,
extraction reads from there, and each uses the same `passages/` + `passages-override/` split —
there's no shared external staging area to read from anymore. See `Masterwork-Modules/CLAUDE.md` for
the authoritative, up-to-date extraction commands (the shape below is the same for all three, just
with different source filenames/module names).

```powershell
# Shared across all three scenarios. <Masterwork-Modules> is the path to your local clone of the
# sibling Masterwork-Modules repo:
$spritemap   = "<path to a local copy of the Unity project's Assets/Resources/TheCostOfDisease_ItemObtain.json>"  # Cost of Disease only; see NOTICE.md for asset provenance — not tracked in any of these repos
$progressmap = "<Masterwork-Modules>/progress-map.json"   # shared by all three modules' hub passages
$audiomap    = "<Masterwork-Modules>/audio-map.json"      # shared by all three modules' VO-narrated intro passages
$modules     = "<Masterwork-Modules>"

# The Cost of Disease
$codbase = "$modules\cost-of-disease\.source"
dotnet run --project src/Masterwork.Extractor -- `
  "$codbase\TheCostofDisease_Eng_v10.cs" `
  "$modules\cost-of-disease\passages" `
  --variables-out "$modules\cost-of-disease" `
  --restext-out "$modules\cost-of-disease" `
  --module-title "The Cost of Disease" `
  --sprite-map $spritemap `
  --common-restext "$codbase\en-US.common.restext" `
  --progress-map $progressmap `
  --audio-map $audiomap

# A Time of War
$atowbase = "$modules\a-time-of-war\.source"
dotnet run --project src/Masterwork.Extractor -- `
  "$atowbase\ATimeofWar_Eng_v8.cs" `
  "$modules\a-time-of-war\passages" `
  --variables-out "$modules\a-time-of-war" `
  --restext-out "$modules\a-time-of-war" `
  --module-title "A Time of War" `
  --common-restext "$atowbase\en-US.common.restext" `
  --progress-map $progressmap `
  --audio-map $audiomap

# Fear of the Unknown
$fotubase = "$modules\fear-of-the-unknown\.source"
dotnet run --project src/Masterwork.Extractor -- `
  "$fotubase\FearoftheUnknown_Eng_v15.cs" `
  "$modules\fear-of-the-unknown\passages" `
  --variables-out "$modules\fear-of-the-unknown" `
  --restext-out "$modules\fear-of-the-unknown" `
  --common-restext "$fotubase\en-US.common.restext" `
  --progress-map $progressmap `
  --audio-map $audiomap
```

`--variables-out`/`--restext-out` put `_variables.yaml`/`en-US.restext` at the module root, next to
`manifest.yaml` and `passages-override/`. `--common-restext` gives stable IDs to Common strings (used
in 2+ passages) from each module's own hand-curated `en-US.common.restext`. `--progress-map` gives
`hub_early`/`hub_middle`/`hub_late`-style layout overrides plus `end_of_round` popups at the
reference app's real progress-bar checkpoints — the same `progress-map.json` file now covers hub
passages across all three modules, not just Cost of Disease.

> **Note:** `--module-title` is required for A Time of War (auto-generation capitalises "Of") and The Cost of Disease (auto-generation produces "The Costof Disease"). Fear of the Unknown's auto-generated title is fine as-is.

> **Note:** Re-running extraction only touches `passages/` — it never writes to `passages-override/`,
> so hand-authored passages there always survive a re-extraction.

> **Note:** the source `.cs` file's directory determines what the "# {path}:{line}" comments in each
> passage resolve to — that's why Cost of Disease reads from its own `.source/` copy (giving
> `../.source/TheCostofDisease_Eng_v10.cs`, a path valid inside `Masterwork-Modules`) rather than
> from an external reference location, which wouldn't resolve to a valid relative path from inside
> that repo.

---

## Output Files

For each run, the output directory contains:

| File | Description |
|---|---|
| `{NNN}-{PassageId}.mws.yaml` | One file per passage in the current MWS format (v0.4), numbered by source order |
| `_variables.yaml` | All discovered session variables with inferred types |
| `en-US.restext` | All extracted human-readable strings, one `Key=Value` per line |
| `_extraction-report.md` | Summary table, warnings, unknown nodes, isolated passages, input prompts. Written next to the source `.cs` file(s), not `<passages-out-dir>` — it's read while working on the Cradle source, so it belongs next to it. |

### `_variables.yaml` format

Each variable is normally one line — a name and its declared type, one of `string`, `int`, `bool`,
`record`, `string_array`, `int_array`, `bool_array`, `record_array` (the array split is
declaration-time documentation only; arrays are untyped at runtime):

```yaml
variables:
  round: 'int'
  wolves: 'string'
  build: 'string_array'
```

A variable only gets the expanded form — a mapping with `type:`/`default:` — when it needs a
non-canonical starting value (i.e. not empty string / `0` / `false` / empty record / empty array).
The extractor only ever derives a default this way from an actual Cradle `VarDefs` field
initializer (e.g. `public StoryVar @final5 = 3;`); it never guesses one from "the first place this
variable happens to get assigned a literal in the source," since that's just wherever the value
appears earliest in a ~30k-line file — arbitrary relative to actual game-start state, not a real
declared default:

```yaml
variables:
  final5:
    type: 'int'
    default: 3
```

At module load, every variable with no explicit default is seeded with its type's canonical zero
value (empty string, `0`, `false`, empty record, empty array).

### Passage filenames

Files are named `{index:D5}-{SanitizedPassageId}.mws.yaml` — for example `00255-Expedition3.mws.yaml`. The five-digit prefix preserves source order when listed alphabetically.

### Source line annotations

Every `.mws.yaml` file contains YAML comments for source navigation:

- **Passage header**: `# path/to/SourceFile.cs:line` above the `---` marker — points to the passage's main method. The path is relative to `<passages-out-dir>`, so it's `SourceFile.cs:line` when the source and output live side by side, or a longer `../../Reference/.../SourceFile.cs:line` when `--variables-out`/`--restext-out` split the module across directory trees.
- **Node comments**: same relative-path style, above every node at every nesting depth (top-level and inside conditional branches, switch cases, sections, foreach loops, popups). Use the `click-file` VS Code extension to make these relative-path links navigable.
- **Restext comments**: `# path/to/en-US.restext:line | "preview"` above every field that resolves to a `restext://Key` reference — relative from `<passages-out-dir>` to wherever `--restext-out` put the file (e.g. `../en-US.restext:42 | "..."` when passages/ and the restext file are split across directories, per the module layout below).
- **Navigation targets**: `# ./00042-PassageName.mws.yaml` appended inline to `target:` fields in `navigation`, `goto`, `include_passage` nodes — for passage targets within the same module.
- **Assign-to-passage annotations**: same inline comment on `assign` nodes where `expr` is a quoted passage name string.

These are for human readers and debugging; the engine ignores them.

---

## Extraction Pipeline

```
CradleExtractor
  ├── PrepareSource            detect complete vs partial class files; wrap partial files for Roslyn
  ├── Pass 1: DiscoverVariables    scan this.Vars.X accesses, infer types
  ├── Pass 2: BuildPassageRegistry passageN_Init() → (name, tags[]) map
  ├── Pass 3: ExtractPassageBodies passageN_Main() → MwsNode list
  │    └── PassageBodyVisitor   Roslyn statement visitor → extractor-internal nodes
  ├── StitchFragments          inline enchantHook fragment stubs into expand links
  ├── HoistAssignAndSwitchPlayerNames  reorder player-name setup sequences into canonical form
  ├── ConsolidateText          merge text runs, apply inline style markup, promote let nodes
  ├── AssignSeedKeys           DFS passage graph, assign stable "PassageId_N" seed keys
  └── V2Serializer             convert extractor-internal types → current MWS YAML dicts
       └── RestextCollector    extract strings to restext URIs while serializing
```

### Extractor-internal nodes → current MWS output

The extractor internally uses a set of **extractor-internal node types** (`MwsNodes.cs`, in the `Masterwork.Extractor` project) produced by `PassageBodyVisitor`. These are converted to the current MWS YAML format (v0.4) at serialization time by `V2Serializer.cs`, which runs after all passes complete. This design keeps the test suite (which checks intermediate node types) unaffected by format changes. (`V2Serializer`'s name is historical — from the v0.1→v0.2 transition — not tied to any particular current format version; it's evolved to emit whatever `mws-format-latest.md` currently specifies, v0.4 as of this writing.)

Key transformations performed by the serializer:

| Extractor-internal | Current MWS YAML output |
|---|---|
| `TextNode(template, style)` | `{type: text, value: **text**}` with inline markdown |
| `LinkNode` | `{type: navigation, label, target, state_affecting, onclick}` |
| `ExpandLinkNode` | `{type: popup, label, state_affecting, content}` |
| `InputPromptNode` | `{type: input, label, text, input, var, onsubmit}` |
| `SectionHeadingNode + SectionBodyNode` | `{type: section, title, content}` (merged pair) |
| `SetupBlockNode` | `{type: section, style: setup, content}` |
| `SetLocationNode` | hoisted to passage-level `location:` header |
| `CheckProgressNode` | hoisted to passage-level `check_progress:` header |
| `LetNode.Random` | `{type: let, var, expr: rand_between(...) or [...].shuffled(...)[0]}` |
| `EffectNode` | one `{type: assign, var, expr}` per variable affected |
| `ForeachNode` | `{type: foreach, var, in, do}` |

See `docs/mws-format-latest.md` for the full current node type reference.

---

## Restext i18n

All human-readable strings are extracted to `en-US.restext` and replaced with `restext://Key` references in the YAML. The extractor adds a comment above each field showing the string's line in the restext file and a preview of its value:

```yaml
# en-US.restext:12 | "**The Expedition Uncovers...**"
- type: text
  value: restext://Expedition3_001

# en-US.restext:15 | "Yes."
- type: navigation
  label: restext://Expedition3_004
  target: ExpYes
  state_affecting: true
```

The `en-US.restext` path shown is relative to `<passages-out-dir>` — it becomes something like `../en-US.restext:12 | "..."` when `--restext-out` puts the file in a different directory (e.g. a module's root, alongside `passages/`).

### Restext key format

Keys are `{PassageId}_{NNN}` — passage ID + underscore + 3-digit counter starting at 001, reset per passage. Keys are stable across re-extractions as long as the passage content and order are unchanged.

### Strings extracted

| Source | Field | Notes |
|---|---|---|
| `TextNode` | `Template` / runs | Skip pure single-`{var}` placeholders |
| `SectionHeadingNode` | `Text` | Section titles |
| `LinkNode` | `Label` | Navigation link labels |
| `ExpandLinkNode` | `Label` | Popup trigger labels |
| `InputPromptNode` | `Text` | User-visible prompt text |
| `SetupNotificationNode` | `Title`, `Text` | Two keys per node |
| `SetLocationNode` | `Name` | Location display name |

Expressions (in `let.expr` and `assign.expr`) are not extracted — they contain variable references and operator syntax, not user-visible text.

### Restext file format

```
# NNNnn-PassageName.mws.yaml
PassageId_001=Some human-readable string
PassageId_002=Another string with {varName} placeholder
```

One `Key=Value` per line; no multi-line values. Lines starting with `#` are comments.

---

## Sprite Mapping (Cost of Disease only)

The Cost of Disease uses TextMesh Pro inline sprite syntax (`<sprite="AtlasName" index=N>`) for icons. The `--sprite-map` JSON file maps sprite indices to named asset slugs from the Unity `ItemObtain` data. The extractor converts these to `{icon:slug}` inline syntax in the `value` field.

Unknown sprites fall back to a slugified form of the atlas name.

---

## Audio Mapping (`--audio-map`, all three scenarios)

Synthesizes an `audio_track` narration node for passages that have real voice-over,
mirroring `--sprite-map`/`--progress-map`'s "external JSON classifies passages by name" shape — but
unlike those two, it's consulted purely by passage name at serialization time
(`V2Serializer.ToDict`), not while walking the Cradle AST, since a synthesized node has no
corresponding source construct to parse in the first place. See `Masterwork.Extractor.AudioMapper`
and the `--audio-map` row above for the exact JSON shape and emitted node.

```json
{
  "Hospitalintro": {
    "slug": "hospitalintro",
    "title": "Thriving in Adversity"
  }
}
```

emits, prepended to `Hospitalintro`'s node list:

```yaml
- type: 'audio_track'
  asset: 'audio://vo/hospitalintro'
  title: 'restext://Hospitalintro_001'   # "Thriving in Adversity"
  autoplay: true
  bgm_behavior: 'pause'
```

The asset is always a plain slug, never an expression: which reading plays is the player's voice
track, chosen in the app (see `docs/mws-format-latest.md` §9 Voices). The audio files themselves are
the module's or a voice pack's responsibility, one per track at
`assets/audio/vo/<language>/<track>/<slug>.<ext>`. An entry in the old gendered shape
(`male`/`female` with no `slug`) is skipped with a warning.

---

## Special-Event On-Display SFX (automatic, no flag needed)

Unlike `--audio-map`, this isn't an opt-in option — the extractor unconditionally synthesizes a
passage-level `audio.on_display` override (`audio://sfx/special_event`) whenever a passage's node
list contains the special-event overlay marker (`type: 'text', style: 'special-event'`, itself
already synthesized whenever `PassageBodyVisitor.IsShowEventPopupCall` recognizes a
`ViewSpecialEvent.instance.ShowEventPopup()` call site — see `V2Serializer.HasSpecialEventMarker`).
No per-passage JSON map is needed the way VO needs one: every real occurrence plays the identical
fixed sound, so the trigger condition alone is enough. Matches the original app's own
`ViewSpecialEvent.clip`, fired via `SoundManager.Instance.OnOpenPopupPlayAudio(clip)` the instant the
overlay appears — a real gap the original `audio-inventory.md` asset survey missed (it mis-reported
`ViewSpecialEvent` as having no resolvable clip guid; see `audio-survey-new-sfx.md`'s correction).

```yaml
# synthesized passage header, whenever the marker is present anywhere in the passage:
audio:
  on_display: 'audio://sfx/special_event'
```

The referenced `audio://sfx/special_event` asset is template-canonical
(`my-fathers-work-template/assets/audio/sfx/special_event.ogg`, propagated to all three official
modules via `scripts/apply-template.ps1`) — same convention as `click`/`popup_open`/`popup_close`/
`reveal`.

---

## Module Overrides (`passages-override/`)

The extractor itself no longer has any override mechanism — it only ever writes to `<passages-out-dir>`, never touches hand-authored content, and every re-extraction is a clean, repeatable regeneration of that one folder. Hand-authored passages instead live directly in a module and are applied at **module load time**, not extraction time.

A module directory (all four current modules, e.g. `Masterwork-Modules/cost-of-disease/`) is laid out as:

```
<module>/
├── manifest.yaml         — declares passages/passages_override folder names (defaults below)
├── _variables.yaml       — extractor-owned; regenerated wholesale on every re-extraction
├── variables/            — optional, hand-authored; add-or-override-by-key on top of _variables.yaml
├── en-US.restext
├── passages/              — extractor-owned; overwritten wholesale by each re-extraction
├── passages-override/     — hand-maintained; never touched by extraction
└── layouts/               — hand-maintained layout chrome (docs/mws-format-latest.md §8)
```

`ModuleLoader.LoadFromDirectory` (in `Masterwork.ModuleFormat`) loads `passages/` first, then applies `passages-override/` on top: a `.mws.yaml` file there with a `passage_id` matching an extracted passage **replaces it entirely**; a `passage_id` not present in `passages/` is simply **added**. The folder names default to `passages`/`passages-override` but can be redirected per-module via optional `passages`/`passages_override` string fields in `manifest.yaml`. A module with no extraction step at all (`my-fathers-work-template`, fully hand-authored) simply has a flat `passages/` and no `passages-override/`/`.source/` — `LoadFromDirectory` accommodates that too.

Overrides must be in **current MWS format** (matching `docs/mws-format-latest.md`). When the format version advances, update all overrides before the next module load.

All three official scenarios now use the same `passages-override/` pattern (each has its own version of the same shape, not just Cost of Disease):
- A hand-implemented player-ranking passage (`WinnerHUB` in Cost of Disease) — the descending-sort-by-points logic the extractor can't derive automatically from the original LINQ code.
- `_Setup_01_PlayerCountSelect.mws.yaml` through `_Setup_09_Preparations.mws.yaml` — a hand-authored player/town onboarding flow that replaces the original app's pre-module-select setup screens, entirely new content with no extracted equivalent. Identical nine-file pattern across all three scenarios.
- `_Scoring_01` through `_Scoring_04` — a hand-authored end-of-game scoring flow, same pattern across all three.
- A handful of hand-authored `*-End{N}.mws.yaml` ending passages and a `Preparations`/`VarEndingsPassage` pair, specific to each scenario.

This is the concrete, shipped result of the app/module responsibility inversion (player onboarding and scoring are module content now, not app-shell UI).

---

## Isolated Passages

The extraction report lists passages with no inbound references from other passages. These may be:
- Intentional entry points (e.g. `TITLE_SCREEN`)
- Unreachable dead code or authoring notes
- Passages referenced from non-passage C# logic the extractor doesn't trace

Isolated passages are flagged but not excluded. Human review is needed to determine which are true entry points vs. dead code. See each module's own `_extraction-report.md` (in its `.source/` folder) for current per-module counts — figures previously quoted here predate all three scenarios being migrated into `Masterwork-Modules` and are no longer reproduced here to avoid going stale again.

---

## Reconcile (`reconcile` subcommand)

`reconcile` is the one-time import that gives an official scenario its starting French restext. It
pairs the official French Cradle script's strings with the module's live English keys and writes
`fr-CA.restext` plus a Markdown report. Afterwards each scenario's `fr-CA.restext` is maintained
directly. Re-running is a deliberate diff, not a sync.

```
dotnet run --project src/Masterwork.Extractor -- reconcile <english-extract-dir> <french-extract-dir> <module-dir> [options]
```

| Argument / option | Description |
|---|---|
| `<english-extract-dir>` | A fresh English extraction of the module's `.source/*.cs`, with the module's usual options. It reproduces the live `en-US.restext` exactly, so its structure carries the live keys. |
| `<french-extract-dir>` | The French script extracted with the same options. Its restext is still named `en-US.restext`. |
| `<module-dir>` | The live module. Its `en-US.restext` and `en-US.overrides.restext` are the target keys. Only the outputs below are written into it. |
| `--out <file>` | Restext output. Default: `<module-dir>/<locale>.restext`. |
| `--report <file>` | Report output. Default: `<module-dir>/.source/_reconcile-report.<locale>.md`. `.source/` is never packaged. |
| `--locale <culture>` | Default `fr-CA`. |
| `--title <title>` | Report heading. Default: the module folder name. |
| `--rename <fr>=<en>` | A further renamed passage. The five known Cradle French renames are always applied: `IntroMartial-Séparatistes`, `MartialPre2`, `FP10PV`, `FP10PVsignin`, `TCOD-NameEntryTownConfirm`. |

Write both extractions to scratch, never into the module. For example, for A Time of War:

```powershell
$modules = "<Masterwork-Modules>"
$french  = "<path to a local copy of the Unity project's Assets/Scripts/StoryScript/French/ATimeofWarFrench.cs>"
$scratch = "<scratch dir>"
$common  = @("--module-title", "A Time of War",
             "--common-restext", "$modules\a-time-of-war\.source\en-US.common.restext",
             "--progress-map", "$modules\progress-map.json", "--audio-map", "$modules\audio-map.json")

# Copy each source into scratch first: the extractor writes _extraction-report.md next to its input.
dotnet run --project src/Masterwork.Extractor -- "$scratch\src-en\ATimeofWar_Eng_v8.cs" "$scratch\en\passages" `
  --variables-out "$scratch\en" --restext-out "$scratch\en" @common
dotnet run --project src/Masterwork.Extractor -- "$scratch\src-fr\ATimeofWarFrench.cs" "$scratch\fr\passages" `
  --variables-out "$scratch\fr" --restext-out "$scratch\fr" @common
dotnet run --project src/Masterwork.Extractor -- reconcile "$scratch\en" "$scratch\fr" "$modules\a-time-of-war"
```

The Cost of Disease also needs its `--sprite-map` in both extractions.

### Method

1. **Passages** pair on `passage_id`, after the rename table.
2. **Keys** pair by structure inside each passage. Each passage is flattened to a sequence of tokens:
   one per run of text between logic nodes (split into lines at `break`s), and one per logic node,
   whose signature is its type plus identity fields (`target`, `var`, `asset`, `passage`, `layout`,
   `match`) with restext references masked. The two sequences are matched by longest common
   subsequence. A switch case's `match` value is part of its identity and scopes its body, so cases
   listed in a different order don't cross-pair.
3. Inside a matched text run, lines pair in order. A line whose English and French have the same
   number of fragments pairs key by key, and only those pairs can be official. A line with
   different fragment counts is a split: every key in it gets the line's whole French as a draft.
   Restext fields on matched logic nodes (`label`, `okay`, `title`, `if`, `match`, …) pair
   field by field when the counts agree, and are a split when they don't.
4. **Mechanical fixes** keep a string `official`. A space the French dropped after a placeholder or
   closing marker (`{nameC}n'a`, `**15**des`, `_…_au`) is restored wherever the English has one. A shared key
   whose uses differ only in whitespace or capitalization takes the majority, and a tie goes to the
   variant capitalized like the English. A use the script left in English isn't counted as a variant.
5. **Dev-note passages** are left out of the output and the coverage: ids containing `note` or
   `example`, the variable lists (`VariableList-ATOW`, `Variable List FnP`, …), `Extra Code`,
   `DECREESLIST`, `OUTDATEDFirstPara` and `FixIt`, and any passage tagged `NOTE`. None is reachable
   in play. A key is left out only when every passage using it is a dev note.

### Buckets

| Bucket | Written as | When |
|---|---|---|
| Official | `# origin: official` | One French string, no flags. |
| Official after a mechanical fix | `# origin: official` | As above, after step 4. |
| Draft: shared key worded differently | `# origin: draft` | A shared key's uses carry different French wording. The majority is written; the agent writes one string that fits every use. |
| Draft: split or mismatch | `# origin: draft` | A split, a placeholder mismatch, a French/English length ratio outside 0.7–2.0 when the English is over 20 characters, both measured without placeholders (what the translation checks flag), or a space still missing after a placeholder or closing marker. |
| No French: English left in the French script | not written | The French prose is the English's unchanged (placeholders ignored) and has a word of three or more letters. A French cognate such as *duo* lands here too, and the agent writes it. |
| No French: unmatched | not written | No counterpart, or the passage has no French. |
| No French: no Cradle source | not written | Keys only in `en-US.overrides.restext`. |

The placeholder, length-ratio, missing-space and English-left rules, and the space restoring, come
from `TranslationComparison` in `Masterwork.Translation`. `checktranslation` uses the same code, so the
two always agree.

Keys with no French are listed in the report and never written in English, so the app falls back
to `en-US` for them. The origin vocabulary is in `docs/mws-format-latest.md` (§3, "String origin").
The agent pass turns every `draft` into `official, revised`. `Masterwork.ModulePacker checktranslation`
runs the deterministic checks on the result (`docs/translation-checks.md`).

The report gives the bucket counts, then every key that isn't plain official (English, French draft
or candidates, reason, passages), then every French-only text (in matched passages, and whole
French-only passages) with its passage, the English on either side and the French. French-only
text is dropped, never made into keys.

### Documented run: A Time of War

Run on 2026-11-24 against the live module. It reproduces the prototype from
[#22](https://github.com/GameGhost/Masterwork/issues/22) within noise:

| Bucket | Prototype | `reconcile` |
|---|---:|---:|
| Official | 1216 | 1249 |
| Official after a mechanical fix | 179 | 116 |
| Draft: shared key worded differently | (counted as a fix) | 47 |
| Draft: split or mismatch | 125 | 123 |
| No French: English left in the French script | 47 | 36 |
| No French: unmatched | 77 | 73 |
| No French: no Cradle source | 49 | 49 |
| **Total** (211 dev-note keys excluded) | **1693** | **1693** |

The differences are deliberate:

- The prototype took the majority for every shared-key conflict. `reconcile` does that only for
  whitespace and capitalization, and sends wording conflicts to the agent: 116 + 47 = 163 against 179.
- A use the script left in English no longer counts as a variant (`Choice_None`: *aucun* in most
  uses, *none* in one), so those keys are plain official.
- Placeholder-only strings that are identical in both languages (`{nameB}.`) are official, not
  English left: 11 keys.
- Case bodies are scoped to their `match` value. This removes cross-paired cases such as
  `PeacetoRebuild_005`, where the prototype paired "zero buildings were sabotaged" with French about
  one destroyed building, at the cost of a few keys left unmatched.
- A restext field whose key count differs between the languages (a ternary `label` against a plain
  one) is a draft. The prototype left those keys unmatched.
- The length ratio that makes a draft is 0.7–2.0 for English over 20 characters, as the translation checks flag. The
  prototype used 0.8–2.0.

Fear of the Unknown (2029 keys, 228 dev-note keys excluded) and The Cost of Disease (1969 keys, 243
excluded) come out at 74.9% and 69.2% official, with 3.7% and 2.9% unmatched.

**Known limit:** where the French restructured a passage, for example by moving a popup inside each
switch case, a text run can still pair with the wrong run (A Time of War `SeedResolution_010`–`012`).
This is why the agent pass verifies every official string too.

---

## Current Extraction Results

All three official scenarios are extracted and shipped (Masterwork-Modules v0.2.1 and earlier releases):

| Module | Source | Passages | Variables | Warnings | Unknowns |
|---|---|---|---|---|---|
| The Cost of Disease | `TheCostofDisease_Eng_v10.cs` | 361 | 260 | 70 | 14 |
| A Time of War | `ATimeofWar_Eng_v8.cs` | 297 | 187 | 365 | 0 |
| Fear of the Unknown | `FearoftheUnknown_Eng_v15.cs` | 378 | 253 | 359 | 0 |

Passage files carry the format version the extractor was built with (`MwsFormatVersion.Current`, now `mws/0.6`); files extracted before v0.6 still declare `mws/0.4` until re-extracted or updated. Warnings are predominantly conflicting-type assignments (a variable used as more than one type across the original ~30k-line source — genuine mixed-use patterns, resolved to `type: string`), not extraction errors; see each module's own `_extraction-report.md` for the exact breakdown, especially Cost of Disease's 14 unknown-node warnings, which are worth a closer look if revisiting that module's content.

See each module's own `_extraction-report.md` (next to its `.source/*.cs` file) for exact, current per-module figures — this table is a snapshot and will drift if quoted without checking back against that file.
