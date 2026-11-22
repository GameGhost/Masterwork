# Official French in the Unity reference project, outside the Cradle scripts

Research for [#19](https://github.com/GameGhost/Masterwork/issues/19), part of the fr-CA map
([#14](https://github.com/GameGhost/Masterwork/issues/14)).

**Question:** what official French text exists in the Unity reference project outside
`Scripts/StoryScript/` (and `Scripts/OldStory/`), what does it cover, and which Masterwork keys does it
map to?

All Unity paths below are relative to the Unity reference project's `Assets/` folder. Scene objects are
given as their hierarchy path under `Canvas/UI/` in `Scenes/Main.unity`. The source is Renegade Game
Studios' community-resources release for *My Father's Work*. Individual strings are quoted. Paragraph
strings are shortened with "…", and the full text stays in the scene file.

## Answer in brief

- **Where the French is:** one scene, `Scenes/Main.unity`. It holds 120 `ButtonLocalText` components
  (`Scripts/Data/ButtonLocalText.cs`, a `buttonTextList` of `[English, French]`), about 60 distinct
  pairs, plus two-language string arrays serialized on six screen MonoBehaviours (`ViewPlayerIntro`,
  `ViewVillageIntro`, `ViewEndings`, `ViewShareEnding`, `ViewMainMenu`, `ViewScenarios`). One runtime
  string is hard-coded in C# (`Scripts/UI/ViewPlayerNameEnter.cs`). The language dropdown offers
  exactly `English` and `français`, and `LocalizationManager.SetLanguageName` maps them to index 0
  and 1 (`Scripts/Manager/LocalizationManager.cs`).
- **Where it isn't:** `Scenes/EndOfRound.unity`, `Scenes/Canvas.prefab` and all three `Prefabs/*.prefab`
  have no `ButtonLocalText` and no French. The CSVs (`CSV/*.csv`, the log book data) have columns for
  English, Spanish, German, Portuguese, Italian, Chinese and Japanese, but **no French**. The item-obtain
  JSON (`Resources/TheCostOfDisease_ItemObtain.json`) has no French names either, though it only drives
  sprite lookup. `StreamingAssets/` holds only a build stamp. Nothing under `Resources/` is French text.
- **A language-index defect:** several scene arrays were filled for a seven-language build: English,
  Spanish, German, Portuguese, Italian, Chinese, Japanese. In those arrays index 1 is **Spanish**, but
  the shipped dropdown maps `français` to index 1. So the French build showed Spanish for the bidding
  and voting buttons and instructions, every end-of-round body, the "Choose a Scenario for N players:"
  sentence, the per-player closing lines 2–5, and the voice-track labels in Settings. It also read the
  Spanish columns of the log book CSV. These strings have **no official French anywhere** in the Unity
  project.
- **Setup coverage:** good. Most `Setup_*` keys in the template have an official French string. The
  gaps are Masterwork-authored keys (`Setup_Greeting_2..4`, and the narrator-voice wording, which
  differs from Unity's).
- **Scoring coverage:** partial. There is French for the titles and column headers. There is none for
  rank ordinals, the scoring help line or the "Masterwork Completed" subtitle. The Tie Breaker title was
  left in English ("Tie Break").
- **App UI coverage:** thin. Main-menu buttons, settings labels, the help topics and the endings and
  achievements counters have French, but Masterwork's app UI is mostly different screens. Fewer than
  ten `AppStrings.resx` keys have a Unity counterpart, and the existing `AppStrings.fr-CA.resx`
  wording is better than Unity's in each case.
- **European or Canadian?** European conventions, machine-translation quality. Details are under
  [Register](#register-european-or-canadian-french).

## Inventory: setup flow → template and scenario restext

The template's `en-US.restext` and every scenario's `en-US.overrides.restext` carry the same `Setup_*`
and `Scoring_*` keys, so one French value serves all four files.

| Purpose | Unity location | English (Unity) | Official French | Masterwork key |
|---|---|---|---|---|
| Player-count title | `GamePlayerCounts/ViewArea/Title` | `<size=125>P</size>layers` | `<size=125>J</size>oueurs` | `Setup_PlayerCount_Title` (drop the rich text) |
| Player-count prompt | `GamePlayerCounts/ViewArea/Description` | How many individuals will be sharing this experience today? | Combien de personnes partageront cette expérience aujourd'hui ? | `Setup_PlayerCount_Prompt` |
| Player-count buttons | `GamePlayerCounts/…/Player2`, `Player3`, `Player4` | 2 Players / 3 Players / 4 Players | 2 joueurs / 3 joueurs / 4 joueurs | `Setup_PlayerCount_Two/Three/Four` |
| Number words | `ViewPlayerIntro.LanguageWiseNumberWord` | two, three, four, five | deux, Trois, quatre, cinq | `Setup_Number_Two/Three/Four` (lower-case "trois") |
| Player intro, part 1 | `ViewPlayerIntro.introPanelText1` | As siblings of an influential family of Eastern European descent, the | En tant que frères et sœurs d'une famille influente d'origine est-européenne, les | `Setup_PlayerIntro_Body1` (the number word goes between part 1 and part 2) |
| Player intro, part 2 | `ViewPlayerIntro.introPanelText2` | of us have received a not-insignificant portion of our family's estate. Before we begin, … providing our names. | d'entre nous ont reçu une part non négligeable de la succession de notre famille. Avant de commencer, rendons cette fiction historique plus attrayante en fournissant nos noms. | Split across `Setup_PlayerIntro_Body1` (to "famille.") and `Setup_PlayerIntro_Body2` |
| Pass-the-book tooltip (scene) | `PlayerSelection/ViewArea/Tooltip/ToolTipText` | Carefully pass the storybook to Player A | Passez soigneusement le livre d'histoires au joueur A | `Setup_PassBook_A..D` |
| Pass-the-book tooltip (runtime) | `Scripts/UI/ViewPlayerNameEnter.cs` `UpdateTooltiptexts` | "Carefully pass the storybook to Player " + X | "Passez Precautionneusement le livret Histoire au joueur " + X | Same keys. At runtime this overwrites the scene text and has spelling errors, so prefer the scene version. |
| Name-entry label (scene) | `PlayerSelection/ViewArea/DescriptionText` | Player A - Please enter your given name: | Joueur A - Veuillez saisir votre prénom : | `Setup_NameEntry_LabelA..D` |
| Name-entry label (runtime) | `ViewPlayerNameEnter.cs` | "Player " + X + " - Please enter your given name:" | "Joueur " + X + "- Veuillez entrer votre prénom :" | Same keys. The runtime version has a missing space and uses "entrer" instead of "saisir". |
| Greeting | `PlayerIntro/ViewArea/PLayerNamePaper/Text` | Welcome to My Father's Work, | Bienvenue dans l'Œuvre de Mon Père, | `Setup_Greeting_1` |
| Greetings 2–4 | none | none | none | `Setup_Greeting_2..4` were written for Masterwork. Agent translation needed. |
| Closing line, player 1 | `ViewPlayerIntro.introText1` | Our warmest regards. | Nos salutations les plus chaleureuses. | `Setup_WarmRegards` |
| Closing lines, players 2–5 | `ViewPlayerIntro.introText2..5` | Please enjoy your experience. / May your name live in infamy. / Be sure to leave your mark on the world. | **Spanish at index 1, no French** | No Masterwork key |
| Village title | `VillageEntery/ViewArea/Title` | `<size=125>T</size>he <size=125>V</size>illage` | `<size=125>L</size>e <size=125>V</size>illage` | `Setup_TownName_Title` |
| Village intro | `VillageEntery/ViewArea/DescriptionText` | Now that our given names have been established, … collectively name the place they will now inhabit. | Maintenant que nos prénoms sont établis, il faut aussi chercher dans notre mémoire le nom du village que surplombent nos vastes propriétés. … l'endroit qu'il habitera désormais. | `Setup_TownName_Intro` |
| Village input label | `VillageEntery/ViewArea/DescriptionText1` | Please collectively enter the name of the town in which your inheritance resides. | Veuillez saisir collectivement le nom de la ville dans laquelle réside votre héritage. | `Setup_TownName_InputLabel` |
| Village confirm, line 1 | `VillageIntro/ViewArea/Paper/DescriptionText1` | Thank you and please do excuse our forgetfulness. | Merci et veuillez excuser notre oubli. | `Setup_TownName_ConfirmBody1` |
| Village confirm, line 2 | `ViewVillageIntro.villageIntro1` + town + `villageIntro2` | Of course, **X** is an appropriate and fitting moniker … mark my words. | Bien sûr, **X** est un surnom approprié et approprié pour un lieu où mener d'importantes recherches scientifiques. En fait, l'histoire retiendra ce nom, notez mes propos. | `Setup_TownName_ConfirmBody2` (fix the doubled "approprié") |
| Voice-choice heading | `VoiceTrack/Viewarea/Prompt/Heading` | Choose Audio Voice | Choisissez la voix audio | Closest: `Setup_NarrationVoice_Title`/`_Prompt`, but the English differs. Draft only. |
| Voice options | `VoiceTrack/…/Male/Lable`, `Female/Lable` | Masculine / Feminine | Masculin / Féminine | Closest: `Setup_NarrationVoice_Male`/`_Female` ("A Gentleman's Voice"). Draft only. |
| Scenario screen title | `Scenarios/Viewarea/BG/Title` | `<size=125>S</size>cenario` | `<size=125>S</size>cénario` | No key |
| Scenario chooser sentence | `ViewScenarios.chooseText` + N + `playerText` | Choose a Scenario for N players: | **Spanish at index 1, no French** | No key |
| Scenario titles | `Scenarios/Viewarea/*Btn/*Title`; also `ViewEndings.storyNameLanguageWise` | The Cost of Disease / Fear of the Unknown / A Time of War | Le coût de la maladie / Peur de l'inconnu / Un temps de guerre | Each scenario's `manifest.yaml` `title:` (a new `fr-CA` entry) |
| Scenario blurbs | `Scenarios/Viewarea/*Btn/*Details` | The Yellow Fever had struck the village. … / The region was rife with superstitious traditions… / The specter of war haunted the small village. … | La fièvre jaune avait frappé le village. … / La région était en proie à des traditions superstitieuses… / Le spectre de la guerre hantait le petit village. … | `manifest.yaml` `description:`. Only Cost of Disease is close to Masterwork's English (which is longer). Masterwork's other two descriptions are different text. |
| Back confirmation | `GoBackConfirmation*/…/RewriteText`, `OkButton`, `CancleButton` | Are you sure you want to go back? / confirm / cancel | Êtes-vous sûr de vouloir revenir en arrière ? / confirmer / annuler | No setup key. Masterwork's timeline handles going back. |

## Inventory: scoring flow → template and scenario restext

| Purpose | Unity location | English (Unity) | Official French | Masterwork key |
|---|---|---|---|---|
| Score-entry title | `Scoring/ViewArea/Title` | `<size=125>S</size>core <size=125>E</size>ntry` | `<size=125>E</size>ntrée de <size=125>S</size>core` | `Scoring_Entry_Title` |
| Player column | `Scoring/…/Titles/Player`, `TieBreak/…/Titles/Player` | Player | Joueur | `Scoring_Header_Player` |
| Score column | `Scoring/…/Titles/Score` | Score | Score | `Scoring_Entry_HeaderScore` |
| Scoring help line | `Prefabs/ScoringCriteriaItem.prefab` (TMP text, not localized) | Add any additional points from Vanity State Upgrades | none | `Scoring_HelpBody`. Agent translation needed. |
| Tie-breaker title | `TieBreak/ViewArea/Title` | `<size=100>T</size>ie <size=100>B</size>reaker` | `<size=100>T</size>ie <size=100>B</size>reak` (left in English) | `Scoring_TieBreaker_Title`. Agent translation needed, because the "official" value is English. |
| Tie-breaker 1 column | `TieBreak/…/TieBreaker1/Titles/Completed MasterWork` | Completed Masterwork | Chef-d'œuvre terminé | `Scoring_TieBreaker1_HeaderCompleted` (and a draft for `Scoring_TieBreaker1_Subtitle`) |
| Tie-breaker 2 column | `TieBreak/…/TieBreaker2/Titles/Estate Upgrades` | Estate Upgrades | Améliorations du domaine | `Scoring_TieBreaker2_HeaderUpgrades`, `Scoring_TieBreaker2_Subtitle` |
| Rankings title | `RankingPage/ViewArea/Title` | `<size=125>R</size>ankings` | `<size=125>C</size>lassements` | `Scoring_Rank_Title` |
| Rank ordinals | `RankingPage/…/RankTexts/*` (TMP, not localized) | 1st, 2nd, 3rd, 4th | none | `Scoring_Rank_1st..4th`. Agent translation needed (1er, 2e, 3e, 4e). |
| Continue button | every `*/ContinueBtn` (11 copies) | continue | Continuez | `Common_Continue` (an imperative. Masterwork's fr-CA uses the infinitive "Continuer".) |

## Inventory: in-game UI → scenario restext (extractor-synthesized keys)

The extractor synthesizes these keys from Unity UI (`src/Masterwork.Extractor/V2Serializer.cs`; and
`progress-map.json` in the modules repo, taken from `PassageTracker`'s `progress` table). They live in
each scenario's extracted `en-US.restext`, not in the Cradle text.

| Purpose | Unity location | English (Unity) | Official French | Masterwork key |
|---|---|---|---|---|
| End-of-round header and help button | `EndOfRound/…/EndOfRoundText`, `Help/…/endofroundText` | End of Round | Fin de tour | `UIButton_EndOfRound` |
| End-of-generation header | `EndOfGeneration/…/EndOfRoundText` (also read by `ViewEndOfGeneration.cs`) | End of Generation | Fin de génération | `Title_EndOfGeneration` |
| Confirm button | `EndOfRound/ViewArea/Acceptbtn`, `EndOfGeneration/ViewArea/Acceptbtn` | CONFIRM | CONFIRMER | `UIButton_Confirm` |
| Accept button | `ItemObtain`, `ItemObtainHUB`, `SpecialEvent`, `BidingSystem` `/ViewArea/Acceptbtn` | accept | J'accepte | `UIButton_Accept` |
| Bid reveal instructions | `BidingSystem/ViewArea/Prompt/details2` | When you see the word 'REVEAL' on the screen, simultaneously show everyone your bid. | Lorsque vous voyez le mot « RÉVÉLER » à l'écran, montrez simultanément votre enchère à tout le monde. | `Bidding_RevealInstructions` (and "RÉVÉLER" for `Countdown_Reveal`) |
| Bid and vote buttons and instructions | `ViewBiddingSystem.startbuttontextlist`, `stopbuttontextlist`, `detailstextlist` | Start Bidding / Stop BIdding / Start Voting / Click on the 'Start Bidding' button … | **Spanish at index 1, no French** | `Bidding_StartButton`, `Bidding_Instructions`, `Voting_StartButton`, `Voting_Instructions`. Agent translation needed. |
| End-of-round bodies | `PassageTracker.progress[].Details`, `details2` | The Early Years of the First Generation has ended. … / Then, perform all Start of Round actions … | **Spanish at index 1, no French** | `Round_*`. Agent translation needed. The Cradle French has a few similar sentences, for example "Les premières années de la deuxième génération sont terminées.", which the Cradle ticket can use as drafts. |
| Log book entries | `CSV/Updated Features MFW - Log Book Data (1).csv` | Event title, location, summary | **no French column**. In French mode, `ViewLogBook.cs` read the Spanish columns. | No current key |
| Special Event title | none in the scene | none | none | `SpecialEvent_Title`. Agent translation needed. |

## Inventory: endings and achievements

| Purpose | Unity location | English (Unity) | Official French | Masterwork key |
|---|---|---|---|---|
| Endings button and tab | `MainMenu/…/GridButtons/Endings`, `Endings and Achievement/Viewarea/ending` | Endings | Fins | `MainMenu_Achievements` (fr-CA already "Fins") |
| Achievements tab | `Endings and Achievement/Viewarea/achievement` | Achievements | Réalisations | `Achievements_Tab_Achievements` (fr-CA "Réussites") |
| Counters | `ViewEndings.endingUnlockedText`, `achievementUnlockedText` | /24 endings unlocked · /30 achievements unlocked | /24 fins débloquées · /30 réalisations débloquées | No key |
| Ending list labels | `ViewEndings.unlockedText`, `ihaveUnckedText`, `endingText` | Unlocked the · I've unlocked the · Ending. | Déverrouillé le · j'ai débloqué le · Fin. | No key |
| Ending-unlocked popup | `ViewShareEnding.unlockedText`, `EndingText`, `reachedText`, `endingScenarioText` | You have unlocked · ending. · You have reached · out of 8 endings for this scenario. | Vous avez débloqué · fin. · Vous avez atteint · sur 8 fins pour ce scénario. | `Common_YouHaveUnlockedEnding` (overrides restext). Unity's assembly ("Vous avez débloqué X fin.") is ungrammatical, so use it as a draft only. |
| Share button | `Sharing Popup/ViewArea/share Button` | share | partager | No key |
| Scenario-completion achievements | `ViewEndings.specialAchievementLanguageWise` | Reach the end of The Cost of Disease. … Complete all 8 Endings in A Time of War | Atteignez la fin du coût de la maladie. / Atteindre la fin de Peur de l'inconnu. / Atteignez la fin de A Time of War. / Terminez les 8 fins dans Le coût de la maladie. / Terminez les 8 fins dans Peur de l'inconnu. / Terminez les 8 fins en temps de guerre | No key yet (manifest achievements are a TODO). These are poor drafts: inconsistent mood and mistranslated titles. |

### Ending titles (`ViewEndings.AchivementsLanguageWise[1]`)

The 24 French ending names match the ending titles in the Cradle French scripts word for word, so they
corroborate that source rather than add to it. **Index alignment is broken.** The English list
(`AchivementsLanguageWise[0]`) orders the scenarios Cost of Disease, A Time of War, Fear of the
Unknown, while the French list orders them Cost of Disease, Fear of the Unknown, A Time of War. Pair
them by meaning, never by index. The table below pairs them by meaning. Two pairings were confirmed
against the passage that carries each title: "Englouti par les ténèbres" is `FOTU-End1` and "De la
poussière pour les braves" is `FOTU-End5`.

| English | Official French | Masterwork key (example) |
|---|---|---|
| Local Legends | Légendes locales | `END_WolvesEvil1_001` (cost-of-disease) |
| Their Own Destiny | Leur propre destin | `END_HuntersEvil1_001` |
| There Could Never Be Peace | L'impossibilité de la paix | Cost of Disease ending passage title |
| To Be Rid of Evil | Débarrassés du mal | 〃 |
| A Legacy Will Live On | Un héritage survivra | 〃 |
| The Spread of Evil | L'emprise du mal | 〃 |
| Overshadowed and Exploited | Éclipsés et exploités | 〃 |
| Editor's note - August 1923 | Note de l'éditeur - Août 1923 | 〃 |
| Faded Into Obscurity | Englouti par les ténèbres | `FOTU-End1` title |
| A Model City | Une ville modèle | Fear of the Unknown ending passage title |
| Springtime for *townname* | Un nouveau printemps | 〃 |
| The Anger of the Mob | La colère de la foule | 〃 |
| Peace in Defiance | De la poussière pour les braves | `FOTU-End5` title |
| warden Forever | Notre guide éternel | 〃 |
| Committed | Internement | 〃 |
| Taking Charge | Prise en charge | 〃 |
| A Giant Success | Un succès Géant | `ATOW_End1_001` (a-time-of-war) |
| Behind the Curve | À la traîne | A Time of War ending passage title |
| A Wrinkle Within Time | Les replis du temps | 〃 |
| A Timeline Unbound | Une trame temporelle déchaînée | 〃 |
| Royal Praise | Louange royale | 〃 |
| Tipping the Scales | Faire pencher la balance | 〃 |
| History of Applied Sciences | Histoire des sciences appliquées | 〃 |
| Local Legend | Légende locale | 〃 |

## Inventory: main menu, settings and help → app UI (`AppStrings.resx`)

Only a few of these strings line up with a Masterwork key. In each case the existing
`AppStrings.fr-CA.resx` value is better French and needs no change.

| Purpose | Unity location | English (Unity) | Official French | Masterwork key → current fr-CA |
|---|---|---|---|---|
| New game | `MainMenu/…/GridButtons/NewGame` | new game | nouveau jeu | `MainMenu_StartNewGame` → "Nouvelle partie" |
| Continue | `MainMenu/…/GridButtons/Continue` | continue | Continuez | `MainMenu_Continue` → "Continuer" |
| Settings | `MainMenu/…/GridButtons/Setting` | settings | réglages | `MainMenu_Options` → "Paramètres" |
| Endings | `MainMenu/…/GridButtons/Endings` | Endings | Fins | `MainMenu_Achievements` → "Fins" (matches) |
| Cancel | `*/CancleButton` | cancel | annuler | `Common_Cancel` → "Annuler" (matches) |
| Language | `MainMenu/…/Settings Panel/Panel/Language` | Language | Langue | `OptionsDialog_UiLanguage` → "Langue de l'interface" |
| Music, volume | `…/Music Text`, `…/Volume Text` | Music / Volume | Musique / Le volume | `OptionsDialog_BgmVolume` → "Volume de la musique de fond" (partial match) |
| Return to title | `GanrationHUb/…/PausePopup/…/GotoMain` | return to title | retour au titre | `Play_ReturnToMainMenuLink` → "Retour au menu principal" (partial match) |
| Help topic | `Help/Viewarea/*/HowToUSebtn` | How to use | Comment utiliser | `Help_Topic_HowToUse` → "Comment jouer" |
| Help title | `Help/Viewarea/HowToUSe/DetailsPanel/title` | How to Use the Storybook | Comment utiliser le livre d'histoires | `Help_HowToUse_Title` ("How to Use Masterwork") → "Comment utiliser Masterwork" |

Unity French with **no Masterwork counterpart**: `Voice`/Voix, `Graphics`/Graphique,
`Low`/`Medium`/`High` → Bas/Moyen/Haut, `Paused`/En pause, `confirm`/confirmer,
`Rewrite existing data?`/Réécrire les données existantes ?, `Story Actions`/Actions de l'histoire, and
the three help paragraphs (Help/HowToUse, StoryActions, EndOfRound `details`). Masterwork's help
content is its own.

These Unity strings are **English only**, with no localization component: the Settings header
"Settings", "Tweak game settings to your preference", the voice dropdown "Voice 1", and the
placeholder text that screens overwrite at runtime ("lorem ipsum", "Janley", "Yellow Fever", and so on).

## Recurring terminology (term-base seed)

The "Cradle" column counts occurrences in the Cradle French scripts, to show where the two official
sources disagree. Counts are approximate (grep over `Scripts/StoryScript/French/*.cs`).

| English | Unity UI French | Cradle French | Note |
|---|---|---|---|
| Player / Players | joueur / joueurs | joueur | Consistent |
| Round | **tour** ("Fin de tour") | **manche** (~300; "fin de la manche" ~110) | Conflict. Prefer *manche*: it's the board-game term and the dominant one. |
| Generation | génération | génération | Consistent |
| Early / Middle / Late Years | none (no French) | premières années / années intermédiaires | Cradle only |
| Estate | domaine | domaine (~200) | Consistent |
| Estate Upgrades | Améliorations du domaine | Agrandissement du domaine (3) | Conflict. The rules term needs a decision. |
| Masterwork (game component) | Chef-d'œuvre | **Grand Œuvre** (7) | Conflict |
| Storybook | livre d'histoires (scene) / livret Histoire (code) | **Storybook** (~240, untranslated) / livret (~180) | Three variants |
| Village / town | village / ville | village / ville | Consistent |
| heirs | héritiers | héritiers | Consistent |
| bid / REVEAL | enchère / RÉVÉLER | enchère (~60) / Révéler | Consistent |
| Score / Score Entry | Score / Entrée de score | score | Canadian usage would be *pointage* |
| Rankings | Classements | none | |
| Tie Breaker | "Tie Break" (untranslated) | TIE-BREAKER; égalité (~120) | Needs a decision |
| Endings / ending | Fins / fin | none | |
| Achievements | Réalisations | none | fr-CA app UI uses "Réussites" |
| given name | prénom | none | |
| Accept / Continue / Confirm | J'accepte / Continuez / confirmer | none | Unity mixes 1st person, imperative and infinitive |
| VP, token, Experiment | none | PV (~545), jeton (~580), Expérience (~280) | Cradle only, but core rules vocabulary |
| *My Father's Work* (title) | "l'Œuvre de Mon Père" once (greeting); kept in English in the help paragraph | never translated | The official French doesn't settle whether to translate the title |

## Register: European or Canadian French?

**European French conventions, with machine-translation quality. There is no Canadian marker anywhere.**

- **Typography:** non-breaking space before `?` and `:` ("revenir en arrière ?", "prénom :",
  "données existantes ?"), and spaced guillemets (« RÉVÉLER »). That is the France convention. Québec
  (OQLF) usage puts no space before `?`/`!`. The Cradle French agrees with the Unity strings: about
  735 spaced `?`/`!` against about 90 unspaced. The existing `AppStrings.fr-CA.resx` follows the
  Canadian convention ("Supprimer ce module?").
- **Vocabulary:** *réglages* for settings, the anglicism *gameplay*, *Score* where Québec would say
  *pointage*. No Québec-specific vocabulary appears.
- **Quality:** word-for-word renderings: "nouveau jeu" (new game), "Le volume", "Graphique", "Haut/Bas"
  (High/Low). Outright errors: "approprié et approprié", "Precautionneusement", "Trois" capitalized,
  "Atteignez la fin du coût de la maladie", and "Tie Break" left in English. The tone is inconsistent:
  "J'accepte" next to "Continuez" and "confirmer".

**Implication for fr-CA:** treat Unity French as a source of drafts and terminology, not as final text.
Normalize typography to the fr-CA convention the app UI already uses, and give every Unity-sourced
string the same revise-and-verify pass as agent-written text.

## Sources

Unity reference project (`Assets/`):

- `Scenes/Main.unity`: the `ButtonLocalText` components (script guid `4f220e06…`); the MonoBehaviour
  arrays on `ViewPlayerIntro`, `ViewVillageIntro`, `ViewEndings`, `ViewShareEnding`, `ViewMainMenu`,
  `ViewScenarios`, `ViewBiddingSystem` and `PassageTracker`; and the language dropdown `m_Options`
- `Scenes/EndOfRound.unity`, `Scenes/Canvas.prefab`, `Prefabs/*.prefab`: checked, no French
- `Scripts/Manager/LocalizationManager.cs`, `Scripts/Data/ButtonLocalText.cs`, `Scripts/Manager/DataManager.cs` (`enum Languages { English, French }`)
- `Scripts/UI/ViewPlayerNameEnter.cs` (the hard-coded French), `ViewBiddingSystem.cs`, `ViewLogBook.cs`
  (`lanIndex = 3 * language`), `ViewScenarios.cs`, `ViewEndings.cs`, `ViewShareEnding.cs`, `PassageTracker.cs`
- `CSV/Updated Features MFW - Log Book Data (1).csv` (header row: no French), `CSV/Updated Features MFW - Sheet1.csv` (English only, unreferenced by the scene)
- `Resources/TheCostOfDisease_ItemObtain.json`, `StreamingAssets/build_info`
- `Scripts/StoryScript/French/*.cs`: used only to check the ending titles and term counts

Masterwork side: `my-fathers-work-template/en-US.restext` and `*/en-US.overrides.restext` (Setup/Scoring keys),
each scenario's `en-US.restext` and `manifest.yaml`, `progress-map.json` (in the modules repo);
`src/Masterwork.App.Shared/Resources/AppStrings.resx` and `AppStrings.fr-CA.resx`;
`src/Masterwork.Extractor/V2Serializer.cs` (synthesized UI keys).
