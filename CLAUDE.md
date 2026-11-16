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
├── src/MasterWork.App.Theme.MyFathersWork/    Razor Class Library — the My Father's Work flavor: its IThemeModule, WhiteLabelConfig, and app-shell skin (CSS, fonts, images, MainMenuScene)
├── src/MasterWork.App.Theme.Test/             Razor Class Library — a bare flavor proving the hosts carry no My Father's Work dependency; also the tests' theme
├── src/WhiteLabel/                            MSBuild props selecting the flavor each head is built as (WhiteLabelFlavor)
├── src/MasterWork.App.Shared/                 Razor Class Library — the actual player-app UI; shared by both heads below
├── src/MasterWork.App/                        MAUI Blazor Hybrid head — hosts Shared via BlazorWebView; Windows and Android buildable now, iOS/MacCatalyst deferred (needs a Mac build host)
├── src/MasterWork.App.Web/                    ASP.NET Core host for the web deliverable — serves Web.Client's WebAssembly payload
├── src/MasterWork.App.Web.Client/             Blazor WebAssembly client — hosts Shared in the browser; this + App.Web together are "the web app" for local runs and self-hosting
├── src/MasterWork.App.Web.Static/             Standalone Blazor WebAssembly head — the same client as plain static files, for Azure Static Web Apps (production)
├── src/MasterWork.App.Web.StaticPages/        Build-time tool: writes Web.Static's per-flavor index.html and manifest.json (never shipped)
├── src/MasterWork.App.Web.Api/                Azure Functions (.NET isolated) deployed with Web.Static as its /api — the content API redirect
├── src/MasterWork.Editor/                     WPF module designer (Phase 8 placeholder — no real work started; see masterwork-plan-rev23.md §3)
├── src/MasterWork.Extractor/                  CLI tool: Cradle C# → MWS v0.4 YAML (also owns extractor-internal node types, MwsNodes.cs)
├── src/MasterWork.ModulePacker/                CLI tool: packs a module directory into a versioned .mwm bundle
└── src/MasterWork.Tests/                      xUnit test suite
```

All fifteen projects (and the WhiteLabel props, as solution items) are registered in `Masterwork.slnx`.

Outside the solution, `site/` is the `masterworkapp.com` information site (static HTML/CSS, its own
README) and holds the privacy policy both app stores link to — update it whenever what the app stores
or sends changes.

## Build and Test

```powershell
dotnet build src/Masterwork.slnx
dotnet test src/Masterwork.Tests/Masterwork.Tests.csproj
```

The whole suite must stay green after any change to `ModuleFormat`, `Engine`, `Extractor`, or the
tested services in `App.Shared`. Don't pin an expected count here — it grows every phase; run the
suite before your change and treat that number as the baseline for your own run.

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
| `Masterwork.App` (MAUI — Windows) | `{FileSystem.AppDataDirectory}/logs/masterwork-{yyyy-MM-dd}.log` |
| `Masterwork.App` (MAUI — Android) | `Android/data/com.masterworkapp.myfatherswork/files/logs/masterwork-{yyyy-MM-dd}.log` on the device's external storage (`Context.GetExternalFilesDir(null)`, not `FileSystem.AppDataDirectory`'s private internal storage) — needs no special permission for the app's own files and is reachable via USB/MTP from a PC, unlike internal storage which needs `adb shell run-as` even to list. Falls back to `FileSystem.AppDataDirectory` only if external storage genuinely isn't mounted. |
| `Masterwork.App.Web` (ASP.NET Core host) | `{ContentRootPath}/logs/masterwork-{yyyy-MM-dd}.log` (next to the project when run via `dotnet run`) |
| `Masterwork.App.Web.Client` / `Masterwork.App.Web.Static` (WASM, runs in the browser) | **No file log** — no filesystem in the browser sandbox. The browser devtools console is the log; unhandled errors also show the `blazor-error-ui` banner. |

`wwwroot/audio.js`'s own diagnostic trail (`logAudio`) is also forwarded into this same file log on
MAUI via `JsAudioLogBridge` (`Masterwork.App.Shared/Services/JsAudioLogBridge.cs`) — a static
`[JSInvokable]` method JS calls directly (`DotNet.invokeMethodAsync("Masterwork.App.Shared", "Log", ...)`),
since audio.js's own log calls fire from native DOM/media events with no single C# component
instance to hand a `DotNetObjectReference` to. Configured once in `MauiProgram`; never configured on
WASM (no file log to forward into there either — the browser console stays authoritative).

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

### Cryptography under Blazor WASM (certificates and hashing)

Two hard constraints in the browser runtime, both of which have already caused real failures:

- **`System.Security.Cryptography.X509Certificates` does not exist there.** Every entry point throws
  `PlatformNotSupportedException` — `X509CertificateLoader.LoadCertificate` included. Anything that
  reads a certificate must therefore never be called from shared code that the web head can reach.
  `SignatureEnvelope.VerifyDigest`/`PackageSigner.Verify` are managed-only for this reason; app code
  calls `SignatureVerifier` instead, which routes the web head through `wwwroot/signatureVerify.js`
  and Web Crypto. That JS parses the public key out of the certificate DER by hand rather than
  taking it from a separate field, deliberately: the trust decision compares the *certificate's*
  thumbprint to the pinned anchor, so a separately-supplied key could be paired with a trusted
  certificate to forge a pass.
- **Managed SHA-256 runs at roughly a second per megabyte** under the WASM interpreter — a 37MB file
  measured at 45+ seconds versus well under one via `crypto.subtle`. Hash real content through
  `BrowserCrypto`/`ModuleHasher`, never `SHA256.HashData` directly. `BrowserCrypto` holds the JS
  module reference rather than importing per call (a per-call import made every hash depend on the
  network still being up, so going offline mid-install silently became a minute-long managed hash),
  and it refuses inputs over a few MB when the browser's crypto is unreachable instead of appearing
  to hang.

The package signature digest is shaped by the second constraint: it hashes each zip entry separately
and then hashes those hashes, so a verifier can go entry-by-entry through `crypto.subtle` (which is
one-shot, with no streaming API) instead of holding a whole decompressed package in memory. Both
digest paths must agree exactly — `PackageDigestTests` pins that, and a change to one without the
other would make packages signed on a native head fail to verify on the web head.

### App settings persist through one path on every head

`AppSettings` is saved by a different store per head (`LocalStorageAppSettingsStore` on web,
`PreferencesAppSettingsStore` on MAUI), and both serialize through `AppSettingsJson`. Keep it that way.
The MAUI store used to write a hand-maintained list of fields, and three settings added later —
trusted publishers, added catalog sources, hidden catalog listings — were never added to it, so on
Windows and Android they were silently never saved. Each *looked* like it worked, because pages hold
settings in memory until the next reload.

So: adding a setting to `AppSettings` needs no store change at all. It does need a non-default value in
`AppSettingsSerializationTests.FullyPopulated()` — `EveryProperty_IsCoveredByTheFixture` fails until
it has one, which is the point. The MAUI store still reads the old per-field keys once, to carry an
upgrading player's settings over; those keys are never written again.

### White-label flavors
Which theme and content source a build carries is chosen by MSBuild, never by host C#. Every head
(`Masterwork.App`, `Masterwork.App.Web.Client`, `Masterwork.App.Web`, `Masterwork.App.Web.Static`,
plus the static head's page tool `Masterwork.App.Web.StaticPages`) imports
`src/WhiteLabel/WhiteLabel.props`, which defaults `WhiteLabelFlavor` to `WhiteLabelDefaultFlavor`
(`MyFathersWork`, set in the root `Directory.Build.props`, so plain IDE Debug just works) and imports
`WhiteLabel/{Flavor}.props`. That one file holds everything
flavor-specific: the theme `ProjectReference`, an `AssemblyAttribute` stamping the host with
`[assembly: ThemeModule(typeof(...))]`, and — for MAUI only — `ApplicationId`/`ApplicationTitle`/icon/
splash. Hosts call `services.AddThemeModule(typeof(Program).Assembly)`, which reads that attribute and
registers the theme's `WhiteLabelConfig` (app name, game name, content source, pinned publisher
thumbprint) plus whatever the theme's `IThemeModule.ConfigureServices` adds (`IMainMenuTheme`).

- **Discovery is an attribute holding a `typeof`, not a reflection scan**, deliberately. .NET loads
  assemblies lazily and the compiler drops references no code uses, so scanning loaded or referenced
  assemblies for an `IThemeModule` finds nothing — verified empirically. The literal `typeof` keeps
  the reference, fails at compile time if a flavor names a missing type, and its
  `[DynamicallyAccessedMembers]` annotation keeps the constructor alive under trimming.
- **Shared never references a theme.** Themes reference Shared (to implement `IThemeModule`); the
  reverse would be a cycle.
- **Every theme serves its static assets from `_content/theme/`** (`StaticWebAssetBasePath`,
  `ThemeModules.AssetBasePath`), and must provide at least `theme.css`, a square 512px `logo.png` and a
  192px `logo-192.png`, so host markup (`index.html`, `App.razor`) and the PWA manifest never name a
  flavor. `app.css` gives most screens their structure, but **the main menu and web splash are laid
  out entirely by the theme**: its `theme.css` must lift `.mws-main-menu-content` above
  `.mws-main-menu-scene` (an absolutely-positioned scene otherwise paints over it) and give
  `.mws-main-menu-logo`/`.mws-splash-logo` a size. `Masterwork.App.Theme.Test`'s `theme.css` is the
  minimal example.
- **The app is named after its flavor, not "Masterwork".** `WhiteLabelConfig.AppName` (window and page
  titles, PWA name) and the flavor props' `ApplicationTitle` (launcher/OS name) are the same string —
  `WhiteLabelBuildTests` checks the pair. "Masterwork" appears only in running text: the app
  description ("…, powered by Masterwork."), help, and the small version + "Powered by Masterwork"
  credit (`Chrome/AppCredit.razor`) on the main menu, help screen and web splash — placed at the screen edge by
  `app.css`, above any theme frame. A **store listing title** is a separate
  thing that does carry "Masterwork", to tell the app apart from other offerings for the same game —
  see the Phase 6 design's store-readiness section; don't fold it into `AppName`.
- **Flavor-specific text lives in `WhiteLabelConfig`**, not in markup or resx. Resx strings that
  mention the game take it as `{0}` (HTML-encode it first when the string is rendered as markup).
  The web head's PWA manifest is served from config (`WebAppManifest.cs`), not a static file.
- **`Package.appxmanifest`'s `Identity Name` stays `maui-package-name-placeholder`.** The build fills it
  from the flavor's `ApplicationId`, and an unpackaged Windows app's data folder
  (`%LOCALAPPDATA%\Masterwork\{ApplicationId}`) is derived from it — a literal id there made every
  flavor share one flavor's settings, saves and modules.
- **Every head's primary source is a site's content API, `/api/content/...` (`ContentApi`)**: the
  web client on its own origin, native builds at the flavor site's address. A site may serve those
  routes itself or redirect them to wherever the content is stored, so storage can move without an
  app update. Keep new content routes under `ContentApi.BasePath`. On Static Web Apps that API is
  `Masterwork.App.Web.Api`'s redirect Function (`ContentRedirect`, into the site's own `/content/`).
- **Both WebAssembly heads register services through `BrowserHost`** (Shared); register a new browser
  service there, not in either head's `Program.cs`.
- **The static head's host page and manifest are generated at build time.** A static site can't render
  per request the way `Masterwork.App.Web` does, so `Masterwork.App.Web.StaticPages` (built for the
  same flavor) fills `index.template.html` and writes `wwwroot/index.html` and `manifest.json`
  (gitignored) from `WebAppPages`. The ASP.NET host serves the same manifest and splash (`WebSplash`)
  from the same code, so the two can't drift — but the template's other markup mirrors `App.razor` by
  hand.
- **The web server's content API upstream defaults to the flavor's own source**; a `Content` section in
  appsettings overrides only the values it sets. Startup fails if the result isn't an absolute
  http(s) URL, and logs the upstream it settled on.
- **A non-default flavor builds into `bin\{Flavor}\` and `obj\{Flavor}\`** (root `Directory.Build.props`),
  so switching flavors never overwrites the default build or leaves one flavor's theme assembly in
  another's output. The default flavor — named or not — keeps plain `bin\`/`obj\`, which is what
  release commands publish from. The flavor folders stay behind after use; clean up with
  `Remove-Item src\*\bin\Test, src\*\obj\Test -Recurse -Force`.
- **The app version is `VersionPrefix` in the root `Directory.Build.props`**, shared by every head and
  flavor; the MAUI head's `ApplicationDisplayVersion` and the in-app credit (`AppVersion`) both read it.
- **`-p:WhiteLabelFlavor=Test`** builds the bare `Masterwork.App.Theme.Test` flavor. It's the proof that
  hosts carry no hidden My Father's Work dependency, and it's the theme the test suite uses.
  `WhiteLabelBuildTests` evaluates each head per flavor.

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

### Mobile audio: autoplay gate and app-lifecycle pause/resume
Android's native `WebView` defaults to `Settings.MediaPlaybackRequiresUserGesture = true` — a gate
enforced by the native WebView itself, beneath the JS layer, entirely separate from Chromium's own
JS-level autoplay policy (which `wwwroot/audio.js`'s `attachUnlockListener` already handles for
desktop/web). No JS-side trick can satisfy this one. Fixed in
`Masterwork.App/Platforms/Android/MainActivity.cs`: `OnCreate` registers a
`BlazorWebViewHandler.BlazorWebViewMapper` customization (before `base.OnCreate`, i.e. before the
native `Android.Webkit.WebView` is actually created) that sets
`webView.Settings.MediaPlaybackRequiresUserGesture = false` once the handler creates it.

Background music also pauses when the app itself loses foreground focus and resumes on return —
`AppLifecycleAudioBridge` (`Masterwork.App.Shared/Services/AppLifecycleAudioBridge.cs`, same
static-bridge pattern as `HardwareBackButtonBridge`: `MainLayout.razor` registers its injected
`IAudioPlayer` once, native lifecycle callbacks registered in `MauiProgram.cs`'s
`ConfigureLifecycleEvents` call back in) wired to Android's `OnPause`/`OnResume` and Windows'
`Window.Activated` (`WindowActivationState.Deactivated` vs. anything else) — Windows pauses on any
focus loss (alt-tab, clicking another window), not just minimizing, since that's the only signal a
desktop window actually has; revisit if that turns out to feel too aggressive in practice. The web
build (`Masterwork.App.Web.Client`) needs none of this C# machinery — `audio.js`'s own
`document.visibilitychange` listener (already there for the AudioContext-resume-on-return fix) calls
the same JS-level pause/resume functions directly.

**iOS/MacCatalyst (not yet implemented — same deferred status as the rest of the iOS target):**
WKWebView's equivalent autoplay gate is `WKWebViewConfiguration.mediaTypesRequiringUserActionForPlayback`
(defaults to requiring a gesture for both audio and video) — set it to `[]` on the configuration
before the `WKWebView` is created, likely in `Platforms/iOS/AppDelegate.cs` or a
`BlazorWebViewHandler` mapper the same way the Android fix works, mirroring this section's own
`Page.On<iOS>()`/`BlazorWebViewHandler` precedent above for safe-area insets. For app-lifecycle
pause/resume, wire `AppLifecycleAudioBridge` to `OnResignActivation`/`OnActivated` via MAUI's
`events.AddiOS(...)` lifecycle events, the same shape as the Android `ConfigureLifecycleEvents` block.

---

## MWS Format Documentation

| File | Purpose |
|---|---|
| `docs/mws-format-latest.md` | **Authoritative current spec** — currently v0.5; edit this for changes |

No frozen prior-version files exist right now. The v0.3→v0.4 transition didn't freeze a
`mws-format-v0.3.md` before `latest` was revised (a process gap, not a deliberate decision). The
v0.4→v0.5 transition *did* freeze `mws-format-v0.4.md` briefly, then deleted it on purpose: v0.5 was
purely additive (new `audio:` fields/`audio_track` node, nothing renamed or removed), and the only
consumers of the format are the template and canonical modules in `Masterwork-Modules`, which get
updated in lockstep with engine changes anyway — so there was nothing a frozen v0.4 snapshot would
protect. Freezing remains the default for major revisions (below); skip it only when the same
reasoning applies (purely additive, no external/frozen consumers depending on the old spec text).

**Versioning protocol for format docs:**
- **Minor / QoL changes** (clarifications, new examples, field descriptions) → edit `mws-format-latest.md` inline.
- **Major revisions** (new node types, breaking field renames, structural changes) → first copy `mws-format-latest.md` to `mws-format-vN.md` to freeze the current version, then make the v(N+1) changes to `latest`, unless the additive/no-frozen-consumers exception above applies. Example: before introducing v0.6, copy `latest` as `mws-format-v0.5.md`, then revise `latest` for v0.6.

---

## Security / Licensing Constraints

- **Never mirror the reference material wholesale** (the full `Reference/UnityOriginalApp/` Unity
  project archive, `_extracted/` output, etc.) into this repo — the archive itself stays in a private
  local reference workspace, not tracked here.
- `_extracted/` output directories are also off-limits for commits here.
- The Masterwork source code itself (this repo) is a separate work from the reference content.
- **Provenance**: the entire `Reference/UnityOriginalApp/` tree — Cradle `.cs` scripts, UI/setup
  images, and audio (BGM/SFX/VO) alike — comes from Renegade Game Studios' own community-resources
  release for *My Father's Work*:
  <https://renegadegamestudios.com/blog/my-fathers-work-app-update-community-resources/>
  Per that release, individual files may be copied and modified into a real project as needed; only
  the reference archive itself must never be mirrored wholesale into a public repo.
  `src/Masterwork.App.Theme.MyFathersWork/` (images/fonts — see its own `NOTICE.md`) and each
  module's `.source/*.cs` (see `Masterwork-Modules/NOTICE.md`) are the documented examples so far;
  any other file pulled from this same release (e.g. Phase 4's real audio content) follows the same
  rule and should get its own `NOTICE.md`-style provenance note wherever it lands.

---

## Other Docs to Keep Current

Keep these files updated as the project evolves:
- `docs/mws-format-latest.md` — format spec (see versioning protocol above)
- `docs/extractor.md` — extractor usage and pipeline description
- `docs/engine.md` — session/timeline model, seeded randomness, and the popup sandbox-transaction mechanism (GameSession/VariableStore/SessionPrng)
- `CLAUDE.md` — this file; update when architecture decisions change
