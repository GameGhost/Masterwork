# Translation checks

`Masterwork.ModulePacker checktranslation` runs the deterministic checks on a translation against the
canonical English: a module's restext, or the app's resx. It works for any target language, however
the translation was made: imported from an official script, written by an agent, or written by a
person. The translate-verify runner, CI and a person running it by hand all get the same verdicts.
The checks don't judge meaning. The runner's back-translation and judge stages do that.

A scenario's first input is usually the `fr-CA.restext` written by `Masterwork.Extractor reconcile`
(`docs/extractor.md`). Its `draft` strings are expected to fail until the agent pass revises them,
and `--fix` leaves their `# origin:` lines alone. The checks live in `Masterwork.Translation`, and
reconcile decides its drafts with the same comparisons (`TranslationComparison`), so a string's
placeholders, prose, length ratio and missing spaces are judged the same way in both.

```powershell
dotnet run --project src\Masterwork.ModulePacker -- checktranslation `
  <module>\en-US.restext <module>\fr-CA.restext `
  --termbase terms.fr-CA.yaml --termbase <module>\terms.fr-CA.yaml `
  --fix --json report.json
```

| Option | Effect |
|---|---|
| `--locale <code>` | The target locale. Without it, the locale comes from the target file's name: `fr-CA.restext`, `fr-CA.overrides.restext`, `AppStrings.fr-CA.resx`. |
| `--termbase <file>` | A term-base file (below). Repeat it for the shared game terms plus a scenario's own list. With none, the term checks don't run. |
| `--fix` | Rewrites the target file in place before checking: Unicode NFC, then the locale's typography. Wording never changes. Comments, `# origin:` lines included, and line endings are left alone, so a fixed string keeps its origin. |
| `--json <file>` | Writes the machine-readable report. `--json -` writes it to stdout and moves the summary to stderr. |

Both files must be `.restext` or both `.resx`. Exit codes: **0** no hard failures (flags allowed),
**1** at least one hard failure, **2** bad arguments or unreadable input, including a bad term base or
a locale that can't be determined.

## Locales

Most checks compare structure with the source and apply to every language. Three depend on the
target locale:

| Rule | fr-CA | Other locales |
|---|---|---|
| Typography (`typography`, and what `--fix` rewrites) | fr-CA spacing, below | None |
| `space-after-closer` | On | Off for scripts without spaces between words: `ja`, `zh`, `th`, `lo`, `km`, `my` |
| `length-ratio` range | 0.7–2.0 | 0.5–2.5, or 0.15–1.2 for scripts without word spaces |
| `latin-script` flag | Off | On for languages written in a non-Latin script (`ru`, `ja`, `ar`, `el`, …) |

Rules are keyed by the full locale code, so fr-FR doesn't get fr-CA's typography. A locale with no
entry of its own gets the neutral defaults from its language subtag and can be checked with no code
change. Giving a language its own typography or ratio means adding an entry in `TranslationLocale`.

## Checks

Placeholders are `{…}`, and in resx, HTML tags too. Both are skipped when the checks look at prose.
Markers are `**`, `_` and `//`. Every `_` outside a placeholder is a marker, as in the renderer,
except that a run of two or more (`___`) is a fill-in blank. Both strings are compared in Unicode NFC.

**Hard failures.** The string can't ship.

| Id | Fails when |
|---|---|
| `unicode-normalization` | The translation isn't in NFC, so a decomposed accent could hide a term. `--fix` corrects it. |
| `placeholders` | The placeholder multiset differs from the source's. Order may change. A repeat counts. |
| `markup` | `**`, `_`, `//` or a `___` blank appears a different number of times. |
| `html-tags` | The tag multiset differs (resx only). |
| `multi-line` | The translation has a line break. |
| `backslash` | The translation has a backslash. Neither format has escapes. |
| `edge-whitespace` | Leading or trailing whitespace differs from the source. |
| `space-after-closer` | More placeholders, closing markers or closing tags run straight into a letter than in the source (`{playtxt}financiers`, `**4 PV**et`). Only for scripts that separate words with spaces. |
| `space-inside-italic` | More `_` markers than in the source have whitespace just inside them. |
| `typography` | The translation isn't in the locale's typography. `--fix` corrects it. For fr-CA: no space before `?` `!` `;`, a non-breaking space before `:` and inside « ». A URL's `://` and a colon between digits or placeholders (`12:30`, `{0}:{1}`) are left alone. A space just inside « » stays even before `?` or `!`. |
| `forbidden-term` | The translation matches a term base's `forbidden` pattern. |
| `empty` | The translation is empty and the source isn't. |
| `unknown-key` | The key isn't in the source file, so the string is never shown. |

A restext line that isn't a comment, blank or `Key=Value` is a file problem, and it fails too. Usually
it's a value broken across lines.

**Flags.** These go to the judge, which may accept them.

| Id | Raised when |
|---|---|
| `term-missing` | The source matches a term's `source` pattern and the translation doesn't match its `target` pattern. |
| `identical-to-source` | The translation's prose is the source's, and it has a three-letter word. Proper names can pass. |
| `latin-script` | In a locale written in another script, more than half the translation's letters are Latin: probably partly untranslated. |
| `length-ratio` | The source prose is over 20 characters and the translation is outside the locale's range. |
| `untranslated` | The target file has no entry for the key. |

## Term base

YAML, one file per target language. Every pattern is a .NET regular expression, matched
case-insensitively against prose. A plain word is a valid pattern. Use `(?-i:…)` where case matters.
Fields other than `source` and `target` are ignored, so the file can carry notes for the agents.

```yaml
terms:
  - source: '\bRounds?\b'
    target: 'manche'
    note: 'tour is a player turn'
  - source: '\bMasterworks?\b'
    target: 'Grand Œuvre'
forbidden:
  - 'Chef-d.œuvre'
  - 'Fin de tour'
```

## JSON report

```json
{
  "kind": "restext",
  "locale": "fr-CA",
  "sourceFile": "…/en-US.restext",
  "targetFile": "…/fr-CA.restext",
  "passed": false,
  "counts": { "strings": 1855, "failed": 3, "flagged": 40, "fixed": 167 },
  "fileProblems": [ { "line": 812, "message": "…" } ],
  "strings": [
    {
      "key": "Reign3_005",
      "source": "…",
      "target": "…",
      "failures": [ { "check": "placeholders", "detail": "…" } ],
      "flags": [],
      "fixed": true
    }
  ]
}
```

Every key is listed, clean ones too, in source file order, then any target-only keys. `source` is
null for an `unknown-key`, and `target` is null for an `untranslated` one. With `--fix`, `target` is
the fixed value and `fixed` says whether the fixer changed it.

## Not covered

Restext holds one value per key, with no plural forms. Languages that need several plural forms
(Polish, Russian, Arabic) can't express "{n} tokens" correctly, and no check can fix that.
