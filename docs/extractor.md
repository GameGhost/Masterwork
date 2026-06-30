# MasterWork Extractor

`MasterWork.Extractor` is the CLI tool that converts Cradle C# scenario files into MWS v0.2 YAML passages, ready for the game engine.

---

## What It Does

The three original *My Father's Work* scenarios were built with **Cradle 2.0**, a Unity plugin that transpiles Twine/Harlowe `.html` story files into C# coroutine code. Each scenario is a single large `.cs` file (~30,000–35,000 lines, ~300–380 passages).

The extractor:
1. Parses the C# source with Roslyn (no regex — full AST)
2. Converts each passage into MWS v0.2 YAML nodes
3. Extracts all human-readable strings into an `en-US.restext` locale file (replacing them with `restext://Key` references in the YAML)
4. Assigns deterministic PRNG seed keys to all random calls
5. Writes one `.mws.yaml` file per passage, plus a variables manifest and extraction report

---

## Usage

```
dotnet run --project src/Masterwork.Extractor -- <input> <output-dir> [options]
```

### Required Arguments

| Argument | Description |
|---|---|
| `<input>` | Path to a `.cs` source file, or a directory containing `.cs` files |
| `<output-dir>` | Directory to write extracted files into (created if it does not exist) |

### Options

| Option | Description |
|---|---|
| `--module-title <title>` | Human-readable module title (used in the extraction report header). If omitted, derived from the source filename by splitting on capital letters. |
| `--module-id <id>` | Module identifier string (reserved for future use in the manifest). |
| `--sprite-map <json>` | Path to a `TheCostOfDisease_ItemObtain.json`-style file mapping sprite indices to asset slugs. Required for The Cost of Disease; not needed for the other scenarios. |
| `--overrides <dir>` | Directory of hand-authored `.mws.yaml` files that replace auto-generated passages. Each override must have the same `passage_id` as the generated file it replaces, identified by filename prefix. |
| `--include-debug` | Include passages gated behind the `devpage` debug flag. Excluded by default. |
| `--dry-run` | Parse and report without writing any output files. |
| `--seed-analysis` | Emit a seed key dependency report alongside the extraction output. |

---

## Extraction Commands for the Three Scenarios

Run from the `c:\Projects\Masterwork` directory.

```powershell
$base      = "c:\Projects\Masterwork-Design\Reference\ScriptsComplete"
$spritemap = "c:\Projects\Masterwork-Design\Reference\my-fathers-work-master-4\Assets\Resources\TheCostOfDisease_ItemObtain.json"
$overrides = "c:\Projects\Masterwork-Design\Reference\ScriptsPartial\cost-of-disease-overrides"

# Fear of the Unknown
dotnet run --project src/Masterwork.Extractor -- `
  "$base\FearoftheUnknown_Eng_v15.cs" `
  "$base\fear-of-the-unknown"

# A Time of War
dotnet run --project src/Masterwork.Extractor -- `
  "$base\ATimeofWar_Eng_v8.cs" `
  "$base\a-time-of-war" `
  --module-title "A Time of War"

# The Cost of Disease
dotnet run --project src/Masterwork.Extractor -- `
  "$base\TheCostofDisease_Eng_v10.cs" `
  "$base\cost-of-disease" `
  --module-title "The Cost of Disease" `
  --sprite-map $spritemap `
  --overrides $overrides
```

> **Note:** `--module-title` is required for A Time of War (auto-generation capitalises "Of") and The Cost of Disease (auto-generation produces "The Costof Disease").

---

## Output Files

For each run, the output directory contains:

| File | Description |
|---|---|
| `{NNN}-{PassageId}.mws.yaml` | One file per passage in MWS v0.2 format, numbered by source order |
| `_variables.yaml` | All discovered session variables with inferred types and defaults |
| `en-US.restext` | All extracted human-readable strings, one `Key=Value` per line |
| `_extraction-report.md` | Summary table, warnings, unknown nodes, isolated passages, input prompts |

### Passage filenames

Files are named `{index:D5}-{SanitizedPassageId}.mws.yaml` — for example `00255-Expedition3.mws.yaml`. The five-digit prefix preserves source order when listed alphabetically.

### Source line annotations

Every `.mws.yaml` file contains YAML comments for source navigation:

- **Passage header**: `# SourceFile.cs:line` above the `---` marker — points to the passage's main method
- **Node comments**: `# ../SourceFile.cs:line` above every node at every nesting depth (top-level and inside conditional branches, switch cases, sections, foreach loops, popups). Use the `click-file` VS Code extension to make these relative-path links navigable.
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
  │    └── PassageBodyVisitor   Roslyn statement visitor → v0.1 intermediate nodes
  ├── StitchFragments          inline enchantHook fragment stubs into expand links
  ├── ConsolidateText          merge text runs, apply inline style markup, promote let nodes
  ├── AssignSeedKeys           DFS passage graph, assign stable "PassageId_N" seed keys
  └── V2Serializer             convert v0.1 intermediate types → v0.2 YAML dicts
       └── RestextCollector    extract strings to restext URIs while serializing
```

### V0.1 intermediate → V0.2 output

The extractor internally uses **v0.1 intermediate node types** (`MwsNodes.cs`) produced by `PassageBodyVisitor`. These are converted to v0.2 YAML at serialization time by `V2Serializer.cs`, which runs after all passes complete. This design keeps the test suite (which checks intermediate node types) unaffected by format changes.

Key v0.1 → v0.2 transformations performed by the serializer:

| V0.1 intermediate | V0.2 YAML output |
|---|---|
| `TextNode(template, style)` | `{type: text, value: **text**}` with inline markdown |
| `LinkNode` | `{type: navigation, label, target, state_affecting}` |
| `ExpandLinkNode` | `{type: popup, label, state_affecting, nodes}` |
| `InputPromptNode` | `{type: input, label, text, input_type, store_in, onsubmit}` |
| `SectionHeadingNode + SectionBodyNode` | `{type: section, title, nodes}` (merged pair) |
| `SetupBlockNode` | `{type: section, style: setup, nodes}` |
| `SetLocationNode` | hoisted to passage-level `location:` header |
| `CheckProgressNode` | hoisted to passage-level `check_progress:` header |
| `LetNode.Random` | `{type: let, var, expr: rand_between(...) or [...].shuffled(...)[0]}` |
| `EffectNode` | one `{type: assign, var, expr}` per variable affected |

See `docs/mws-format-latest.md` for the full v0.2 node type reference.

---

## Restext i18n

All human-readable strings are extracted to `en-US.restext` and replaced with `restext://Key` references in the YAML. The extractor adds an inline comment showing a preview of the original string:

```yaml
- type: text
  value: restext://Expedition3_001 # "**The Expedition Uncovers...**"

- type: navigation
  label: restext://Expedition3_004 # "Yes."
  target: ExpYes
  state_affecting: true
```

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
PassageId_003="""
Multi-line value — opening """ followed by newline.
Closing """ must appear on its own line.
"""
```

---

## Sprite Mapping (Cost of Disease only)

The Cost of Disease uses TextMesh Pro inline sprite syntax (`<sprite="AtlasName" index=N>`) for icons. The `--sprite-map` JSON file maps sprite indices to named asset slugs from the Unity `ItemObtain` data. The extractor converts these to `{icon:slug}` inline syntax in the `value` field.

Unknown sprites fall back to a slugified form of the atlas name.

---

## Override System

The `--overrides <dir>` option allows hand-authored `.mws.yaml` files to replace auto-generated passages. Each override file is matched to its generated counterpart by filename prefix (e.g. `00327-WinnerHUB.mws.yaml`). The extractor validates that the `passage_id` fields match before applying the override.

Overrides must be in **current MWS format** (matching `docs/mws-format-latest.md`). When the format version advances, update all overrides before re-running extraction.

The only current override is `cost-of-disease-overrides/00327-WinnerHUB.mws.yaml`, which hand-implements the player ranking algorithm (descending sort by points using the `NamePoints` custom type) that the extractor cannot derive automatically from the original LINQ code.

---

## Isolated Passages

The extraction report lists passages with no inbound references from other passages. These may be:
- Intentional entry points (e.g. `TITLE_SCREEN`)
- Unreachable dead code or authoring notes
- Passages referenced from non-passage C# logic the extractor doesn't trace

Isolated passages are flagged but not excluded. Human review is needed to determine which are true entry points vs. dead code. Counts per module: FotU 94, AToW 73, CoD 101.

---

## Current Extraction Results (as of 2026-06-29)

| Module | Source | Passages | Variables | Strings | Warnings | Unknowns |
|---|---|---|---|---|---|---|
| Fear of the Unknown | `FearoftheUnknown_Eng_v15.cs` | 378 | 254 | 2698 | 0 | 0 |
| A Time of War | `ATimeofWar_Eng_v8.cs` | 297 | 188 | 2093 | 0 | 0 |
| The Cost of Disease | `TheCostofDisease_Eng_v10.cs` | 359 | 261 | 2732 | 0 | 0 |

All 1034 passage files carry `format: mws/0.2`.
