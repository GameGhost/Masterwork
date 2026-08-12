# Masterwork — Claude Instructions

## Project Overview

Community companion app for the boardgame *My Father's Work* (Renegade Game Studios). Converts official scenario scripts to an open format and plays them. C# .NET 10 solution.


---

## Solution Structure

Solution file: `src/Masterwork.slnx`

```
src/Masterwork.slnx
├── src/MasterWork.Engine/                     pure C# — interpreter and game session (Phase 1, complete)
├── src/MasterWork.ModuleFormat/               VarDef, MWS reader types + YAML loader, .mwm pack/unpack (shared library)
├── src/MasterWork.App.Theme.MyFathersWork/    Razor Class Library — the default app-shell visual skin (CSS, fonts, images, MainMenuScene)
├── src/MasterWork.App.Shared/                 Razor Class Library — the actual player-app UI; shared by both heads below
├── src/MasterWork.App/                        MAUI Blazor Hybrid head — hosts Shared via BlazorWebView; Windows and Android buildable now, iOS/MacCatalyst deferred (needs a Mac build host)
├── src/MasterWork.App.Web/                    ASP.NET Core host for the web deliverable — serves Web.Client's WebAssembly payload
├── src/MasterWork.App.Web.Client/             Blazor WebAssembly client — hosts Shared in the browser; this + App.Web together are "the web app"
├── src/MasterWork.Editor/                     WPF module designer (Phase 7 placeholder — no real work started; see masterwork-plan-rev22.md §3)
├── src/MasterWork.Extractor/                  CLI tool: Cradle C# → MWS v0.4 YAML (also owns extractor-internal node types, MwsNodes.cs)
├── src/MasterWork.ModulePacker/                CLI tool: packs a module directory into a versioned .mwm bundle
└── src/MasterWork.Tests/                      xUnit test suite
```

All eleven projects are registered in `Masterwork.slnx`.

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
`src/Masterwork.Extractor/V2Serializer.cs` converts the extractor-internal `MwsNode` types (in `MwsNodes.cs`, produced by `PassageBodyVisitor`) into current-format (v0.4) YAML dicts at serialization time. The intermediate types are **kept unchanged** — this lets existing tests stay green while the emitted YAML format evolves. When the MWS format changes again, extend the serializer; do not mutate the node types. Both `MwsNodes.cs` and `V2Serializer.cs` live in the `Masterwork.Extractor` project — `ModuleFormat` holds only `VarDef` and the current reader types consumed by the engine. (The class name `V2Serializer` is historical, from the v0.1→v0.2 transition — it's evolved to emit whatever `docs/mws-format-latest.md` currently specifies, not literally tied to "v2" of anything.)

### RestextCollector
Walks the dict produced by `V2Serializer.ToDict()` and replaces human-readable strings with `restext://Key` URIs, accumulating entries for the `en-US.restext` file. It reads the current format's field names (`value`, `navigation`, `popup`, `input`, `section`). Restext values are single-line only — no multi-line block syntax.

### Test strategy
Tests in `ExtractorTests.cs` check the extractor-internal node types directly (e.g. `textNode.Template`, `linkNode.Target`). The emitted YAML format is an output concern — do not add format-specific assertions there unless specifically testing the serializer. Add a separate `V2SerializerTests.cs` if serializer tests are needed.

### Engine (Phase 1)
- `ModuleFormat/PassageYamlParser.cs` parses `.mws.yaml` by hand against YamlDotNet's low-level representation model (`YamlMappingNode` etc.), dispatching on `type:` — not a generic object-graph deserializer, since polymorphic node dispatch is exactly what that API is for.
- `ModuleFormat/RestextResolver.cs` runs once at load time, before any expression is parsed. It has two resolution modes: display fields (substituted as-is) and expression fields (substituted with embedded `"` escaped, since the value lands inside a string literal).
- `Engine/SessionPrng.cs` derives each seed_key's draw from `(masterSeed, key, occurrence#)` rather than mutating one shared `Random` per key — this is what makes timeline rewind exact (restoring position is just restoring an integer occurrence count, no need to replay RNG draw history).
- `Engine/GameSession.cs`: every `SessionSnapshot` captures state from **before** the passage it points to renders. `StepBack`/`StepForward` restore that state and **re-render** the passage (not just redisplay a cached result) — this keeps the live `VariableStore` consistent with what's shown. The one exception is `SnapshotKind.Checkpoint` entries, which capture mid-passage state and can't be reproduced by a fresh top-down render without double-applying earlier assigns; those replay from a cached render instead. See the doc comments on `SessionSnapshot` and `GameSession.RestoreAndRerenderCurrent`.
- A non-state-affecting `link`/`popup`/`goto` (`snapshot: false`, or inherited from the enclosing action — see `goto`'s own `snapshot` override in `docs/mws-format-latest.md` §6) navigates via `RenderInPlace`, which does **not** add a timeline entry. The live edge tracks at most one such divergence as `GameSession._activeState` (`Session/ActiveState.cs`) so it isn't silently lost: `StepBack`'s first press shows the entry's own anchor render instead of consuming a real timeline entry, and survives **any** amount of further stepping back into real history (it is *not* discarded by `StepBack` itself); `StepForward`/`JumpToPresent` restore it directly rather than replaying whatever produced it. Only `ResumeFromHere` (branching play from a historical point) or a new state-affecting navigation (which supersedes it with a real snapshot) discard it. Persisted through `SessionSave.ActiveState` so resuming a save taken mid-chain doesn't lose it either.
- Popup content (`popup.content`) is rendered eagerly by `PassageRenderer`, alongside the rest of the passage, against a sandboxed `VariableStore` clone (`RenderedPopup.Sandbox`) — never the live store. This means opening/closing a popup's *display* is a pure UI state toggle with no engine call involved; only `ClosePopupAsync` (Accept) touches the engine, committing the sandbox to the live store and running `onclose` + navigation as a single transaction. This deliberately trades away one thing: an unopened/never-accepted popup's content still gets evaluated (so a seeded random draw inside popup content is "spent" even if the player never opens it) — acceptable since nothing else can mutate the live store while a popup sits unopened on an already-rendered passage.
  - The commit itself (`VariableStore.CommitChangesTo`) applies only the session variables the sandbox *itself* changed, relative to its own `Clone()`-time baseline — an overlay onto whatever the live store's current state is, not `RestoreSession`'s wholesale replace. This matters because the popup node isn't necessarily the last thing in its passage's own top-level node list: a top-level `assign`/`let` sibling positioned *after* the popup runs directly against the live store during that same render, well before the player ever sees/accepts the popup, since `RenderPopup` only clones the sandbox at the point the popup node itself is reached — it doesn't pause the rest of the node list. A wholesale replace at accept-time would silently discard that later sibling's effect (real bug, found via a player-submitted save file: A Time of War's `AdvancedWeaponryIntro` sets `sepinc1`/`sepinc2`/... in `assign` nodes positioned after its own setup popup; accepting that popup used to wipe them before `Martial1` — which reads `sepinc1` — ever rendered).

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
| `docs/mws-format-latest.md` | **Authoritative current spec** — currently v0.4; edit this for changes |

No frozen prior-version files exist right now — the v0.1/v0.2 freeze files were removed once the project committed to v0.3 as the baseline, but the v0.3→v0.4 transition did **not** freeze a `mws-format-v0.3.md` before `latest` was revised (a process gap, not a deliberate decision — noted here so it isn't repeated silently). Resume the freeze protocol below for the next major revision.

**Versioning protocol for format docs:**
- **Minor / QoL changes** (clarifications, new examples, field descriptions) → edit `mws-format-latest.md` inline.
- **Major revisions** (new node types, breaking field renames, structural changes) → first copy `mws-format-latest.md` to `mws-format-vN.md` to freeze the current version, then make the v(N+1) changes to `latest`. Example: before introducing v0.5, copy `latest` as `mws-format-v0.4.md`, then revise `latest` for v0.5.

---

## Security / Licensing Constraints

- **Never commit** CC BY-NC-SA 4.0 reference material derived from the official app (extracted Unity project assets, original Cradle scripts, screenshots, etc.) to this repo. That material lives in a private local reference workspace, not tracked here.
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
- `docs/engine.md` — session/timeline model, seeded randomness, and the popup sandbox-transaction mechanism (GameSession/VariableStore/SessionPrng)
- `CLAUDE.md` — this file; update when architecture decisions change
