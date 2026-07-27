# Masterwork — Claude Instructions

## Project Overview

Community companion app for the boardgame *My Father's Work* (Renegade Game Studios). Converts official scenario scripts to an open format and plays them. C# .NET 10 solution.

Design documents and design decisions live in the companion repo at `c:\Projects\Masterwork-Design\Design\`. Reference assets (CC BY-NC-SA) live there too.

---

## Solution Structure

Solution file: `src/Masterwork.slnx`

```
src/Masterwork.slnx
├── src/MasterWork.Engine/          pure C# — interpreter and game session (Phase 1, implemented)
├── src/MasterWork.ModuleFormat/    VarDef, v0.3 reader types + YAML loader (shared library)
├── src/MasterWork.App.Shared/      Razor Class Library — the actual player-app UI (Phase 2, in progress); shared by both heads below
├── src/MasterWork.App/             MAUI Blazor Hybrid head — hosts Shared via BlazorWebView; Windows and Android buildable now, iOS/MacCatalyst deferred (needs a Mac build host)
├── src/MasterWork.App.Web/         ASP.NET Core host for the web deliverable — serves Web.Client's WebAssembly payload
├── src/MasterWork.App.Web.Client/  Blazor WebAssembly client — hosts Shared in the browser; this + App.Web together are "the web app"
├── src/MasterWork.Editor/          WPF module designer (Phase 5 placeholder)
├── src/MasterWork.Extractor/       CLI tool: Cradle C# → MWS v0.3 YAML (also owns extractor-internal node types, MwsNodes.cs)
└── src/MasterWork.Tests/           xUnit test suite
```

All nine projects are registered in `Masterwork.slnx`.

## Build and Test

```powershell
dotnet build src/Masterwork.slnx
dotnet test src/Masterwork.Tests/Masterwork.Tests.csproj
```

All 215 tests must pass after any change to `ModuleFormat`, `Engine`, or `Extractor`.

### Android build setup

Building `Masterwork.App`'s `net10.0-android` target needs a local JDK + Android SDK. Copy `Directory.Build.local.props.example` to `Directory.Build.local.props` (gitignored) and fill in your paths:

```xml
<JavaSdkDirectory>C:\Path\To\Your\jdk</JavaSdkDirectory>
<AndroidSdkDirectory>C:\Path\To\Your\android-sdk</AndroidSdkDirectory>
```

Building the Android target adds noticeably to build time (~3 min vs ~30s for the rest of the solution). Pass `-p:BuildAndroid=false` to skip it for fast iteration when only working on Engine/ModuleFormat/Web:

```powershell
dotnet build src/Masterwork.slnx -p:BuildAndroid=false
```

### Logging

`FileLoggerProvider` (`Masterwork.App.Shared/Services/FileLoggerProvider.cs`) writes a rolling daily
text log — no external logging package, always on (not DEBUG-only), since it's what would have
diagnosed a crash a user hits before they can describe it. Registered on the two hosts that have a
real filesystem:

| Host | Log location |
|---|---|
| `Masterwork.App` (MAUI — Windows/Android) | `{FileSystem.AppDataDirectory}/logs/masterwork-{yyyy-MM-dd}.log` |
| `Masterwork.App.Web` (ASP.NET Core host) | `{ContentRootPath}/logs/masterwork-{yyyy-MM-dd}.log` (next to the project when run via `dotnet run`) |
| `Masterwork.App.Web.Client` (WASM, runs in the browser) | **No file log** — no filesystem in the browser sandbox. The browser devtools console is the log; unhandled errors also show the `blazor-error-ui` banner. |

---

## Key Architecture Decisions

### V2Serializer (do not bypass)
`src/Masterwork.Extractor/V2Serializer.cs` converts the extractor-internal `MwsNode` types (in `MwsNodes.cs`, produced by `PassageBodyVisitor`) into v0.3 YAML dicts at serialization time. The intermediate types are **kept unchanged** — this lets existing tests stay green while the emitted YAML is v0.3. When the MWS format changes again, extend the serializer; do not mutate the node types. Both `MwsNodes.cs` and `V2Serializer.cs` live in the `Masterwork.Extractor` project — `ModuleFormat` holds only `VarDef` and the v0.3 reader types consumed by the engine.

### RestextCollector
Walks the v0.3 dict produced by `V2Serializer.ToDict()` and replaces human-readable strings with `restext://Key` URIs, accumulating entries for the `en-US.restext` file. It reads v0.3 field names (`value`, `navigation`, `popup`, `input`, `section`). Restext values are single-line only — no multi-line block syntax.

### Test strategy
Tests in `ExtractorTests.cs` check the extractor-internal node types directly (e.g. `textNode.Template`, `linkNode.Target`). The v0.3 format is an output concern — do not add v0.3 assertions there unless specifically testing the serializer. Add a separate `V2SerializerTests.cs` if serializer tests are needed.

### Engine (Phase 1)
- `ModuleFormat/PassageYamlParser.cs` parses `.mws.yaml` by hand against YamlDotNet's low-level representation model (`YamlMappingNode` etc.), dispatching on `type:` — not a generic object-graph deserializer, since polymorphic node dispatch is exactly what that API is for.
- `ModuleFormat/RestextResolver.cs` runs once at load time, before any expression is parsed. It has two resolution modes: display fields (substituted as-is) and expression fields (substituted with embedded `"` escaped, since the value lands inside a string literal).
- `Engine/SessionPrng.cs` derives each seed_key's draw from `(masterSeed, key, occurrence#)` rather than mutating one shared `Random` per key — this is what makes timeline rewind exact (restoring position is just restoring an integer occurrence count, no need to replay RNG draw history).
- `Engine/GameSession.cs`: every `SessionSnapshot` captures state from **before** the passage it points to renders. `StepBack`/`StepForward` restore that state and **re-render** the passage (not just redisplay a cached result) — this keeps the live `VariableStore` consistent with what's shown. The one exception is `SnapshotKind.Checkpoint` entries, which capture mid-passage state and can't be reproduced by a fresh top-down render without double-applying earlier assigns; those replay from a cached render instead. See the doc comments on `SessionSnapshot` and `GameSession.RestoreAndRerenderCurrent`.
- A non-state-affecting `link`/`popup`/`goto` (`snapshot: false`, or inherited from the enclosing action — see `goto`'s own `snapshot` override in `docs/mws-format-latest.md` §6) navigates via `RenderInPlace`, which does **not** add a timeline entry. The live edge tracks at most one such divergence as `GameSession._activeState` (`Session/ActiveState.cs`) so it isn't silently lost: `StepBack`'s first press shows the entry's own anchor render instead of consuming a real timeline entry, and survives **any** amount of further stepping back into real history (it is *not* discarded by `StepBack` itself); `StepForward`/`JumpToPresent` restore it directly rather than replaying whatever produced it. Only `ResumeFromHere` (branching play from a historical point) or a new state-affecting navigation (which supersedes it with a real snapshot) discard it. Persisted through `SessionSave.ActiveState` so resuming a save taken mid-chain doesn't lose it either.
- Popup content (`popup.content`) is rendered eagerly by `PassageRenderer`, alongside the rest of the passage, against a sandboxed `VariableStore` clone (`RenderedPopup.Sandbox`) — never the live store. This means opening/closing a popup's *display* is a pure UI state toggle with no engine call involved; only `ClosePopupAsync` (Accept) touches the engine, committing the sandbox to the live store and running `onclose` + navigation as a single transaction. This deliberately trades away one thing: an unopened/never-accepted popup's content still gets evaluated (so a seeded random draw inside popup content is "spent" even if the player never opens it) — acceptable since nothing else can mutate the live store while a popup sits unopened on an already-rendered passage.

### Mobile safe-area / system-bar insets
Android renders edge-to-edge by default (enforced on API 35+), which stretched `BlazorWebView` under
the status bar and behind the gesture/button navigation bar. Fixed in
`Masterwork.App/Platforms/Android/MainActivity.cs`: `OnCreate` calls
`WindowCompat.SetDecorFitsSystemWindows(Window, false)` (explicit, so behavior is identical on older
OS versions too) and installs a `ViewCompat.SetOnApplyWindowInsetsListener` on the Activity's content
view that pads it by `WindowInsetsCompat.Type.SystemBars()` insets on all four edges. This deliberately
shrinks the actual native view hosting `BlazorWebView` rather than padding around it in CSS — so the
WebView's own laid-out size already excludes the status bar/nav bar in both orientations, meaning
`vh`/`vw` inside any module's Blazor content measure the true visible area with **no per-template
changes needed**. Keep any future mobile-layout work on this side of the fence (native container size)
rather than reaching for CSS safe-area padding, or `vh`/`vw`-based templates will need per-page fixes.

**iOS/MacCatalyst (not yet implemented — deferred along with the rest of the iOS target, see Solution
Structure above; needs a Mac build host to write and verify):** first check whether MAUI's default
`Page.On<iOS>().SetUseSafeArea(true)` behavior already keeps `BlazorWebView` inside the safe area —
it may just work, unlike Android. If it doesn't (known history of `BlazorWebView`'s internal
`WKWebView` ignoring the page's safe area and extending under the notch/Dynamic Island/home
indicator), the analogous fix is native, not CSS, mirroring the Android approach above: in
`Platforms/iOS/AppDelegate.cs` or a `BlazorWebViewHandler` mapper registered in `MauiProgram.cs`, read
the hosting `UIViewController`'s `View.SafeAreaInsets` (or set `AdditionalSafeAreaInsets`) and
constrain/inset the native `WKWebView`'s frame accordingly, re-applied on `ViewSafeAreaInsetsDidChange`
so rotation (notch/Dynamic Island top in portrait, safe-area sides in landscape, home indicator
bottom) is handled the same way `WindowInsetsCompat` handles it on Android.

---

## MWS Format Documentation

| File | Purpose |
|---|---|
| `docs/mws-format-latest.md` | **Authoritative current spec** — currently v0.3; edit this for changes |

Frozen prior-version references (`mws-format-v0.1.md`, `mws-format-v0.2.md`) were removed once the project committed to v0.3 as the baseline going forward. Future major revisions resume the freeze protocol below.

**Versioning protocol for format docs:**
- **Minor / QoL changes** (clarifications, new examples, field descriptions) → edit `mws-format-latest.md` inline.
- **Major revisions** (new node types, breaking field renames, structural changes) → first copy `mws-format-latest.md` to `mws-format-vN.md` to freeze the current version, then make the v(N+1) changes to `latest`. Example: before introducing v0.4, copy `latest` as `mws-format-v0.3.md`, then revise `latest` for v0.4.

---

## Security / Licensing Constraints

- **Never commit** content from `c:\Projects\Masterwork-Design\Reference\` to this repo. That directory contains CC BY-NC-SA 4.0 derived content from the official app.
- `_extracted/` output directories are also off-limits for commits here.
- The Masterwork source code itself (this repo) is a separate work from the reference content.
- **Exception**: `src/Masterwork.App.Theme.MyFathersWork/` is a documented, deliberate exception to
  the rule above. Its assets come from Renegade Game Studios' own community-resources release for
  *My Father's Work* (linked in that project's own `NOTICE.md`), not the general CC BY-NC-SA
  reference material used for content extraction — individual files may be copied/modified per that
  release's terms, just not mirrored wholesale. The blanket rule above still applies to everything
  else under `Reference/`.

---

## Other Docs to Keep Current

Keep these files updated as the project evolves:
- `docs/mws-format-latest.md` — format spec (see versioning protocol above)
- `docs/extractor.md` — extractor usage and pipeline description
- `CLAUDE.md` — this file; update when architecture decisions change
