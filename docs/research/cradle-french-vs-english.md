# How far apart are the French and English Cradle scripts?

Research for [#18](https://github.com/GameGhost/Masterwork/issues/18), under the fr-CA map [#14](https://github.com/GameGhost/Masterwork/issues/14).
These are findings only. Reconciliation design belongs to the matching-method ticket (#22).

## Sources and method

- **French scripts**: `Reference/UnityOriginalApp/Assets/Scripts/StoryScript/French/{ATimeofWarFrench,FearoftheUnknownFrench,TheCostofDiseaseFrench}.cs` in the private reference workspace (Renegade community-resources release).
- **English scripts**: the matching `English/*Eng.cs`, byte-identical to `<Masterwork-Modules>/<module>/.source/*_Eng_v*.cs`.
- **Unity runtime**, for audio and images: `Assets/Scripts/UI/PassageTracker.cs`, `Assets/Scripts/ScriptableObject/VOAudio.asset`, `Assets/Scripts/UI/ViewItemObtain.cs`, `Assets/Resources/setupImages/`.
- **Current modules**: `<Masterwork-Modules>/<module>/` (`passages/`, `passages-override/`, `en-US.restext`, `en-US.overrides.restext`).
- **Extraction**: the unmodified extractor at `2fe8aba` was run on all six scripts. It used each module's real options (`--common-restext` from the module's own `.source/en-US.common.restext`, the shared `progress-map.json` and `audio-map.json`, and `--sprite-map` for Cost of Disease). Output went to a scratch directory and is not committed.
- **Comparison**: a throwaway script did the comparison, and nothing from it is committed. It loaded both extractions, flattened each passage into a sequence of node "signatures" (node type plus non-text fields, with every `restext://` reference masked), and compared the passages that share a `passage_id`. Where the signatures match exactly, the restext keys pair position by position. Elsewhere they pair through a longest-common-subsequence match. Raw passage counts come from the `this.Passages[@"…"]` registrations in each `.cs`.

The headers show the two languages were compiled from different Twine exports, not from one export:

| Scenario | English source | French source |
|---|---|---|
| A Time of War | `ATimeofWarVer8.html` | `ATimeOfWarFranch_update21.html` |
| Fear of the Unknown | `FearoftheUnknownVer15.html` | `TheFearofUnknown_Franch_UPDATE17.html` |
| The Cost of Disease | `TheCostofDiseaseVer10.html` | `TheCostofDisease_FRANCH10.html` |

Both languages were also hand-patched in C# after Cradle generated them, to different degrees. The English has 780, 818 and 962 commented-out `// yield return` lines, the French 622, 657 and 577. The French also keeps more raw Cradle `enchant(...)` calls (151, 173 and 134, against 118, 131 and 124).

## A side finding: the current English modules are a fresh extraction plus overrides

Running the extractor today on the English `.source` reproduces each module's `en-US.restext` **byte for byte**. It also reproduces `passages/` exactly, once `#` source-path comments are ignored. All hand edits live only in `passages-override/` (23, 23 and 24 files) and `en-US.overrides.restext` (49 keys). Override passages otherwise reuse extraction keys: every key the current modules use is either in the fresh English extraction (1850, 2201 and 2156 keys) or in the overrides file (49).

So a fresh English extraction *is* the current key set. Aligning French to it aligns French to the live modules.

## 1. Missing and extra passages

| | A Time of War | Fear of the Unknown | The Cost of Disease |
|---|---|---|---|
| Raw Cradle passages, EN / FR | 300 / 296 | 380 / 379 | 361 / 360 |
| Extracted, EN / FR | 297 / 296 | 378 / 379 | 361 / 360 |
| Shared `passage_id`s after extraction | 293 | 375 | 358 |

The extractor skips three unreferenced English ATOW passages (`ReignEndings`, `ResistStick`, `THEBIGREDBUTTON`) and one FotU passage (`AsylumOutcome`). English FotU also registers `AsylumQuestion1` twice, which accounts for 380 raw against 379 unique.

**Renamed in French.** This is the same passage, but links in the French script use the new name:

- ATOW: `IntroMartial-Separatists` → `IntroMartial-Séparatistes`; `Martia1Pre2` → `MartialPre2` (typo fixed).
- FotU: `FP10VP` → `FP10PV`, `FP10VPsignin` → `FP10PVsignin` ("VP" translated to "PV", *points de victoire*). The French also adds a passage named `FP10 PV`, with a space. That one is **empty**, yet it is the one the French links target (3 references). The populated `FP10PV` is never linked, so this is a French-side bug.
- CoD: `NameEntryTownConfirm` → `TCOD-NameEntryTownConfirm`.

**English-only, absent from French:**

- `VarEndingsPassage`, in all three. It is unreferenced in Cradle, and the current ATOW and CoD modules replace it with `passages-override/01_VarEndingsPassage`.
- ATOW `Reign10`, tagged `Incomplete` and unreferenced.
- CoD `ImmortalitySetup`, unreferenced.
- The skipped ATOW and FotU passages listed above.

**French-only:** `Passage sans titre` (empty and unreferenced, in ATOW and CoD) and FotU `23CodeWitchGame` (tagged `NOTE`, unreferenced).

**Net effect:** every English passage that carries player-reachable content has a French counterpart, except `Reign10` and `ImmortalitySetup`, both unreferenced. Five renames have to be mapped by hand.

## 2. Variables and branch structure

**Variables** (extracted `_variables.yaml`, EN / FR): ATOW 190 / 181, FotU 275 / 277, CoD 273 / 272. The name differences are small:

- ATOW: the English `r1`, `r5`, `r9`–`r16` are missing in French, and French adds `winnera`.
- FotU: `fp10vp`/`asylumquestion` versus `fp10PV`/`one`/`two`/`three`.
- CoD: French recases or renames `twopage`→`Twopage`, `gen3pg`→`gen3page` and `thirtyFivevp`→`thirtyfivevp`/`thirtyfivevpvp`, and it lacks the English `scoreX_submitted` flags.
- Types inferred from initializers differ for 16 FotU variables (int in English, string in French) and 9 CoD variables.

**Variables hold translated string values.** Most string-valued variables take French literals, for example `Vars.barracks = "oui"` where English has `"yes"`, and translated faction names. Of the string-valued variables, the value sets differ for 36 of 42 in ATOW, 15 of 51 in FotU and 15 of 82 in CoD.

In the current modules these values are already restext keys inside expressions: 833 `if`/`expr`/`on` fields and 67 `match` fields embed `restext://` references, for example `warwinner == "restext://Title_UnifiedMonarchists"`. Translating those keys keeps logic consistent within one locale. It does make the *stored* value locale-dependent, which matters if a save made in one language is resumed in another.

**Passage structure** for the shared passages (tags ignored, string literals masked, `== 0` treated as `!`, `.png` suffixes and the five renames normalized):

| Shared passages | ATOW 293 | FotU 375 | CoD 358 |
|---|---|---|---|
| Identical node structure | 97 (33%) | 122 (33%) | 117 (33%) |
| Same logic; only text/`break` fragmentation differs | 71 | 112 | 69 |
| Same actions; control-flow shape differs | 72 | 76 | 76 |
| Actions differ (assign/let/link/popup/goto/image/…) | 53 | 65 | 96 |
| …of which ≥4 differing action nodes | 18 | 33 | 31 |

Without the normalization, the raw extractions agree structurally on only 69, 13 and 105 passages. FotU is low because French tags 357 of its passages `Trad-FR`.

- **Control-flow shape** differs when the same branches are written another way: an `if`/`else` chain against a `switch` (the French often turns `(if: $x is "…")` chains into switches), `x == 0` against `!x`, or nested conditionals against `conditions` chains. The rows above treat these as equivalent, but the conditions themselves are not compared.
- **Action-level differences** are real logic divergence, mostly small. Examples:
  - Computed English targets that French hard-codes, such as CoD `EvilMayor`, where English picks its target by `hcount`/`society` and French always goes to `LosingOrderAid`.
  - Extra French round branches (`round == 12`, `round == 14`).
  - Changed random ranges: FotU `letter1a`/`journal1a`/`date1a` draw `rand_between(2, 29)` in French and `(1, 29)` in English.
  - Different `goto` targets (FotU French detours through `BattleStringConvert`).
  - Untranslated Unity expressions: French has `?(int.Parse(Vars.release + 1))` where English has `release + 1`.
  - Larger rewrites: CoD `HospitalVisitCheck` (41 → 11 action nodes), `Lettercode`, `WinnerHUB`, `5Note`; FotU `ScenarioBox-FOTU`, `BattleStart`, `StandardSign-inPageCode`; ATOW `ShadowBonus` (45 → 21), `TimeWarp2`, `ResistEnd*`.
  - The scoring flow differs: English `Scoring` links to `ScoreEntry`, French to `${ending}`; English `ScoreEntryP*` uses `scoreX_submitted` prompts. The current modules override scoring entirely, so this matters little.
- **The extractor handles French with a few more failures:**
  - Unknown nodes: 5 in ATOW, 2 in FotU and 1 in CoD for French, against 0, 0 and 14 for English. The 14 English ones are CoD's hand-written C# scoring.
  - Three ATOW French sites are `macros1.random(1, n);` used as a bare statement where English has `yield return text(macros1.random(1, n))`. That is a French bug: the rolled number is never shown.
  - The remaining French unknowns are raw `enchant("…", HarloweEnchantCommand.Replace, …)` reveal gates that the English had hand-rewritten into `link(... enchantHook(...))`.
  - `__UNREACHABLE__` link targets appear in 5, 18 and 6 French passage files, against 0, 4 and 1 English.

The direction of divergence is not uniform. Sometimes English has logic that French lacks (once-flags such as CoD `University1`, `scoreX_submitted`). Sometimes French has extra cases (more round branches, `PEWitch2Player*` +5 nodes each). Neither script is a strict superset of the other.

## 3. Inside the strings: fragmentation or structure?

The difference is **mostly fragmentation, with some real content divergence**.

- **Fragmentation.** Text-node totals across shared passages are close: 3188/3216 (ATOW), 4162/4222 (FotU) and 4626/4612 (CoD). In 71, 112 and 69 passages the logic is the same and only the text-node and `break` arrangement differs. In 35, 85 and 32 of those the text-node count is even equal, so only break placement or style moved. The rest split or merge fragments differently around bold runs and inline variables. Common-string promotion also differs, because the French repeats different phrases.
- **Placeholders.** Among the logic-equal passages, the multiset of `{var}`/`{icon:…}` placeholders across all strings matches in 160 of 168 (ATOW), 220 of 234 (FotU) and 179 of 186 (CoD). The 8, 14 and 7 mismatches are *content* differences, not just reordering. Example: in ATOW `MilitaryYes` the French drops the English line *"If you have no {icon:s3_paradoxtoken} tokens, gain a … token and then discard it."*
- **Extra text.** Some French passages carry text with no English counterpart. ATOW `Amessyes` keeps (in French) the "once you are ready, click here to reveal your secret conversation" prompt as plain text, where the English made it a link.

Word order alone does not explain it: roughly 5–10% of passages differ in what the text says, not just in how it is cut.

## 4. Does a parallel French extraction align key for key with English?

**Not by key name.**

- The extractor names per-passage keys `<Passage>_NNN` by position, and Common keys take curated names only when the *English* text matches `en-US.common.restext`. French Common strings therefore always get auto `Common_NNN` ids.
- English key names that also exist in the French restext: 1485 of 1855 (ATOW), 1763 of 2208 (FotU) and 1705 of 2163 (CoD).
- A same-name join is correct (structurally confirmed) for only 1171 (63%), 1363 (62%) and 1126 (52%) of English keys. Joining on name alone would be wrong about 20% of the time where a name matches.

**By structure, most keys pair:**

| | ATOW | FotU | CoD |
|---|---|---|---|
| English restext keys | 1855 | 2208 | 2163 |
| Paired to ≥1 French string by structural alignment | 1757 (95%) | 2103 (95%) | 2081 (96%) |
| Paired to exactly one distinct French string | 1679 (91%) | 2024 (92%) | 1977 (91%) |
| Placeholders agree, among single pairings that carry placeholders | 354/375 (94%) | 491/524 (94%) | 527/563 (94%) |
| French/English length ratio within 0.8–2.0, among single pairings | 1612 (96%) | 1922 (95%) | 1860 (94%) |
| Current-module keys reachable to a French string through this pairing | 1752 of 1899 | 2096 of 2250 | 2074 of 2205 |

The current-module key counts exclude `UIButton_*`. The unreachable ones are mainly the 49 app-authored override keys per module (setup and scoring), which have no Cradle source at all.

**Rough answer:** about 85–90% of English keys get a plausible single French string from an unmodified parallel extraction plus structural alignment. Alignment in passages with different fragmentation is positional, so text nodes in a run pair in order. That is why some pairs are one-to-many and the placeholder and length checks fail for about 5%. The remaining 4–5% of keys have no partner: missing passages, removed lines, or extractor failures on the French.

## 5. Audio and images

- **Voice-over.** The Cradle scripts reference no audio at all, in either language. In Unity, `PassageTracker.OnPlayVOAudioFile` looks clips up by **passage name** in one `VOAudio.asset`: 28 passages, each with a male and a female clip. Nothing checks the language, so the French build played the **same English VO**. The one exception is ATOW `IntroMartial-Séparatistes`, which the rename broke out of the lookup, leaving that passage silent in French. The extractor's `--audio-map` behaves the same way: 10 rather than 11 ATOW French passages got an `audio_track`, while FotU and CoD matched English (8 and 9). SFX and BGM are driven by Unity UI events with no language branch.
- **Setup images.** These are the same `Resources/setupImages` set, with no French-specific image files. The French scripts differ only in how they *name* the images:
  - 50 of 487 French image references carry a `.png` suffix (26, 14 and 10 per scenario), for example `"Money_Icon.png"` where English has `"Money_Icon"`. Unity's `Resources.Load("setupImages/" + name)` takes paths without extensions, so these probably failed to load in the French build.
  - A few names are broken: `S3_FlagSéparatistes` (a translated filename), `S3_enFigureToken` (for `S3_WoodenFigureToken`) and `S1_Suspicious Building` (space for underscore). None of these exist as files.
  - A few French references point to a *different* existing image (`S1_ScenarioIcon` for `S3_ScenarioIcon`, `BodyResource` for `Bodies_Icon`, `Creepy_Icon` for `S1_EstateUpgradeBACK`).
  - Since English logic is canonical, none of this is French content to carry over.

## Observations bearing on the matching method (#22)

- `passage_id` matches about 99% of passages, given a five-entry rename table.
- Per-passage structural alignment pairs about 95% of keys. Key-name joining does not.
- Placeholder multiset and length ratio are cheap validity checks. Together they flag about 5–10% of pairs.
- A finer unit than single text nodes, for example concatenating each run of text nodes between two logic nodes, would absorb most fragmentation mismatches. That has not been tested here.
- Expression-embedded restext keys (faction names, yes/no values) need the same treatment as display strings.
