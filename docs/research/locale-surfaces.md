# Which translatable surfaces already support per-locale content?

Research for [#20](https://github.com/GameGhost/Masterwork/issues/20), part of the fr-CA map
([#14](https://github.com/GameGhost/Masterwork/issues/14)). Read from source on `main` (code at
`2fe8aba`) and the `<Masterwork-Modules>` working tree, 2026-11-22. Line numbers are as of those
revisions.

## Short answer

The **module-content pipeline already accepts `fr-CA`**: drop a `fr-CA.restext` (and optionally a
`fr-CA.overrides.restext`) next to `en-US.restext` and it is picked up, offered in the language
pickers, selected by the content-language setting, and falls back to English **per key**. Audio has a
matching culture-suffix probe. The **app UI resx is already complete** in French (187/187 keys).

What does **not** work by content alone:

1. **Logic that compares against inline English literals.** About 40 expression sites in the three
   scenarios compare a variable that was assigned from a `restext://` value to a hard-coded English
   string (`seedy == "yes"`, `warwinner == "Unified Monarchists"`). In fr-CA those comparisons are
   silently false, so the game takes the wrong branch. This is the biggest risk found.
2. **Manifest titles, descriptions and playtime** can be localized in YAML, but no app code ever asks
   for a locale, so the app always shows the default-locale value.
3. **Catalog listings, asset-pack manifests, `WhiteLabelConfig`, the PWA manifest and the web
   splash** are single-language by design today.
4. **Hardcoded English** in a few places: `'The End'`/`'Close'` in every scenario's ending overrides,
   most of the template module, CSS `content: 'SETUP'` in `mwf-common-assets`, the Blazor error
   banners, `NotFound.razor`, the `Loading…` text, and `<html lang="en">`.

## How content locale is chosen today

- Two independent settings in `AppSettings`: `UiLocale` (app chrome only,
  `src/Masterwork.App.Shared/Services/AppSettings.cs:33`) and `PreferredModuleLanguage` (module
  content, `null` = no preference, `AppSettings.cs:41`). Both are edited in the main-menu Options
  dialog (`Chrome/OptionsDialog.razor:46-72`).
- The supported UI cultures are a hardcoded list, `AppUiCultures.Supported` = `en-US`, `fr-CA`
  (`AppSettings.cs:101-111`). The module-language dropdown instead lists the union of installed
  modules' `AvailableLanguages` (`OptionsDialog.razor:172`).
- `AvailableLanguages` comes from which `{locale}.restext` files are actually in the package, not
  from the manifest's `languages:` list (`src/Masterwork.App/Services/FileModuleStore.cs:145-146`,
  `src/Masterwork.App.Shared/Services/IndexedDbModuleStore.cs:195`, via
  `ModuleLocales.SortedLocales`, `src/Masterwork.ModuleFormat/ModuleLocales.cs:45`).
- At Start New Game the per-module language dropdown preselects `PreferredModuleLanguage` if the
  module has it, else `en-US`, else the first language (`Pages/StartNewGame.razor:93-97, 368-378`).
  The player can override it per game. The choice is stored on the session
  (`GameSessionState.Language`) and in every save (`SaveEntry.Language`), so a resumed game reloads
  the same language (`Pages/ContinueList.razor:271-274`).
- Loading: `ModuleLocales.SelectLocale` takes the requested locale if a file with exactly that key
  exists, else the manifest's `default_locale` (default `en-US`), else any locale present
  (`ModuleLocales.cs:22-35`). **The match is exact**: `fr` or `fr-FR` will not match `fr-CA`.
- Per-key fallback order, after selection (`ModuleLoader.cs:150-235`, `FileModuleStore.cs:76-105`):
  the selected locale's overrides, then its base file, then the default locale's overrides, then its
  base file, then dependency asset packs (the selected locale, then each pack's own default), and
  finally the raw `restext://Key` string with a load warning.

## Surface table

"Accepts fr-CA today?" means: if someone adds French content in the expected place, does the player
see it with no code change?

| Surface | Mechanism (file:line) | Accepts fr-CA today? | Gap |
|---|---|---|---|
| App UI strings | `Resources/AppStrings.resx` + `AppStrings.fr-CA.resx` (Aigamo.ResXGenerator), selected by `CurrentUICulture`, which `AppSettingsApplier.ApplyCulture` sets (`Services/AppSettingsApplier.cs:29-65`) | **Yes.** All 187 keys present in both files. 6 values are identical to English by choice (`OK`, `Options`, `Pause` ×2, `Sources` ×2). | **None** (still to be verified per #14's process) |
| Module passage text (base) | `{locale}.restext` at the module root, classified by `ModulePackage` (`ModuleFormat/ModulePackage.cs:382`), selected by `ModuleLocales.SelectLocale` (`ModuleLocales.cs:22`), resolved at load by `RestextResolver` (`ModuleFormat/RestextResolver.cs:40-190`) | **Yes** | **Content-only** for display text. Logic hazard below needs content fixes. |
| Module hand edits | `{locale}.overrides.restext`, merged per locale (`ModulePackage.cs:376-379`, `FileModuleStore.cs:79-81`) | **Yes.** A `fr-CA.overrides.restext` overrides `fr-CA.restext`. French can also go entirely in `fr-CA.restext`, since the English override layer is only consulted for keys French lacks. | **None** |
| Restext used inside logic | `restext://` inside `expr`/`if`/`target`/`match` is substituted with the escaped value before expressions are parsed (`RestextResolver.cs:9-20, 175`; spec `docs/mws-format-latest.md:194-214`) | **Partly.** Consistent only when both sides of a comparison are `restext://` refs to the **same key**. | **Content change needed**, see *Logic hazard* |
| Inline English in scenario passages | Literal YAML strings that never went through restext: `label: 'The End'` and `okay: 'Close'` on 8 ending overrides per scenario (e.g. `<Masterwork-Modules>/cost-of-disease/passages-override/00016-END-WolvesEvil1.mws.yaml:43,57`). Radio values `'male'`/`'female'` (`_Setup_08_NarrationVoiceSelect.mws.yaml:25,27`) are values, not labels, and are fine. | **No** | **Content-only**: move them to restext keys in `passages-override/` |
| Template module | `my-fathers-work-template/en-US.restext` has 83 keys, but passages carry about 229 inline English display strings (`01_Entry.mws.yaml` alone has 78) | **Mostly no** | **Content-only**: move the strings to restext first, then translate |
| Asset-pack restext | Root `{locale}.restext` in a `.mwassets` (`ModuleFormat/AssetPackPackage.cs:186-188`). Picked at the module's resolved locale, with the pack's own `default_locale` as fallback (`Services/ModuleDependencyResolver.cs:66-75`). | **Yes** (mechanism). `mwf-common-assets` ships **no restext at all**. | **None** for the mechanism. One subtlety: the module's own English fallback outranks a pack's French on a shared key (`ModuleLoader.cs:196-235`). |
| Layout chrome (`mwf-common-assets/layouts`) | Layout chrome YAML is restext-resolved (`RestextResolver.ResolveLayoutChrome`, `RestextResolver.cs:56`) | Moot. All 18 layouts are image, conditional and switch nodes with no text. | **None** |
| Common-assets CSS | `content: 'SETUP';` on the setup popup header (`<Masterwork-Modules>/mwf-common-assets/assets/style.css:1527`) | **No.** CSS has no locale hook. | **Content-only workaround** (replace with a restext-backed header node in `layouts/setup.mws.yaml`), or a code change for locale-scoped CSS |
| Manifest title, description, `info.playtime` | Localized list form `- en-US: '…'` parsed by `ManifestParser.ResolveLocalizedOrPlainString` with `preferredLocale` (`ModuleFormat/ManifestParser.cs:128-132, 159-199`) | **No.** The parser supports it, but **no production caller passes `preferredLocale`**: `FileModuleStore.cs:73,121`, `IndexedDbModuleStore.cs:75,101,145`, `ManageModules.razor:457`, `LoadedModuleContent.cs:41`, `CatalogBuilder.cs:81`. The title is baked into the installed index at install time (`FileModuleStore.cs:155`). Playtime and players are not shown anywhere in the app. | **Code change needed**: resolve at display time against the UI or content locale |
| Manifest `languages:` | Hand-maintained list (`ManifestParser.cs:134`), copied into catalog entries (`CatalogBuilder.cs:105`). The app's pickers ignore it (they use the restext files). | Yes, as data | **Content-only**: add `'fr-CA'` so the catalog advertises it |
| Catalog listing | `catalog.json` entries carry one plain `title`/`description`/`playtime` (`ModuleFormat/Catalog.cs:37-71`: "a catalog carries one language's text"). Built from the default-locale manifest (`CatalogBuilder.cs:81-112`). Shown before install via `ModuleLibrary.Title`/`Description` (`Services/ModuleLibrary.cs:41,47`). The source title is also single-language (`Catalog.cs:100`). | **No** | **Code change needed** (localized catalog fields or per-locale catalogs, plus a catalog format bump) |
| Asset-pack manifest title/description | Plain strings (`ModuleFormat/AssetPackManifestParser.cs:26-27`), shown on Manage Modules | **No** | **Code change needed** (if wanted; packs are rarely player-facing) |
| `WhiteLabelConfig` (AppName, ShortName, GameName, GamePublisher) | Single strings in the theme (`src/Masterwork.App.Theme.MyFathersWork/MyFathersWorkTheme.cs:14-28`). Spliced into resx strings as `{0}`. MAUI `ApplicationTitle` is also a single string (`src/WhiteLabel/MyFathersWork.props:15`). | **No** (these are proper nouns, so this may be fine) | **Code change needed** only if a French game or app name is wanted |
| Web PWA manifest | `WebAppPages.Manifest`, with the description from `AppStrings.App_Description` **forced to the default culture** (`Services/WebAppPages.cs:34-57, 95-107`). Served by `src/Masterwork.App.Web/WebAppManifest.cs:11` and written at build time by `Web.StaticPages/Program.cs:17-18`. | **No, by design**: "A browser fetches the manifest once at install and keeps it" (`WebAppPages.cs:17-21`) | **Code change needed** to change this; arguably keep |
| Web splash | `Chrome/WebSplash.razor`, rendered in `en-US` (`WebAppPages.cs:73-93`; pinned by `WebAppPagesTests.cs:42-44`). The tagline comes from resx, but `Loading…` is hardcoded (`WebSplash.razor:13`). | **No, by design** (shows before saved settings can be read) | **Code change needed** (could read `navigator.language` or `localStorage` in JS); `Loading…` should at least go through resx |
| `<html lang>` | `lang="en"` hardcoded in `src/Masterwork.App/wwwroot/index.html:2`, `src/Masterwork.App.Web/Components/App.razor:4`, `src/Masterwork.App.Web.Static/index.template.html:2` | **No** | **Code change needed** (set `document.documentElement.lang` from `UiLocale`; it matters for screen readers and hyphenation) |
| Other Razor/JS/CSS literals | `MainLayout.razor:20-22` "An unhandled error has occurred." / "Reload"; `wwwroot/app.css:24` `content: "An error has occurred."`; `Pages/NotFound.razor:4-5`; `src/Masterwork.App/wwwroot/index.html:19` "Loading..."; `Web/Components/Pages/Error.razor` (dev-only) | **No** | **Code change needed** (small; move to resx) |
| Language picker labels | Raw codes rendered: `<option>@lang</option>` (`StartNewGame.razor:96`, `OptionsDialog.razor:67`). Saves show the raw code too (`ContinueList.razor:67`). | Shows `fr-CA`, not "Français (Canada)" | **Code change needed** (small: a display name via `AppUiCultures` or `CultureInfo.NativeName`) |
| Engine-generated text | No player-facing English in `Masterwork.Engine`. The timeline label falls back to the passage id when a passage has no title (`Engine/GameSession.cs:782-785`). Booleans and integers stringify culture-neutrally (`StoryValue.cs:57-62`). | Yes | **None**. Exception messages spliced into localized error strings stay English (`ContinueList.razor:287`, `ManageModules.razor:387`), which is acceptable. |
| Date formatting | `AppStrings.EffectiveCulture` (`Resources/AppStrings.Culture.cs:18-21`), used at `ContinueList.razor:64` and `Play.razor:278` | Yes | **None** |
| Dev or tool load path | `ModuleLoader.LoadFromDirectory` always prefers `en-US.restext`, with no locale parameter (`ModuleLoader.cs:93-121`). `ModulePackage.WriteStandaloneToBytes` drops a pack's restext for any locale the module already ships, rather than merging keys (`ModulePackage.cs:306-312`). | Not in the app | **Code change** only if a French standalone or test build is needed |

## Logic hazard: inline English compared against restext values

`RestextResolver` substitutes the *current locale's* value into expressions. A comparison is only
locale-safe if both sides come from the same restext key. A scan of `expr`/`if`/`target`/`match`
lines for quoted literals that exactly equal a restext value (an automated scan of
`<Masterwork-Modules>`, with some hits checked by hand) found:

| Module | Candidate sites | Confirmed examples |
|---|---|---|
| cost-of-disease | 27 | `seedy` is assigned `"restext://Choice_Yes_Lower"` (`00296-Gen1CreepyYes.mws.yaml:47`) but tested as `seedy == "yes"` (`00003-Hospital1.mws.yaml:28`). `society == "Fraternity of Hunters"` (`00228-HelpingEvil.mws.yaml:27`, `00356-HelpingEvil2.mws.yaml:24`) while other passages use `restext://Faction_FraternityOfHunters_Plain`. Also `building == "library"` (`00214-WolvesEco-Friendly.mws.yaml:49`) and `"Order of St. Hubertus"` (`00174-EvilMayor.mws.yaml:32`). |
| a-time-of-war | 12 | `warwinner` is assigned from `restext://Title_*` (`00002-ATimeofWarIntro.mws.yaml:84-87`) but tested as `warwinner == "Unified Monarchists"` (`00024-Martial2.mws.yaml:180,223`, `00026-Amessno.mws.yaml:28`, `00027-Amessyes2.mws.yaml:32`) and `"Separatists"` (`00037`, `00038`, `00149`). Also `crest == "Sickle"` (`00008-RumorD1.mws.yaml:56,113` and others). |
| fear-of-the-unknown | 4 | Mostly false positives (`"Isolation"` is a passage id). `"misfortune"` (`00192-RevengePlotMisfortune.mws.yaml:50`) needs checking. `townname == "Transylvania"` (`00189`) compares free player input, which is fine. |
| template | 0 | none |

Two related patterns:

- **Identifier strings spliced into display text.** CoD's `huntbeast` is set to `"Manticore"` etc.
  (`00274-Manticore.mws.yaml:28`) and displayed through `{huntbeast}` in restext
  (`en-US.restext:2337,2353`). FotU's `"Player A"…"Player E"` (`00057-Extracodes.mws.yaml:16-40`) is
  similar. French restext cannot translate these without a passage change.
- **Distinct keys with identical English.** `Setup_Number_Two`/`Number_Two_Lower` ("two") in all
  three scenarios, and `Focus_Biology`/`Focus_Biology_Lower` and two similar pairs in CoD, are all
  used in expressions. If a translator renders the two keys of a pair differently and the logic
  assigns from one key but compares against the other, the comparison breaks. Each such pair needs
  checking before translation.

**Fix shape:** content-only, in `passages-override/` (replace literals with the matching
`restext://` refs, and split display text from identifiers). Re-extraction is not the fix, since the
extractor's `RestextCollector` left these literals in the first place. A load-time lint, "expression
literal equals a restext value", would be a small engine or tool addition that catches regressions.

## Audio

- References: `audio://` URIs on manifest `audio:` defaults (spec `docs/mws-format-latest.md:1561-1594`),
  per-passage/popup/link `audio:` blocks (`:1304-1342`) and the `audio_track` node (`:1280-1300`).
  VO is only ever reached through `audio_track`, gendered by an expression such as
  `${narrationVoice == "female" ? "audio://vo/x_f" : "audio://vo/x_m"}` (e.g.
  `<Masterwork-Modules>/cost-of-disease/passages/00005-Scenario5Start.mws.yaml:14`). The scenarios
  have 9, 8 and 11 VO passages; the template has none. BGM and SFX live mostly in `mwf-common-assets`.
- **Locale-aware today: yes, but tied to the text language.** `AssetResolver.TryResolveAudioFromSourceAsync`
  first probes `assets/audio/{slug}.{culture}{ext}` using `GameSessionState.Language`, which is the
  same value that picked the restext, then falls back to the bare `{slug}{ext}`
  (`src/Masterwork.App.Shared/Services/AssetResolver.cs:160-213`; tested in `AssetResolverTests.cs:262-327`).
  This applies to **every** `audio://` URI (VO, BGM and SFX), in the module and in its dependency
  packs.
- So today, **fr-CA text plays English VO**, because no `.fr-CA` files exist and the bare file is
  used. That is the implicit "audio = English" behaviour. There is no separate audio-language
  setting, no "None" option, and no VO volume or mute: `AppSettings` has only BGM and SFX
  volume/mute (`AppSettings.cs:11-20`).
- `cost-of-disease/assets/audio/vo/hospitalintro_m.fr-CA.ogg` is a byte-identical copy of the English
  file, kept only to exercise the probe (`<Masterwork-Modules>/NOTICE.md:74-78`). It is harmless, but
  it is not French.
- The `audio_track` `title` is restext, so it follows the text language.

## Implications for the map

- **For #21 (text vs audio language):** the mechanism is a single culture, so splitting it means a
  second setting (say `PreferredAudioLanguage`, with a "None" value), a second value on
  `GameSessionState` and in `SaveEntry`/`SessionSave`, and `AssetResolver` probing with that value
  instead of `Language`. "None" also needs a decision: suppress only VO (the `audio://vo/` prefix or
  the `audio_track` nodes) or all locale-variant audio. Muting is not enough, because a disabled
  `audio_track` still renders its controls. The culture probe applies to BGM and SFX too, which
  probably shouldn't follow a "voice language" setting, so scoping the probe to VO is part of that
  decision.
- **New engine/app work surfaced:** localized manifest fields at display time; a catalog format with
  localized fields; resx-backed error banner, `NotFound`, `Loading` and splash text; `<html lang>`;
  language display names; optionally a lint for literal-vs-restext comparisons. The web manifest and
  splash staying in English is a deliberate choice that the map should confirm or reverse.
- **Content work surfaced (Modules):** about 40 literal comparisons to fix before French is safe;
  `'The End'`/`'Close'` per ending; template inline text into restext; `SETUP` CSS text; `languages:`
  lists; and an `mwf-common-assets` restext only if any pack text is added.
