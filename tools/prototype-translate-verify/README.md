# PROTOTYPE — agent translate and round-trip verify (throwaway, never merged)

Answers *What is the agent translate and round-trip-verify process?* (GameGhost/Masterwork#23) on a
55-string sample: A Time of War's drafts and gaps from the reconciliation prototype
(`prototype/cradle-french-matching`), some official strings, and 10 `AppStrings.fr-CA.resx` keys.

Stages, in order:

1. `prep-sample.ps1`: picks the sample with passage context → `sample.json`.
2. Forward (agent): drafts and gaps → `forward.json`. Term base: `termbase.md`. Unity drafts: `unity-drafts.json`.
3. `pipeline.ps1 -Stage assemble`: typography normalization plus deterministic checks → `candidates.json`, and `blind-input.json` (French only).
4. Back-translation (a separate agent, blind to the English) → `backtranslation.json`.
5. Judge (agent): English vs back-translation vs French → `judge.json` (verdict, reason, minimal fix).
6. Revision: fixes re-checked → `revisions.json`.
7. `pipeline.ps1 -Stage report` → `report.md`.

`pipeline.ps1 -Stage scan -MatchesFile <matches.json>` runs the checks over every official string in a scenario.

The term base's precedence starts with the official French rulebook, *Le Secret de mon père*
(Origames). Only short terms are cited from it. The rulebook itself is not in this repo.
Official French strings in the sample come from Renegade's community-resources release.
