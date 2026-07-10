# MasterWork Extractor

`MasterWork.Extractor` is the CLI tool that converts Cradle C# scenario files into MWS v0.3 YAML passages, ready for the game engine.

---

## What It Does

The three original *My Father's Work* scenarios were built with **Cradle 2.0**, a Unity plugin that transpiles Twine/Harlowe `.html` story files into C# coroutine code. Each scenario is a single large `.cs` file (~30,000–35,000 lines, ~300–380 passages).

The extractor:
1. Parses the C# source with Roslyn (no regex — full AST)
2. Converts each passage into MWS v0.3 YAML nodes
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
| `--variables-out <dir>` | Where `_variables.yaml` is written. Defaults to `<passages-out-dir>`. |
| `--restext-out <dir>` | Where `en-US.restext` is written. Defaults to `<passages-out-dir>`. |
| `--include-debug` | Include passages gated behind the `devpage` debug flag. Excluded by default. |
| `--dry-run` | Parse and report without writing any output files. |
| `--seed-analysis` | Emit a seed key dependency report alongside the extraction output. |

The extractor no longer accepts hand-authored overrides — see [Module Overrides](#module-overrides-passages-override) below.

---

## Extraction Commands for the Three Scenarios

Run from the `c:\Projects\Masterwork` directory. See `Masterwork-Design/CLAUDE.md` for the
authoritative, up-to-date version of these commands.

```powershell
$base      = "c:\Projects\Masterwork-Design\Reference\ScriptsComplete"
$spritemap = "c:\Projects\Masterwork-Design\Reference\my-fathers-work-master-4\Assets\Resources\TheCostOfDisease_ItemObtain.json"
$modules   = "c:\Projects\Masterwork-Design\Modules"

# Fear of the Unknown (still flat output — not yet moved into Modules/)
dotnet run --project src/Masterwork.Extractor -- `
  "$base\FearoftheUnknown_Eng_v15.cs" `
  "$base\fear-of-the-unknown"

# A Time of War (still flat output — not yet moved into Modules/)
dotnet run --project src/Masterwork.Extractor -- `
  "$base\ATimeofWar_Eng_v8.cs" `
  "$base\a-time-of-war" `
  --module-title "A Time of War"

# The Cost of Disease — passages go into the module's passages/ subfolder; _variables.yaml and
# en-US.restext go into the module root, next to manifest.yaml and passages-override/
dotnet run --project src/Masterwork.Extractor -- `
  "$base\TheCostofDisease_Eng_v10.cs" `
  "$modules\cost-of-disease\passages" `
  --variables-out "$modules\cost-of-disease" `
  --restext-out "$modules\cost-of-disease" `
  --module-title "The Cost of Disease" `
  --sprite-map $spritemap
```

> **Note:** `--module-title` is required for A Time of War (auto-generation capitalises "Of") and The Cost of Disease (auto-generation produces "The Costof Disease").

> **Note:** Re-running extraction only touches `passages/` — it never writes to `passages-override/`,
> so hand-authored passages there always survive a re-extraction.

---

## Output Files

For each run, the output directory contains:

| File | Description |
|---|---|
| `{NNN}-{PassageId}.mws.yaml` | One file per passage in MWS v0.3 format, numbered by source order |
| `_variables.yaml` | All discovered session variables with inferred types |
| `en-US.restext` | All extracted human-readable strings, one `Key=Value` per line |
| `_extraction-report.md` | Summary table, warnings, unknown nodes, isolated passages, input prompts |

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
  └── V2Serializer             convert extractor-internal types → v0.3 YAML dicts
       └── RestextCollector    extract strings to restext URIs while serializing
```

### Extractor-internal nodes → v0.3 output

The extractor internally uses a set of **extractor-internal node types** (`MwsNodes.cs`, in the `Masterwork.Extractor` project) produced by `PassageBodyVisitor`. These are converted to v0.3 YAML at serialization time by `V2Serializer.cs`, which runs after all passes complete. This design keeps the test suite (which checks intermediate node types) unaffected by format changes.

Key transformations performed by the serializer:

| Extractor-internal | v0.3 YAML output |
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

See `docs/mws-format-latest.md` for the full v0.3 node type reference.

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

## Module Overrides (`passages-override/`)

The extractor itself no longer has any override mechanism — it only ever writes to `<passages-out-dir>`, never touches hand-authored content, and every re-extraction is a clean, repeatable regeneration of that one folder. Hand-authored passages instead live directly in a module and are applied at **module load time**, not extraction time.

A module directory (e.g. `Masterwork-Design/Modules/cost-of-disease/`) is laid out as:

```
<module>/
├── manifest.yaml         — declares passages/passages_override folder names (defaults below)
├── _variables.yaml
├── en-US.restext
├── passages/              — extractor-owned; overwritten wholesale by each re-extraction
└── passages-override/     — hand-maintained; never touched by extraction
```

`ModuleLoader.LoadFromDirectory` (in `Masterwork.ModuleFormat`) loads `passages/` first, then applies `passages-override/` on top: a `.mws.yaml` file there with a `passage_id` matching an extracted passage **replaces it entirely**; a `passage_id` not present in `passages/` is simply **added**. The folder names default to `passages`/`passages-override` but can be redirected per-module via optional `passages`/`passages_override` string fields in `manifest.yaml`. Older extractor output with no `passages/` subfolder at all (e.g. the still-flat `fear-of-the-unknown`/`a-time-of-war` directories) loads unchanged — `LoadFromDirectory` falls back to reading passages directly from the module root when no `passages/` subfolder exists.

Overrides must be in **current MWS format** (matching `docs/mws-format-latest.md`). When the format version advances, update all overrides before the next module load.

The Cost of Disease's `passages-override/` currently contains:
- `00327-WinnerHUB.mws.yaml` — hand-implements the player ranking algorithm (descending sort by points using the `NamePoints` custom type) that the extractor cannot derive automatically from the original LINQ code.
- `_Setup_01_PlayerCountSelect.mws.yaml` through `_Setup_09_Preparations.mws.yaml` — a hand-authored player/town onboarding flow that replaces the original app's pre-module-select setup screens, entirely new content with no extracted equivalent.

---

## Isolated Passages

The extraction report lists passages with no inbound references from other passages. These may be:
- Intentional entry points (e.g. `TITLE_SCREEN`)
- Unreachable dead code or authoring notes
- Passages referenced from non-passage C# logic the extractor doesn't trace

Isolated passages are flagged but not excluded. Human review is needed to determine which are true entry points vs. dead code. Counts per module: FotU 80, AToW 62, CoD 84.

---

## Current Extraction Results (as of 2026-07-02)

| Module | Source | Passages | Variables | Strings | Warnings | Unknowns |
|---|---|---|---|---|---|---|
| Fear of the Unknown | `FearoftheUnknown_Eng_v15.cs` | 378 | 254 | 2157 | 46 | 0 |
| A Time of War | `ATimeofWar_Eng_v8.cs` | 297 | 188 | 1826 | 44 | 0 |
| The Cost of Disease | `TheCostofDisease_Eng_v10.cs` | 361 | 261 | 2144 | 60 | 0 |

All 1036 passage files carry `format: mws/0.3`. Warnings are all conflicting-type assignments (genuine mixed-use patterns; resolved to `type: string`) — not extraction errors.
