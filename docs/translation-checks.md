# Translation checks

`Masterwork.ModulePacker checktranslation` runs the deterministic checks on a translation: a module's
English and French restext, or the app's English and French resx. The translate-verify runner, CI and
a person running it by hand all get the same verdicts. The checks don't judge meaning. The runner's
back-translation and judge stages do that.

```powershell
dotnet run --project src\Masterwork.ModulePacker -- checktranslation `
  <module>\en-US.restext <module>\fr-CA.restext `
  --termbase terms.yaml --termbase <module>\terms.yaml `
  --fix --json report.json
```

| Option | Effect |
|---|---|
| `--termbase <file>` | A term-base file (below). Repeat it for the shared game terms plus a scenario's own list. With none, the term checks don't run. |
| `--fix` | Normalizes the French file's typography in place before checking. Only spacing changes. Comments, `# origin:` lines included, and line endings are left alone, so a fixed string keeps its origin. |
| `--json <file>` | Writes the machine-readable report. `--json -` writes it to stdout and moves the summary to stderr. |

Both files must be `.restext` or both `.resx`. Exit codes: **0** no hard failures (flags allowed),
**1** at least one hard failure, **2** bad arguments or unreadable input, including a bad term base.

## Checks

Placeholders are `{…}`, and in resx, HTML tags too. Both are skipped when the checks look at prose.
Markers are `**`, `_` and `//`. Every `_` outside a placeholder is a marker, as in the renderer.

**Hard failures.** The string can't ship.

| Id | Fails when |
|---|---|
| `placeholders` | The French's placeholder multiset differs from the English's. Order may change. A repeat counts. |
| `markup` | `**`, `_` or `//` appears a different number of times. |
| `html-tags` | The tag multiset differs (resx only). |
| `multi-line` | The French has a line break. |
| `backslash` | The French has a backslash. Neither format has escapes. |
| `edge-whitespace` | Leading or trailing whitespace differs from the English. |
| `space-after-closer` | More placeholders, closing markers or closing tags run straight into a letter than in the English (`{playtxt}financiers`, `**4 PV**et`). |
| `space-inside-italic` | More `_` markers than in the English have whitespace just inside them. |
| `typography` | The French isn't in fr-CA typography: no space before `?` `!` `;`, a non-breaking space before `:` and inside « ». `--fix` corrects it. A URL's `://` and a digit-to-digit colon (`12:30`) are left alone. |
| `forbidden-term` | The French matches a term base's `forbidden` pattern. |
| `empty` | The French is empty and the English isn't. |
| `unknown-key` | The key isn't in the English file, so the string is never shown. |

A restext line that isn't a comment, blank or `Key=Value` is a file problem, and it fails too. Usually
it's a value broken across lines.

**Flags.** These go to the judge, which may accept them.

| Id | Raised when |
|---|---|
| `term-missing` | The English matches a term's `en` pattern and the French doesn't match its `fr` pattern. |
| `identical-to-english` | The French prose is the English's, and it has a three-letter word. Proper names can pass. |
| `length-ratio` | The English prose is over 20 characters and the French is under 0.7× or over 2.0× its length. |
| `untranslated` | The French file has no entry for the key. |

## Term base

YAML. Every pattern is a .NET regular expression, matched case-insensitively against prose. A plain
word is a valid pattern. Use `(?-i:…)` where case matters. Fields other than `en` and `fr` are
ignored, so the file can carry notes for the agents.

```yaml
terms:
  - en: '\bRounds?\b'
    fr: 'manche'
    note: 'tour is a player turn'
  - en: '\bMasterworks?\b'
    fr: 'Grand Œuvre'
forbidden:
  - 'Chef-d.œuvre'
  - 'Fin de tour'
```

## JSON report

```json
{
  "kind": "restext",
  "english": "…/en-US.restext",
  "french": "…/fr-CA.restext",
  "passed": false,
  "counts": { "strings": 1855, "failed": 3, "flagged": 40, "fixed": 167 },
  "fileProblems": [ { "line": 812, "message": "…" } ],
  "strings": [
    {
      "key": "Reign3_005",
      "english": "…",
      "french": "…",
      "failures": [ { "check": "placeholders", "detail": "…" } ],
      "flags": [],
      "fixed": true
    }
  ]
}
```

Every key is listed, clean ones too, in English file order, then any French-only keys. `english` is
null for an `unknown-key`, and `french` is null for an `untranslated` one. With `--fix`, `french` is the
fixed value and `fixed` says whether the fixer changed it.
