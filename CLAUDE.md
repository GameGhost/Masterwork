# Masterwork — Claude Instructions

## Project Overview

Community companion app for the boardgame *My Father's Work* (Renegade Game Studios). Converts official scenario scripts to an open format and plays them. C# .NET 10 solution.

Design documents and design decisions live in the companion repo at `c:\Projects\Masterwork-Design\Design\`. Reference assets (CC BY-NC-SA) live there too.

---

## Solution Structure

Solution file: `src/Masterwork.slnx`

```
src/Masterwork.slnx
├── src/MasterWork.Engine/          pure C# — interpreter and game session (Phase 1)
├── src/MasterWork.ModuleFormat/    node types, YAML schema, VarDef (shared library)
├── src/MasterWork.App/             MAUI Blazor Hybrid player app (Phase 2 placeholder)
├── src/MasterWork.Editor/          WPF module designer (Phase 5 placeholder)
├── src/MasterWork.Extractor/       CLI tool: Cradle C# → MWS v0.2 YAML
└── src/MasterWork.Tests/           xUnit test suite
```

## Build and Test

```powershell
dotnet build src/Masterwork.slnx
dotnet test src/Masterwork.Tests/Masterwork.Tests.csproj
```

All 37 tests must pass after any change to `ModuleFormat` or `Extractor`.

---

## Key Architecture Decisions

### V2Serializer (do not bypass)
`src/Masterwork.Extractor/V2Serializer.cs` converts the v0.1 intermediate `MwsNode` types (produced by `PassageBodyVisitor`) into v0.2 YAML dicts at serialization time. The intermediate types (`MwsNodes.cs`) are **kept unchanged** — this lets existing tests stay green while the emitted YAML is v0.2. When the MWS format changes again, extend the serializer; do not mutate the node types.

### RestextCollector
Walks the v0.2 dict produced by `V2Serializer.ToDict()` and replaces human-readable strings with `restext://Key` URIs, accumulating entries for the `en-US.restext` file. It reads v0.2 field names (`value`, `navigation`, `popup`, `input`, `section`).

### Test strategy
Tests in `ExtractorTests.cs` check the v0.1 intermediate node types directly (e.g. `textNode.Template`, `linkNode.Target`). The v0.2 format is an output concern — do not add v0.2 assertions there unless specifically testing the serializer. Add a separate `V2SerializerTests.cs` if serializer tests are needed.

---

## MWS Format Documentation

| File | Purpose |
|---|---|
| `docs/mws-format-latest.md` | **Authoritative current spec** — edit this for changes |
| `docs/mws-format-v0.1.md` | Frozen v0.1 reference (intermediate extraction format) |

**Versioning protocol for format docs:**
- **Minor / QoL changes** (clarifications, new examples, field descriptions) → edit `mws-format-latest.md` inline.
- **Major revisions** (new node types, breaking field renames, structural changes) → first copy `mws-format-latest.md` to `mws-format-vN.md` to freeze the current version, then make the v(N+1) changes to `latest`. Example: before introducing v0.3, copy `latest` as `mws-format-v0.2.md`, then revise `latest` for v0.3.

---

## Security / Licensing Constraints

- **Never commit** content from `c:\Projects\Masterwork-Design\Reference\` to this repo. That directory contains CC BY-NC-SA 4.0 derived content from the official app.
- `_extracted/` output directories are also off-limits for commits here.
- The Masterwork source code itself (this repo) is a separate work from the reference content.

---

## Other Docs to Keep Current

Keep these files updated as the project evolves:
- `docs/mws-format-latest.md` — format spec (see versioning protocol above)
- `docs/extractor.md` — extractor usage and pipeline description
- `CLAUDE.md` — this file; update when architecture decisions change
