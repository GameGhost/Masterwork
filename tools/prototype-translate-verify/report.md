# PROTOTYPE fr-CA verification report — sample

`⍽` marks a non-breaking space. Verdicts: **pass**, **fail: meaning**, **fail: typo**, **fail: term**, **fail: integrity**. A fail sends the string back for one revision round; a second fail goes to "Unresolved".

## Summary

| Bucket | Strings | Pass first time | Revised | Unresolved | Typography normalized |
|---|---|---|---|---|---|
| a-time-of-war / draft | 16 | 16 | 0 | 0 | 0 |
| a-time-of-war / translate | 16 | 15 | 1 | 0 | 0 |
| a-time-of-war / verify | 13 | 9 | 4 | 0 | 3 |
| app-ui / verify | 10 | 10 | 0 | 0 | 3 |

## Changed or failed

### `TriggerParadox_003` — a-time-of-war, verify (official)

- **Origin:** official, revised
- **English:** Unfettered by the pressures of the mundane and reinvigorated by knowledge of the universe's complex inner workings, I surged forward anew!
- **French:** Libre des pressions du quotidien et savourant l'exalatation des connaissances des rouages internes de l'univers, j'allais à nouveau de l'avant!
- **Back-translation:** Free of the pressures of everyday life and savoring the exaltation of knowledge of the inner workings of the universe, I was once again moving forward! [note: French has the typo "exalatation" for "exaltation"]
- **Verdict:** fail: typo → revised → pass (re-checked: deterministic only) — Official text misspells 'exaltation' as 'exalatation'.
- **Revised French:** Libre des pressions du quotidien et savourant l'exaltation des connaissances des rouages internes de l'univers, j'allais à nouveau de l'avant!
- **Checks:** clean (typography normalized)

### `SecretParadoxBonus_003` — a-time-of-war, verify (spacing)

- **Origin:** official, revised
- **English:** The player(s) with the most {icon:s3_paradoxtoken} tokens remaining on their Experiments gains **4VP** and may immediately advance their lowest Journal Track 1 space at no cost (_if multiple tracks are lowest, they may choose_).
- **French:** Le ou les joueurs avec le plus de jetons {icon:s3_paradoxtoken}sur leurs Expériences gagnent **4 PV**et peuvent immédiatement faire progresser gratuitement d'une case leur piste du Journal qui a le moins progressé (_s'il y en a plusieurs, ils peuvent choisir_).
- **Back-translation:** The player or players with the most {icon:s3_paradoxtoken}tokens on their Experiments gain **4 VP**and may immediately advance by one space, for free, their Journal track that has progressed the least (_if there are several, they may choose_). [note: French is missing a space after {icon:s3_paradoxtoken} and after **4 PV**]
- **Verdict:** fail: integrity → revised → pass (re-checked: deterministic only) — Spaces lost after {icon:s3_paradoxtoken} and after **4 PV**, which the English has.
- **Revised French:** Le ou les joueurs avec le plus de jetons {icon:s3_paradoxtoken} sur leurs Expériences gagnent **4 PV** et peuvent immédiatement faire progresser gratuitement d'une case leur piste du Journal qui a le moins progressé (_s'il y en a plusieurs, ils peuvent choisir_).
- **Checks:** clean

### `ResolutionofEvent_003` — a-time-of-war, verify (spacing)

- **Origin:** official, revised
- **English:** Could it be that I had underestimated the scope of their ignorance? How was it that even under pressure from {playtxt} aristocratic financiers they remained apprehensive and defiant?
- **French:** Se pouvait-il que j'aie sous-estimé toute l'étendue de leur ignorance? Comment se faisait-il que même avec la pression des {playtxt}financiers de l'aristocratie, ils soient restés aussi craintifs et défiants?
- **Back-translation:** Could it be that I had underestimated the full extent of their ignorance? How was it that even with the pressure of the {playtxt}financiers of the aristocracy, they had remained so fearful and defiant?
- **Verdict:** fail: integrity → revised → pass (re-checked: deterministic only) — Space lost after {playtxt} (English has '{playtxt} aristocratic'), so the placeholder runs into 'financiers'.
- **Revised French:** Se pouvait-il que j'aie sous-estimé toute l'étendue de leur ignorance? Comment se faisait-il que même avec la pression des {playtxt} financiers de l'aristocratie, ils soient restés aussi craintifs et défiants?
- **Checks:** clean

### `Reign7_007` — a-time-of-war, verify (spacing)

- **Origin:** official, revised
- **English:** _This will result in: {crownR} Choose your own title that will display with your name for the rest of the story._
- **French:** _ Cela aura pour effet de⍽: {crownR}Choisissez-vous un titre qui complètera votre nom pour le reste de la partie._
- **Back-translation:** _ This will have the effect of: {crownR}Choose yourself a title that will complete your name for the rest of the game._ [note: French has a space after the opening underscore, and the sentence switches from "will have the effect of" to an imperative mid-sentence]
- **Verdict:** fail: integrity → revised → pass (re-checked: deterministic only) — Stray space just inside the opening italic marker and a lost space after {crownR} (English has '{crownR} Choose').
- **Revised French:** _Cela aura pour effet de⍽: {crownR} Choisissez-vous un titre qui complètera votre nom pour le reste de la partie._
- **Checks:** clean (typography normalized)

### `Setup_TownName_Title` — a-time-of-war, translate (override-no-source)

- **Origin:** unity, revised
- **English:** The Village
- **French:** Le Village
- **Back-translation:** The Village
- **Verdict:** fail: typo → revised → pass (re-checked: deterministic only) — French titles capitalize only the first word, so the English title case 'Village' should be lowercase (no proper noun here).
- **Revised French:** Le village
- **Checks:** clean
- **Translator note:** Kept 'Village' (not 'Ville') to match the English 'Village'.

## Passed unchanged

### `NobodyFigures_004` — a-time-of-war, verify (official)

- **Origin:** official
- **English:** Remove the Suspicion Marker and {icon:angrymob_icon} Marker from the {icon:creepy_icon} Track. Retrieve the {figure} token and place it on space **{accolade}** of the {icon:creepy_icon} Track.
- **French:** Retirez le marqueur Suspicion et le jeton {icon:angrymob_icon} de la piste {icon:creepy_icon}. Prenez le jeton {figure} et placez-le sur la case **{accolade}** de la piste {icon:creepy_icon}.
- **Back-translation:** Remove the Suspicion marker and the {icon:angrymob_icon} token from the {icon:creepy_icon} track. Take the {figure} token and place it on the **{accolade}** space of the {icon:creepy_icon} track.
- **Verdict:** pass — Angry Mob 'Marker' is rendered 'jeton' while Suspicion stays 'marqueur'; official wording, same component, kept.
- **Checks:** clean

### `Town_Paradox` — a-time-of-war, verify (official)

- **Origin:** official
- **English:** Paradox
- **French:** Paradoxe
- **Back-translation:** Paradox
- **Verdict:** pass
- **Checks:** clean

### `WhoopsUtopian_002` — a-time-of-war, verify (official)

- **Origin:** official
- **English:** While the journal of my parents was an astonishing discovery in itself, I did not realize that the true revelation would arrive several weeks later when I rifled through the pages willed to me. I suspect that you may assume that my memory had slipped or that I had not been thorough in my studies of those browning pages locked within the cellar laboratory, but I swear by my blood to be true.
- **French:** Bien que la découverte du journal de mes parents ait été en soi quelque chose d'extraordinaire, je ne me doutais pas que la véritable révélation se produirait quelques semaines plus tard alors que je parcourais ces pages qui m'avaient été léguées. En lisant ces lignes, vous imaginez sans doute que ma mémoire me jouait des tours ou que je n'avais pas fait preuve d'une rigueur suffisante en étudiant ces pages brunies enfermées dans le laboratoire de la cave, mais je vous jure sur mon sang que c'est vrai.
- **Back-translation:** Although the discovery of my parents' journal was in itself something extraordinary, I had no suspicion that the true revelation would occur a few weeks later as I was leafing through those pages that had been bequeathed to me. Reading these lines, you no doubt imagine that my memory was playing tricks on me or that I had not shown sufficient rigor in studying those browned pages locked away in the cellar laboratory, but I swear to you on my blood that it is true.
- **Verdict:** pass
- **Checks:** clean

### `Heading_KnowledgeBonus` — a-time-of-war, verify (official)

- **Origin:** official
- **English:** **Knowledge Bonus**
- **French:** **Bonus de Connaissances**
- **Back-translation:** **Knowledge Bonus**
- **Verdict:** pass
- **Checks:** clean

### `ATOW_End8_004` — a-time-of-war, verify (official)

- **Origin:** official
- **English:** While practical in theory, I recognize that the processing of enemy soldiers into consumable goods was an unfortunate practice that potentially renders this work unreadable to some.
- **French:** Bien qu'en théorie commode, j'admets volontiers que la transformation des soldats ennemis en biens consommables était une pratique bien regrettable qui peut potentiellement en révulser plus d'un et leur faire rejeter cet ouvrage.
- **Back-translation:** Although convenient in theory, I readily admit that the transformation of enemy soldiers into consumable goods was a most regrettable practice which may potentially revolt more than one person and make them reject this work.
- **Verdict:** pass
- **Checks:** clean

### `FinalBattleTime_002` — a-time-of-war, verify (official)

- **Origin:** official
- **English:** Despite our superior weapon technology and a pioneering reinterpretation of ethics in conflict, some of the neighboring countries still refused to be subjugated. The generals were prepared for a final push which would determine the outcome of the conflict and if this glorious empire would reign above all else.
- **French:** Malgré notre technologie d'armement supérieure et une réinterprétation avant-gardiste de l'éthique des conflits, certains pays voisins refusaient encore de se soumettre. Les généraux avaient préparé une ultime offensive qui allait déterminer l'issue de la guerre et si ce glorieux empire allait régner sur la création.
- **Back-translation:** Despite our superior weapons technology and an avant-garde reinterpretation of the ethics of conflict, certain neighboring countries still refused to submit. The generals had prepared a final offensive that would determine the outcome of the war and whether this glorious empire would reign over creation.
- **Verdict:** pass
- **Checks:** clean

### `Narrative_SpiritedGroupCitizens` — a-time-of-war, verify (official)

- **Origin:** official
- **English:** A spirited group of concerned citizens.
- **French:** Un groupe de citoyens animés et impliqués.
- **Back-translation:** A group of lively and engaged citizens.
- **Verdict:** pass
- **Checks:** clean

### `RumorD2_001` — a-time-of-war, verify (official)

- **Origin:** official
- **English:** No matter how I attempted to engage conversation, the frilly aristocrat ignored me. What sight in this dingy statehouse could possibly provoke him into silenced awe? Flustered, I secured my overcoat and made to leave. 
- **French:** J'avais beau essayer d'engager la conversation, l'aristocrate m'ignorait. Qu'est-ce qui pouvait à ce point l'inquiéter dans ce lieu officiel miteux? Avec un certain trouble, j'ai récupéré mon pardessus pour me préparer à partir. 
- **Back-translation:** However much I tried to strike up a conversation, the aristocrat ignored me. What could worry him so much in this shabby official place? With a certain unease, I retrieved my overcoat to prepare to leave. 
- **Verdict:** pass — Official paraphrase shifts 'silenced awe' to worry ('l'inquiéter') and drops 'frilly'; narration in the verify bucket, so kept.
- **Checks:** clean (typography normalized)

### `TriggerParadox_004` — a-time-of-war, verify (official)

- **Origin:** official
- **English:** Gain **{_rnd_TriggerParadox_0}VP** and a Knowledge cube of your choice.
- **French:** Gagnez **{_rnd_TriggerParadox_0} PV** et un cube Connaissances de votre choix.
- **Back-translation:** Gain **{_rnd_TriggerParadox_0} VP** and one Knowledge cube of your choice.
- **Verdict:** pass
- **Checks:** clean

### `Blame1c_003` — a-time-of-war, draft (needs-split)

- **Origin:** official, revised
- **English:** I was struck by a **sudden epiphany!**
- **Official French (input):** **Rappel : ne laissez pas les autres joueurs voir l'écran.** **Jeter le blâme** Les soldats ont pris le temps de m'informer de la véritable raison de leur intrusion inopportune. Les habitants de la ville avaient entendu parler d'événements étranges dans les collines surplombant la vallée et des rumeurs d'activités félonnes avaient éveillé les soupçons des forces des {warwinner}. Ils voulaient savoir si j'avais la moindre information à ce sujet. J'ai été frappé d'une **soudaine épiphanie !** Si, avec les autres membres de ma famille, nous pouvions désigner l'un de nous comme la cible de leur courroux, peut-être pourrions-nous modifier l'histoire un peu plus et éviter la destruction du domaine au cours des années à venir. Je savais que celui qui serait choisi souffrirait - il **défausserait immédiatement 1 Aménagement de Domaine et gagnerait 2** {icon:creepy_icon}**.** Nous serions punis s'il n'y avait pas un vainqueur clairement défini. Mais qu'est-ce qu'une vie sacrifiée si cela permet de rectifier l'Histoire ? J'ai décidé de :
- **French:** J'ai été frappé d'une **soudaine épiphanie!**
- **Back-translation:** I was struck by a **sudden epiphany!**
- **Verdict:** pass
- **Checks:** clean
- **Translator note:** Cut from merged fragment; removed space before !.

### `Warning2_030` — a-time-of-war, draft (needs-split)

- **Origin:** official, revised
- **English:** Suddenly, a thunderous noise erupted, reverberating against the stone walls. In the distance, we watched as towering beasts of the Mesozoic Era roamed across the dusty plains. The tyrannosaurus stalked its prey, its face glimmering brightly like a bronze statue. Moments later, a bright laser burst from its eye-socket, singling out a lone brachiosaurus from the herd and cutting it in twain. As the biomechanical menace masticated it's freshly charred meal, we determined that this may be a rather difficult environment in which to continue our experiments.
- **Official French (input):** Nous avons immédiatement perçu une odeur de cuir moisi qui a assailli nos narines comme le soufre incandescent d'une allumette. L'atmosphère était chaude, toute la ville était abandonnée comme si elle n'avait jamais existé. Nous avons eu la surprise de retrouver nos domaines à peu près intacts bien que des palmiers et des lianes recouvraient les jardins extérieurs.Soudain, se réverbérant sur les murs de pierre, un bruit de tonnerre a brisé le silence. Au loin, nous avons aperçu des bêtes immenses de l'air mésozoïque parcourir les plaines poussiéreuses. Le tyrannosaure traquait ses proies, sa tête scintillant comme une statue de bronze. Quelques instants plus tard, un rayon laser aveuglant a jailli d'une de ses orbites, isolant un brachiosaure solitaire du reste du troupeau avant de le couper en deux.Tandis que la menace biomécanique mastiquait son repas fraîchement calciné, nous nous sommes accordés sur le fait que cet environnement serait probablement un peu trop hostile pour continuer nos expériences.
- **French:** Soudain, se réverbérant sur les murs de pierre, un bruit de tonnerre a brisé le silence. Au loin, nous avons aperçu des bêtes immenses de l'ère mésozoïque parcourir les plaines poussiéreuses. Le tyrannosaure traquait ses proies, sa tête scintillant comme une statue de bronze. Quelques instants plus tard, un rayon laser aveuglant a jailli d'une de ses orbites, isolant un brachiosaure solitaire du reste du troupeau avant de le couper en deux. Tandis que la menace biomécanique mastiquait son repas fraîchement calciné, nous nous sommes accordés sur le fait que cet environnement serait probablement un peu trop hostile pour continuer nos expériences.
- **Back-translation:** Suddenly, reverberating off the stone walls, a sound of thunder broke the silence. In the distance, we glimpsed immense beasts of the Mesozoic era roaming the dusty plains. The tyrannosaurus was stalking its prey, its head gleaming like a bronze statue. A few moments later, a blinding laser beam shot out of one of its eye sockets, isolating a lone brachiosaurus from the rest of the herd before cutting it in two. While the biomechanical menace chewed its freshly charred meal, we agreed that this environment would probably be a little too hostile to continue our experiments.
- **Verdict:** pass
- **Checks:** clean
- **Translator note:** Dropped the first part (Narrative_MoldyLeatheryStenchTown); fixed official typo 'l'air mésozoïque' -> 'l'ère mésozoïque'.

### `ATOW_End5_019` — a-time-of-war, draft (needs-split)

- **Origin:** official, revised
- **English:** I now have a passion of a different sort and this competitive chapter is complete. And not a moment too soon; it is time for tea.
- **Official French (input):**  Trois générations de service, de dévouement d'influence. Vais-je continuer leur œuvre ? Je ne ressens pas encore le besoin de suivre les traces de mes chers géniteurs. Avec mon nouveau titre de Conseiller royal {winner} quatrième du nom, et bras droit de la couronne, j'ai très peu de temps pour m'adonner à des loisirs. Peut-être qu'un jour je manipulerai une pipette ; il est si difficile de prévoir l'avenir. J'ai aujourd'hui une passion d'un autre genre et ce chapitre au sujet de nos rivalités est clos. Et il était temps ; c'est l'heure du thé. LA FIN ? _Traduction du scénario : Philippe Tessier_
- **French:** J'ai aujourd'hui une passion d'un autre genre et ce chapitre au sujet de nos rivalités est clos. Et il était temps; c'est l'heure du thé.
- **Back-translation:** Today I have a passion of another kind, and this chapter about our rivalries is closed. And it was about time; it is time for tea.
- **Verdict:** pass
- **Checks:** clean
- **Translator note:** Cut from merged fragment; removed space before ;.

### `ATOW_End4_002` — a-time-of-war, draft (needs-split)

- **Origin:** official, revised
- **English:** The words scatter and reform as I move my pen about the page. Like rusted leaves turned to beetles, they skitter towards the margins. The ink flows to the nub and surges back inside. The twine that binds the pages, the glue that grips the spine, bound and unbound again, it all flits by reality like a zoopraxiscope, projected images of distorted timelines with minor changes.
- **Official French (input):** Les mots se dispersent et se reforment au fur et à mesure que je déplace mon stylo sur la page. Comme des feuilles rouillées transformées en scarabées, elles décampent vers les marges. L'encre se regroupe sur la pointe de ma plume et rentre à l'intérieur. La ficelle reliant les pages et la colle de la reliure se défont et se reconstituent encore et encore. Tout cela voltige à travers la réalité comme avec un zoopraxiscope, la projection d'images de lignes temporelles altérées par des changements mineurs. Je suis incapable de savoir si ce mémoire est complet. J'ignore si quelqu'un pourra lire ces mots. Les veines de mes doigts changent de couleur. Les {warwinner} ont gagné la guerre. Je ne sais pas si j'existe ou si je suis une simple projection. La ficelle reliant les pages et la colle de la reliure ... Est-ce que je me répète ? Pouvez-vous lire ceci ? Pouvez-vous lire ces mots ? N'y a-t-il aucune réponse ? Qui pourrait répondre à part moi-même ? Pouvez-vous lire ceci ? J'ignore si quelqu'un pourra lire ces mots. Comme des scarabées rouillés, des feuilles qui décampent vers les marges déliées Pouvez-vous page au sujet stylo déplace je comme reforme et mots dispersés le Pouvez-vous lire ceci ? LA FIN ? _Traduction du scénario : Philippe Tessier_
- **French:** Les mots se dispersent et se reforment au fur et à mesure que je déplace mon stylo sur la page. Comme des feuilles rouillées transformées en scarabées, ils décampent vers les marges. L'encre se regroupe sur la pointe de ma plume et rentre à l'intérieur. La ficelle reliant les pages et la colle de la reliure se défont et se reconstituent encore et encore. Tout cela voltige à travers la réalité comme avec un zoopraxiscope, la projection d'images de lignes temporelles altérées par des changements mineurs.
- **Back-translation:** The words scatter and re-form as I move my pen across the page. Like rusty leaves transformed into beetles, they scurry off toward the margins. The ink gathers on the tip of my nib and goes back inside. The string binding the pages and the glue of the binding come undone and reconstitute themselves over and over again. All of it flutters through reality as if with a zoopraxiscope, the projection of images of timelines altered by minor changes.
- **Verdict:** pass
- **Checks:** clean
- **Translator note:** Cut at end of this key's English; 'elles décampent' -> 'ils décampent' (subject is 'les mots').

### `MilitaryYes_007` — a-time-of-war, draft (needs-split)

- **Origin:** official, revised
- **English:** _If you have no {icon:s3_paradoxtoken} tokens, gain a {icon:s3_paradoxtoken} token and then discard it._
- **Official French (input):** **Défaussez une Expérience avec un jeton {icon:s3_paradoxtoken} dessus (défaussez aussi le jeton).** Remettez le jeton {crest} dans la boîte du scénario.
- **French:** _Si vous n'avez aucun jeton {icon:s3_paradoxtoken}, gagnez un jeton {icon:s3_paradoxtoken}, puis défaussez-le._
- **Back-translation:** _If you have no {icon:s3_paradoxtoken} tokens, gain one {icon:s3_paradoxtoken} token, then discard it._
- **Verdict:** pass
- **Checks:** clean
- **Translator note:** Official French omits this sentence entirely (fragment holds only _006 and the crest directive); written fresh in the same register.

### `ATOW_End7_011` — a-time-of-war, draft (needs-split)

- **Origin:** official, revised
- **English:** **5.** What nation is the greatest and **only** nation in the world?
- **Official French (input):** **4.** Quelle a été l'importance décisive de l'invention connue sous le nom de **{_rnd_ATOW_End7_0}** ? Justifiez votre réponse. **5.** Quelle est la **seule** et la plus grande nation du monde ? LA FIN ? _Traduction du scénario : Philippe Tessier_
- **French:** **5.** Quelle est la **seule** et la plus grande nation du monde?
- **Back-translation:** **5.** What is the **one and only** and greatest nation in the world? [note: literally "the **only** and the greatest nation"]
- **Verdict:** pass
- **Checks:** clean
- **Translator note:** Cut from merged fragment; removed space before ?.

### `ReignHUB_005` — a-time-of-war, draft (placeholders)

- **Origin:** official, revised
- **English:** Lose 2 {icon:creepy_icon} .
- **Official French (input):** Perdre 2 {icon:insanity_icon} .
- **French:** Perdre 2 {icon:creepy_icon} .
- **Back-translation:** Lose 2 {icon:creepy_icon} .
- **Verdict:** pass — Infinitive matches the official hub option list; icon corrected to creepy_icon per English.
- **Checks:** clean
- **Translator note:** Took the alternative with the correct icon (primary had insanity_icon). Kept the official infinitive used across the hub list.

### `AdvancedWeaponryIntro_006` — a-time-of-war, draft (placeholders)

- **Origin:** official, revised
- **English:** you may gain 1 Servant from Lost and lose 1 {icon:creepy_icon} .
- **Official French (input):** vous pouvez gagner un Domestique de l'Asile et perdre 1 Malepeur.
- **French:** vous pouvez gagner un Domestique de l'Asile et perdre 1 {icon:creepy_icon} .
- **Back-translation:** you may gain an Asylum Servant and lose 1 {icon:creepy_icon} .
- **Verdict:** pass
- **Checks:** clean
- **Translator note:** Restored the icon placeholder in place of the spelled-out 'Malepeur' and kept the space before the period.

### `RumorIngredient_003` — a-time-of-war, draft (placeholders)

- **Origin:** official, revised
- **English:** **Gain 1 Ingredient of your choice.** _If it is a_ {icon:bodies_icon}_, gain 1_ {icon:creepy_icon}_._
- **Official French (input):** **Gagnez 1 Ingrédient de votre choix.** _Si c'est un_ {icon:bodyresource}_, gagnez 1_ {icon:creepy_icon}_._
- **French:** **Gagnez 1 Ingrédient de votre choix.** _Si c'est un_ {icon:bodies_icon}_, gagnez 1_ {icon:creepy_icon}_._
- **Back-translation:** **Gain 1 Ingredient of your choice.** _If it is a_ {icon:bodies_icon}_, gain 1_ {icon:creepy_icon}_._
- **Verdict:** pass
- **Checks:** clean
- **Translator note:** Placeholder corrected to {icon:bodies_icon} to match the English.

### `ParadoxExReminder_004` — a-time-of-war, draft (placeholders)

- **Origin:** official, revised
- **English:**  _tokens are meant to be infinite. In the rare case that you do not have enough tokens, use another marker as a substitute until there are enough remaining._
- **Official French (input):** _Les jetons {icon:s3_paradoxtoken} ne sont pas limités. Dans le rare cas où vous n'en auriez pas assez, utilisez d'autres marqueurs comme substituts jusqu'à ce qu'il vous en reste suffisamment._
- **French:**  _Ces jetons ne sont pas limités. Dans le rare cas où vous n'en auriez pas assez, utilisez d'autres marqueurs comme substituts jusqu'à ce qu'il vous en reste suffisamment._
- **Back-translation:**  _These tokens are not limited. In the rare case that you do not have enough of them, use other markers as substitutes until you have enough of them left._
- **Verdict:** pass — 'Ces jetons' works after the icon that precedes this key; the official's 'jetons {icon}' order can't be kept.
- **Checks:** clean
- **Translator note:** English has no icon in this key (it comes before, outside the string), so the official's 'jetons {icon}' order can't be kept; used 'Ces jetons' so it reads after a preceding icon. Kept the leading space.

### `AfternoonTeaResolutionNAY_005` — a-time-of-war, draft (length)

- **Origin:** official, revised
- **English:** All players who voted **YAY** **gain 1** {icon:creepy_icon} and may draw an **Advanced Weaponry** card.
- **Official French (input):** Ma nature altruiste ne m'autorisait pas à chercher à me venger d'un incident aussi trivial alors que mon travail fondamental était destiné à changer le monde. Naturellement, j'ai tenté de dissuader mon parent de commettre un acte aussi irréfléchi, mais en vain. Comme on pouvait s'y attendre, ses manières douteuses et ses projets manifestes n'ont pas été appréciés par les responsables locaux. Il était heureux que notre illustre grand-parent ne soit plus là pour assister à ce spectacle pathétique. J'ai l'intime conviction qu'il doit encore être en train de fulminer dans son cercueil !
- **French:** Tous les joueurs ayant voté **YAY** **gagnent 1** {icon:creepy_icon} et peuvent piocher une carte **Armement avancé**.
- **Back-translation:** All players who voted **YAY** **gain 1** {icon:creepy_icon} and may draw an **Advanced Weaponry** card. [note: French says YAY although the passage is the NAY resolution]
- **Verdict:** pass — YAY matches the English source; 'Armement avancé' for Advanced Weaponry still needs confirming against the rulebook.
- **Checks:** clean
- **Translator note:** Official text for this key is the wrong passage text (_002); written fresh. 'Armement avancé' for Advanced Weaponry is not confirmed by an official source.

### `Martial1_007` — a-time-of-war, draft (length)

- **Origin:** official, revised
- **English:** **gain $2 from the supply** 
- **Official French (input):** **2 $ de la réserve**
- **French:** **gagnez 2 $ de la réserve** 
- **Back-translation:** **gain $2 from the supply** 
- **Verdict:** pass
- **Checks:** clean
- **Translator note:** Official drops the verb (likely carried in its _006 fragment); restored 'gagnez' to match this key's English. Kept the trailing space.

### `Blame1b_007` — a-time-of-war, draft (length)

- **Origin:** official, revised
- **English:** Sorry. I must sadly decline.
- **Official French (input):** Désolé. Je me vois au regret de devoir décliner cette offre.
- **French:** Désolé. Je me vois au regret de décliner.
- **Back-translation:** Sorry. I find myself obliged to regretfully decline.
- **Verdict:** pass
- **Checks:** clean
- **Translator note:** Shortened the official text to fit the button-length English.

### `WarningIntro_001` — a-time-of-war, draft (conflict)

- **Origin:** official, revised
- **English:** Future Consequences
- **Official French (input):** GÉNÉRATION III : Conséquences futures
- **French:** Conséquences futures
- **Back-translation:** Future Consequences
- **Verdict:** pass
- **Checks:** clean
- **Translator note:** Removed the 'GÉNÉRATION III :' prefix, which belongs to Title_GenerationIII.

### `TimeTravelintro1_001` — a-time-of-war, draft (conflict)

- **Origin:** official, revised
- **English:** A Return to Devastation
- **Official French (input):** GÉNÉRATION II : Retour à la dévastation
- **French:** Retour à la dévastation
- **Back-translation:** Return to Devastation
- **Verdict:** pass
- **Checks:** clean
- **Translator note:** Removed the 'GÉNÉRATION II :' prefix, which belongs to the separate generation title key.

### `Directive_MustFlipToSeparatist` — a-time-of-war, draft (conflict)

- **Origin:** official, revised
- **English:** **must** flip their Flag token to the **Separatist** side and lose **1VP**. The Monarchy is not pleased.
- **Official French (input):** **doivent**retourner leur jeton Drapeau pour révéler la face **Séparatistes**et perdre **1 PV**. La Monarchie n'a pas apprécié.
- **French:** **doivent** retourner leur jeton Drapeau pour révéler la face **Séparatistes** et perdre **1 PV**. La Monarchie n'a pas apprécié.
- **Back-translation:** **must** flip their Flag token to reveal the **Separatists** side and lose **1 VP**. The Monarchy did not appreciate it.
- **Verdict:** pass
- **Checks:** clean
- **Translator note:** Took the correctly spaced alternative. Plural 'doivent' depends on the official plural lead-in fragment.

### `Heading_BigBrother` — a-time-of-war, translate (untranslated)

- **Origin:** agent
- **English:** **Big Brother**
- **Official French (input):** **Big Brother**
- **French:** **Big Brother**
- **Back-translation:** **Big Brother**
- **Verdict:** pass — 'Still English' is correct: Big Brother is the established French form of the Orwell reference.
- **Checks:** still English
- **Translator note:** Orwell reference; 'Big Brother' is the established French form, so kept.

### `DuelRules1_008` — a-time-of-war, translate (untranslated)

- **Origin:** agent
- **English:** pistols
- **Official French (input):** pistols
- **French:** pistolets
- **Back-translation:** pistols
- **Verdict:** pass
- **Checks:** clean

### `Round_EndOfRoundActions_Rule` — a-time-of-war, translate (untranslated)

- **Origin:** agent
- **English:** Perform any End of Round actions before continuing.
- **Official French (input):** Perform any End of Round actions before continuing.
- **French:** Effectuez les actions de fin de manche avant de continuer.
- **Back-translation:** Perform the End of Round actions before continuing.
- **Verdict:** pass — 'actions de fin de manche' drops the article of the termbase's 'fin de la manche' but keeps the mandated 'manche'; natural in a compound.
- **Checks:** clean

### `Reign7c_006` — a-time-of-war, translate (untranslated)

- **Origin:** agent
- **English:** {title} {nameE}
- **Official French (input):** {title} {nameE}
- **French:** {title} {nameE}
- **Back-translation:** {title} {nameE}
- **Verdict:** pass
- **Checks:** clean
- **Translator note:** Placeholders only; unchanged.

### `TimeTravel_011` — a-time-of-war, translate (untranslated)

- **Origin:** agent
- **English:** The Early Years of the Second Generation has ended. Perform all End of Round actions.
- **Official French (input):** The Early Years of the Second Generation has ended. Perform all End of Round actions.
- **French:** Le Début de cycle de la deuxième Génération est terminé. Effectuez toutes les actions de fin de manche.
- **Back-translation:** The Early Years of the second Generation has ended. Perform all End of Round actions.
- **Verdict:** pass — Same 'actions de fin de manche' compound as Round_EndOfRoundActions_Rule; the rest follows the termbase example exactly.
- **Checks:** clean
- **Translator note:** Pattern from the term base example.

### `NameEntryC_001` — a-time-of-war, translate (untranslated)

- **Origin:** agent
- **English:** {nameC}.
- **Official French (input):** {nameC}.
- **French:** {nameC}.
- **Back-translation:** {nameC}.
- **Verdict:** pass
- **Checks:** clean
- **Translator note:** Placeholder only; unchanged.

### `ParadoxTimeRandom_029` — a-time-of-war, translate (unmatched)

- **Origin:** agent
- **English:** This was a minor setback.
- **French:** Ce ne fut qu'un revers mineur.
- **Back-translation:** It was only a minor setback.
- **Verdict:** pass — 'ne… que' adds a slight 'only', which fits the downplaying tone; nothing of substance changes.
- **Checks:** clean
- **Translator note:** Passé simple for narration.

### `ResistEnd2_005` — a-time-of-war, translate (unmatched)

- **Origin:** agent
- **English:** The {warwinner}
- **French:** Les {warwinner}
- **Back-translation:** The {warwinner}
- **Verdict:** pass
- **Checks:** clean
- **Translator note:** Follows the official 'Les {warwinner} ont gagné la guerre'.

### `TimeEnd2_001` — a-time-of-war, translate (unmatched)

- **Origin:** agent
- **English:** Fully Repaired
- **French:** Entièrement réparée
- **Back-translation:** Entirely Repaired [note: feminine past participle, agreeing with an unstated feminine noun]
- **Verdict:** pass — Feminine 'réparée' correctly agrees with the Time Machine (la machine) from context.
- **Checks:** clean
- **Translator note:** Feminine to agree with 'machine' (the Time Machine); choice heading.

### `RumorD1_006` — a-time-of-war, translate (unmatched)

- **Origin:** agent
- **English:** "I think your fears are unfounded. The {warwinner} are planning on building a barracks in the coming years. That means stability, prosperity, and _opportunity._"
- **French:** «⍽Je pense que vos craintes sont infondées. Les {warwinner} prévoient de construire une caserne dans les années à venir. Cela signifie stabilité, prospérité et _opportunités._⍽»
- **Back-translation:** "I think your fears are unfounded. The {warwinner} plan to build a barracks in the coming years. That means stability, prosperity and _opportunities._"
- **Verdict:** pass — 'caserne' for Barracks still needs checking against the rulebook's board-space name.
- **Checks:** clean
- **Translator note:** Quotes converted to guillemets with NBSP. 'caserne' for barracks is not confirmed against the rulebook's board-space name.

### `ATimeofWarIntro_002` — a-time-of-war, translate (unmatched)

- **Origin:** agent
- **English:** GENERATION I
- **French:** GÉNÉRATION I
- **Back-translation:** GENERATION I
- **Verdict:** pass
- **Checks:** clean

### `Setup_TownName_Intro` — a-time-of-war, translate (override-no-source)

- **Origin:** unity, revised
- **English:** Now that our given names have been established, we must also search our recollection for the name of the village which our vast estates overlook. Considering the myriad of small towns in the Hungarian countryside, it would be unmeritably forward to not allow the group a chance to collectively name the place they will now inhabit.
- **French:** Maintenant que nos prénoms sont établis, il nous faut aussi chercher dans nos souvenirs le nom du village que surplombent nos vastes domaines. Vu la myriade de petites bourgades de la campagne hongroise, il serait d'une impertinence injustifiable de ne pas laisser au groupe l'occasion de nommer ensemble l'endroit qu'il habitera désormais.
- **Back-translation:** Now that our first names are established, we must also search our memories for the name of the village overlooked by our vast estates. Given the myriad of small hamlets of the Hungarian countryside, it would be an unjustifiable impertinence not to give the group the opportunity to name together the place it will henceforth inhabit.
- **Verdict:** pass
- **Checks:** clean
- **Translator note:** Filled in the middle sentence the Unity draft had elided with '…'.

### `Setup_PlayerCount_Four` — a-time-of-war, translate (override-no-source)

- **Origin:** unity, revised
- **English:** 4 Players
- **French:** 4 joueurs
- **Back-translation:** 4 players
- **Verdict:** pass
- **Checks:** clean
- **Translator note:** Unity draft accepted as is.

### `Setup_NameEntry_LabelC` — a-time-of-war, translate (override-no-source)

- **Origin:** unity, revised
- **English:** Player C - Please enter your given name:
- **French:** Joueur C - Veuillez saisir votre prénom⍽:
- **Back-translation:** Player C - Please enter your first name:
- **Verdict:** pass
- **Checks:** clean
- **Translator note:** NBSP before the colon.

### `Setup_Greeting_2` — a-time-of-war, translate (override-no-source)

- **Origin:** agent
- **English:** Greetings and welcome to My Father's Work,
- **French:** Salutations et bienvenue dans Le Secret de mon père,
- **Back-translation:** Greetings and welcome to My Father's Work,
- **Verdict:** pass — The in-story letter names the game, so the termbase French title applies (the app name rule doesn't).
- **Checks:** clean
- **Translator note:** Treated as the game in running text, so used Le Secret de mon père per the term base.

### `Common_Resume` — app-ui, verify (existing-fr-CA)

- **Origin:** existing
- **English:** Resume
- **French:** Reprendre
- **Back-translation:** Resume
- **Verdict:** pass
- **Checks:** clean

### `CatalogSources_ErrorUnreachable` — app-ui, verify (existing-fr-CA)

- **Origin:** existing
- **English:** Nothing answered at that address.
- **French:** Rien n'a répondu à cette adresse.
- **Back-translation:** Nothing responded at this address.
- **Verdict:** pass
- **Checks:** clean

### `ManageModules_UnsignedBody` — app-ui, verify (existing-fr-CA)

- **Origin:** existing
- **English:** '{0}' isn't signed. Unable to verify content integrity or publisher. Install at your own risk.
- **French:** «⍽{0}⍽» n'est pas signé. Impossible de vérifier l'intégrité du contenu ni l'identité de l'éditeur. Installez-le à vos risques.
- **Back-translation:** "{0}" is not signed. Impossible to verify the integrity of the content or the identity of the publisher. Install it at your own risk.
- **Verdict:** pass
- **Checks:** clean (typography normalized)

### `ManageModules_UnrecognizedSignerBodyModule` — app-ui, verify (existing-fr-CA)

- **Origin:** existing
- **English:** This module was created by '{0}'. Install if you trust the publisher.
- **French:** Ce module a été créé par «⍽{0}⍽». Installez-le si vous faites confiance à l'éditeur.
- **Back-translation:** This module was created by "{0}". Install it if you trust the publisher.
- **Verdict:** pass
- **Checks:** clean (typography normalized)

### `TimelineScrubber_CurrentLabel` — app-ui, verify (existing-fr-CA)

- **Origin:** existing
- **English:** Current
- **French:** Actuel
- **Back-translation:** Current
- **Verdict:** pass
- **Checks:** clean

### `ContinueList_ResumeAnyway` — app-ui, verify (existing-fr-CA)

- **Origin:** existing
- **English:** Resume anyway
- **French:** Reprendre quand même
- **Back-translation:** Resume anyway
- **Verdict:** pass
- **Checks:** clean

### `Help_Topic_TimelineAndRewind` — app-ui, verify (existing-fr-CA)

- **Origin:** existing
- **English:** Timeline and rewind
- **French:** Chronologie et retour en arrière
- **Back-translation:** Timeline and going back
- **Verdict:** pass
- **Checks:** clean

### `TimelineScrubber_ResumeConfirmTitle` — app-ui, verify (existing-fr-CA)

- **Origin:** existing
- **English:** Resume from "{0}"?
- **French:** Reprendre à partir de «⍽{0}⍽»?
- **Back-translation:** Resume from "{0}"?
- **Verdict:** pass
- **Checks:** clean (typography normalized)

### `ContinueList_ModuleMissing` — app-ui, verify (existing-fr-CA)

- **Origin:** existing
- **English:** This save's module isn't installed anymore.
- **French:** Le module de cette sauvegarde n'est plus installé.
- **Back-translation:** The module for this save is no longer installed.
- **Verdict:** pass
- **Checks:** clean

### `ContinueList_ModuleMissingTitle` — app-ui, verify (existing-fr-CA)

- **Origin:** existing
- **English:** This save's module isn't installed anymore
- **French:** Le module de cette sauvegarde n'est plus installé
- **Back-translation:** The module for this save is no longer installed
- **Verdict:** pass
- **Checks:** clean

