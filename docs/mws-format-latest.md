# Masterwork Script Format — v0.5 Reference

MWS (Masterwork Script) is the YAML-based passage format used to represent interactive narrative content for the Masterwork engine. Each `.mws.yaml` file is a single passage.

---

## 1. File Structure

Every passage file is a YAML document with a standard header followed by a `nodes:` list.

```yaml
format: 'mws/0.5'
passage_id: 'Hospital1'
title: 'The Hospital'
tags:
- 'HUB'
layout: 'hub'
location:
  name: 'The Hospital'
  icon: 'icon://hospital_icon'
check_progress: 'Hospital0'
audio:
  music: 'audio://bgm/hospital_theme'
nodes:
  - ...
```

### Header Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `format` | string | yes | Always `mws/0.5`. Identifies which format revision produced the file — bump when re-extracting or hand-authoring against a newer version of this spec |
| `passage_id` | string | yes | Canonical passage identifier |
| `title` | string | no | Display title; defaults to `passage_id`. For `hub`/`narration` passages, the extractor hoists this from the source's leading bold-styled text block (see below) instead of leaving it as an ordinary body node |
| `subtitle` | string | no | Optional subtitle shown alongside `title` in the passage header. Populated the same way as `title` — either the second line of a two-line bold heading, or the part after " - " in a single-line "Title - Subtitle" heading |
| `tags` | list of strings | no | Source tags; drives layout inference |
| `layout` | string | yes | An open, module-extensible vocabulary. Built-in values: `hub`, `event`, `narration` (see below) |
| `debug` | bool | no | `true` for developer-only passages excluded from player builds |
| `location` | object | no | Location shown in app header. Fields: `name` (string), `icon` (asset URI) |
| `check_progress` | string | no | `passage_id` that must have been visited before this passage is valid to render |
| `audio` | object | no | Background-music override and an on-display sound for this passage — see §6 "Audio" below |

For `hub`/`narration`/`introduction` passages, the extractor recognizes a leading bold-styled text block as the
passage's heading rather than emitting it as ordinary body text, per these rules: a single bold
line splits on the first standalone `" - "` into `title`/`subtitle` (no `-` → the whole line becomes
`title` with no `subtitle`); two bold lines separated by a single `break` become `title` and
`subtitle`, but only when that break sits *inside the same source styleScope* as both lines (a
`lineBreak()` nested inside one `using (styleScope("bold", true))` block, not a break between two
separate scopes). A break between two separate bold styleScopes is never folded in as a subtitle —
the second scope is left as ordinary body text, since in every real occurrence that shape is an
unrelated sentence (an instruction, a question), not a continuation of the heading. Whichever shape
matches, leading/trailing whitespace and `:` characters are trimmed from `title`/`subtitle` text
(e.g. a source line of `"GENERATION I:"` extracts as `title: 'GENERATION I'`). Any other shape —
three or more bold lines, mixed-style leading text, non-text content first — is left untouched and
extracted as normal nodes. This detection only ever looks at the very first block of the passage; a
bold run anywhere else in the body is unaffected. The app renders `title`/`subtitle` in a dedicated
header region above the passage body (see `Masterwork.App.Shared`'s `PassageView`), and the timeline
scrubber's default snapshot label uses `"{title} - {subtitle}"` when both are set.

### Layout Values

| Layout | Description |
|---|---|
| `hub` | Generation hub — sections with headings, collapsible bodies, multiple optional links |
| `event` | Full-page event card — narrative text with prominent bottom links |
| `narration` | Story passage; minimal chrome |
| `introduction` | Generation-opening passage (Cradle tag `INTRO`) — visually distinct from ordinary narration in the reference app |

`layout` is an open vocabulary beyond this built-in list — a module's own `--progress-map` (see
`docs/extractor.md`) can override a passage's layout entirely, e.g. Cost of Disease splits `hub`
into `hub_early`/`hub_middle`/`hub_late` for its three distinct per-round hub screens (see §8).

These are the only values the engine's own passage chrome renders specially; any other string is accepted and rendered generically (with a `layout-{value}` CSS class for module/asset-pack theming), since `layout` is deliberately an open vocabulary a module can extend. A generation-end summary is handled by `popup` nodes with `layout: end_of_generation` (see §9), which is unrelated to a passage-level `modal` layout. A module wanting a "reveal to one player" moment can compose one from ordinary `conditional`/`input`/`assign` nodes.

The passage `layout` is a CSS/structural hook, not a manifest-declared chrome definition — see §8 "Module Layout/Chrome" for how module CSS and, optionally, a `layouts/{id}.yaml` chrome file give it visual meaning.

---

## 2. Variable Types

Variables exist at two scopes: **session variables** are module-global and persist across passages; **let variables** are passage-scoped and discarded after each render. Both scopes use the same type system.

### Built-in Types

| Type | Description |
|---|---|
| `int` | Integer |
| `string` | Text string |
| `array` | Ordered list of a single element type |

### Custom Types

Modules define additional immutable record types in the module manifest. Each type has a fixed set of named, typed properties. Custom types are value types with member equality: two instances are equal if and only if all their properties are equal.

```yaml
# In the module manifest
types:
  player:
    properties:
      name: string
      hearts: int
      vp: int
  round_result:
    properties:
      winner: player
      score: int
```

Custom types can nest other custom types as property types.

**Creating instances** — record literal syntax in expressions:

```
{ name: nameA, hearts: 3, vp: 0 }
```

**Property access** — dot notation in both expressions and string formatting:

```
current_player.hearts + 1
all_players[0].name
```

**Equality** — custom types support `==` and `!=` via member equality. All other comparison operators are not supported on custom types directly; compare individual properties instead.

**Arrays of custom types** are declared with an `items` qualifier:

```yaml
variables:
  all_players: { type: array, items: player, default: [] }
```

Array operations (`shuffled`, `toSorted`, `except`, etc.) work with arrays of custom types. `toSorted` takes the property name to sort on; `except` uses member equality.

---

## 3. String Formatting

The `value` field on `text` nodes, and any field containing user-visible text, supports inline formatting tokens. These tokens are resolved at render time from the current variable scope.

### Inline Style Markup

| Markup | Meaning |
|---|---|
| `**...**` | Bold span |
| `_..._` | Italic span |

```yaml
- type: 'text'
  value: '**Glory and Recognition**'

- type: 'text'
  value: 'Turn to **The Cost of Disease** section. _(All tied players gain this bonus.)_'
```

Delimiters must not have whitespace immediately inside them — `**bold **` (space before the closing
`**`) or `** bold**` (space after the opening `**`) aren't recognized as emphasis by a standard
markdown parser and render as literal asterisks. Put the space outside instead: `**bold** ` /
` **bold**`.

### Variable References

`{varName}` — resolved from the current scope (session variables and let variables).

`{varName.property}` — property access on a custom-typed variable.

```yaml
- type: 'text'
  value: 'Round {round} — Leader: {all_players[0].name} with {all_players[0].vp} VP'
```

### Array Element Access

`{arr[N]}` — resolves the element at 0-based index `N`. Supports C# range index syntax for from-end access:

| Syntax | Meaning |
|---|---|
| `{arr[0]}` | First element |
| `{arr[1]}` | Second element |
| `{arr[^1]}` | Last element |
| `{arr[^2]}` | Second-to-last element |

Property access chains through array indexing: `{all_players[0].name}`.

### Inline Icons

`{icon:slug}` — resolved to the visual asset identified by `icon://slug`.

```yaml
- type: 'text'
  value: 'All players take all their {icon:s3_weapontoken} tokens into their hands.'
```

### i18n String References

`restext://Key` — a reference to a locale string stored in the module's `.restext` file. All `restext://` URIs are resolved at **module load time** by substring replacement on every string field in every node, applied after YAML parsing. Only the URI token itself is replaced; surrounding expression syntax is preserved.

```yaml
- type: 'text'
  value: 'restext://BattleTime_001' # "**Glory and Recognition**"
```

The `# "..."` comment is an inline preview for human readers; the engine ignores it.

`restext://` URIs can appear within expression strings. The URI token is replaced in-place; surrounding expression quotes are untouched. Because the substituted value is spliced into a double-quoted string literal, any `"` character in the resolved value must be escaped as `\"` so the containing expression remains syntactically valid:

```yaml
# After load-time substitution: warwinner == "Separatists"
- if: warwinner == "restext://Common_026"  # en-US.restext:33
# After load-time substitution: Separatists
- match: restext://Common_026              # en-US.restext:33
# If Common_099 resolved to: She said "hello"
# the substituted expression becomes: notice == "She said \"hello\""
```

#### Locale Resource File (`.restext`)

Each module ships an `en-US.restext` file (and optionally other locale files) in the same directory as its passage files. Format:

```restext
# 00072-BattleTime.mws.yaml      — passage comment (informational)
BattleTime_001=**Glory and Recognition**
BattleTime_002=I alone deserved recognition for my glorious, bombastic...

# Cross-passage common strings
Common_026=Separatists
Common_027=Unified Monarchists
```

**Rules:**
- One `Key=Value` entry per line, single-line values only; keys are alphanumeric with underscores (`[A-Za-z0-9_]+`)
- Lines starting with `#` are comments; blank lines are ignored
- Keys are case-sensitive; no spaces around `=`

---

## 4. String and Expression Encoding

Fields in MWS nodes carry either a **string value** or an **expression value**. The engine determines which by inspecting the field content at load time.

### String values

Any value that does not match the expression rule below is a string. Strings support:
- Plain text: `PassageId` or `Hello world`
- `restext://Key` — a locale key replaced at load time (see §3)
- `{varName}` — template interpolation: the variable's current value is spliced in (see §3)
- Multiple placeholders in one string: `{first} of {total}`

In YAML, string values should be written with single-quote delimiters so that `{` and `restext://` are never mistaken for YAML special syntax. Use `''` to represent a literal single quote inside a single-quoted YAML string.

```yaml
value: 'Just a string'
value: 'A {adjective} template string'
label: 'String with ''quotes'''
value: 'restext://Example_Resource'
```

### Expression values

A field value that is a YAML string starting with `${` and ending with `}` is an **expression**. The engine strips the `${` / `}` wrapper and evaluates the contents using the expression language (§4.1). The result is the field's runtime value.

```yaml
target: '${nextPassage}'                       # single variable
target: '${count + 1}'                         # arithmetic
onclose: '${a == "x" ? "PathA" : "PathB"}'    # conditional
onclose: '${nomore == 1 ? th < players ? "TownHallS1" : "TakeSides2" : round == 1 ? "TakeSides" : "TakeSides2"}'
```

String literals *inside* an expression use C#-style double quotes (`"`), not single quotes. A single quote inside an expression string literal is `''` (doubled, because the whole field is YAML single-quoted). A double quote inside an expression string literal uses the standard `\"` escape.

Fields that support expression values: `link.target`, `popup.target`, `goto.target`, and every audio
URI field — `audio.music`, `audio.on_display` (passage header), `audio.music`/`audio.open`/
`audio.okay`/`audio.cancel` (`popup`), `audio.click` (`link`), and `audio_track.asset`. Unlike
`target` fields (resolved at follow/close time, since `onclick`/`onclose` may run first), audio
fields resolve eagerly at render time, the same way `goto`/`include_passage` targets do — there's no
equivalent "run onclick first" step for an audio value. This is what lets e.g. voice-gender selection
be expressed inline: `music: '${voiceGender == "female" ? "audio://vo/intro_f" : "audio://vo/intro_m"}'`.
Module-manifest audio fields (§9) do **not** support expressions — a manifest is parsed once, before
any session exists, so there's no variable scope to evaluate against.

### `let` / `assign` expr fields

The `expr:` field on `let` and `assign` nodes is *always* an expression — no `${}` wrapper is used. String literals within `expr:` use C#-style double quotes, and the whole YAML field is single-quoted.

```yaml
- type: 'assign'
  var: 'wolves'
  expr: '"evil"'

- type: 'let'
  var: 'chosen'
  expr: '[nameA, nameB].shuffled("BattleTime_4")[0]'
```

---

## 4.1. Expression Language (reference)

Expressions appear in `let / expr`, `assign / expr`, conditional `if` fields, and any field that accepts `'${expression}'` values (see §4).

### Literals

| Kind | Syntax | Examples |
|---|---|---|
| Integer | Unquoted number | `0`, `42`, `-3` |
| String | Double-quoted | `"yes"`, `"Biology"` |
| Boolean | Keywords | `true`, `false` |
| Array | `[item, ...]` | `[1, 2, 3]`, `["a", "b"]` |
| Record | `{ prop: value, ... }` | `{ name: nameA, hearts: 3, vp: 0 }` |

String literals in condition expressions may be `restext://` URIs when the string matches a locale resource value. After load-time substitution the URI is replaced with the locale string before evaluation:

```yaml
# warwinner holds "Separatists" (assigned from a shuffled restext array)
# load-time substitution: warwinner == "restext://Common_026" → warwinner == "Separatists"
- if: warwinner == "restext://Common_026"  # en-US.restext:33
```

#### String Literal Interpolation

A double-quoted expression string literal supports the same `{expr}` placeholder syntax as
display-text templates (§3 Variable References / Array Element Access) — each `{...}` is evaluated
as a full expression against the current variable scope and the stringified result is spliced in.
Unlike display text, `{icon:slug}` inside an expression string literal is left untouched (icon
resolution happens later, at text-render time, not during expression evaluation) — everything else
about the two mechanisms is identical, including that indexing/property-access chains and even
arithmetic are valid inside the braces, not just a bare variable name.

This is what makes a combining assign — building one variable's value out of several pieces —
directly expressible as a template, instead of a hand-built `+` concatenation:

```yaml
- type: 'assign'
  var: 'newspaper'
  expr: '"The {townname} {newspapername}"'
```

Combined with a `restext://` reference (see §3 i18n String References), the *whole* combining
template becomes a translatable resource, not just its individual pieces — a locale can restructure
word order, drop/add an article, etc., since the template is resolved from `.restext` before
`{townname}`/`{newspapername}` are interpolated:

```yaml
- type: 'assign'
  var: 'newspaper'
  expr: '"restext://Common_012"'  # en-US.restext: The {townname} {newspapername}
```

Interpolation is re-evaluated on every evaluation of the expression (not baked in once at parse
time), so it always reflects the current value of whatever variables it references — the same way
a display-text template re-resolves on every render.

### Variable References

Plain identifiers refer to the current scope. Dot notation accesses properties on custom-typed values:

```
round
wolves
current_player.hearts
all_players[0].vp
```

### Operators

**Math** — integers only; integer divide and modulo:

```
a + b    a - b    a * b    a / b    a % b
```

**Comparison:**

```
a == b    a != b    a < b    a <= b    a > b    a >= b
```

Custom types support only `==` and `!=` (member equality). Compare individual properties for ordering.

**Logic:**

```
a && b    a || b    !a
```

**Ternary conditional:**

```
cond ? whenTrue : whenFalse
```

Right-associative, so a chain reads as if/else-if: `a ? "X" : b ? "Y" : "Z"` means `a ? "X" : (b ? "Y" : "Z")`. Only the taken branch is evaluated (short-circuits, like `&&`/`||`). Valid anywhere a full expression is — including nested inside `(...)`, function-call arguments, array elements, and record property values.

Precedence (high to low): `!` → `* / %` → `+ -` → `< <= > >= == !=` → `&&` → `||` → `?:`

Use parentheses to override.

**String concatenation:**

```
str + other
```

### Index and Range Syntax

Uses C# range semantics:

| Syntax | Meaning |
|---|---|
| `arr[0]` | First element |
| `arr[^1]` | Last element |
| `arr[^2]` | Second-to-last element |
| `arr[1..3]` | Elements at indices 1 and 2 (exclusive upper) |
| `arr[1..]` | All elements from index 1 onward |
| `arr[..^1]` | All elements except the last |
| `str[2]` | Character at position 2 (returned as string) |
| `str[1..4]` | Substring from index 1 to 3 |

### Array Operations

All array operations are **immutable** — they return a new value and do not modify the source.

| Operation | Description |
|---|---|
| `arr.count()` | Number of elements |
| `arr[index-or-range]` | Element access or slice |
| `arr.shuffled(seed_key)` | A new array with elements in random order |
| `arr.toSorted(dir)` | A new sorted array; `dir` is `"ascending"` or `"descending"` |
| `arr.toSorted(dir, property)` | Sort an array of custom types by a named property |
| `arr.except(value)` | New array with all occurrences of `value` removed (member equality for custom types) |
| `arr.except(other_arr)` | New array with all elements in `other_arr` removed |
| `arr.countif(pattern)` | Count of elements matching a pattern string |
| `[item1, item2, ...]` | Array literal |
| `[..arr]` | Spread — used in array literals: `[item0, ..arr, itemN]` |

### String Operations

All string operations are **immutable**.

| Operation | Description |
|---|---|
| `str.length()` | Number of characters |
| `str.contains(substr)` | `true` if `str` contains `substr` |
| `str.toLower()` | Lowercase copy |
| `str.toUpper()` | Uppercase copy |
| `str.replace(find, with)` | Replace all occurrences of `find` with `with` |
| `str.substr(start)` | Substring from `start` to end |
| `str.substr(start, end)` | Substring from `start` to `end` (exclusive) |
| `str[index-or-range]` | Character or substring |
| `str + other` | Concatenate two strings |

### Built-in Functions

| Function | Description |
|---|---|
| `rand_between(min, max, seed_key)` | Random integer in `[min, max]` inclusive |
| `max(a, b, ...)` | Maximum of a variadic list of integers |
| `min(a, b, ...)` | Minimum of a variadic list of integers |
| `parseInt(str)` | Parse a string-typed variable as an integer for use in arithmetic |

`seed_key` is a string literal that uniquely identifies a random call within the module. The engine uses it to derive a stable PRNG offset from the master seed.

### Special Target Sentinels

A `target` field (`link`, `popup`, `goto`) can resolve to one of two reserved sentinel values instead of an ordinary `passage_id`. Both are fixed strings the engine recognizes directly — not something a module can define or extend.

| Sentinel | Form | Meaning |
|---|---|---|
| `module::entrypoint` | Expression: `target: '${module::entrypoint}'` | Resolves to the loaded module's own `Begins-Here` passage. For a shared asset-pack flow (e.g. onboarding) whose final `goto`/navigation needs to hand off into whichever module pulled it in, without hardcoding a passage id it can't know in advance. |
| `app::gameover` | Plain literal: `target: 'app::gameover'` | Signals that this playthrough is complete — not a passage reference at all. Following a link or closing a popup with this as the resolved target does not navigate anywhere; instead the engine stops and the App takes over: it deletes the module's autosave, records the playthrough's memory (TBD — not yet implemented), and returns the player to the main menu. Any `onclick`/`onclose` logic before this (e.g. a `record` achievement trigger) still runs and commits normally — only the navigation itself is replaced. |

`app::gameover` is a plain literal (no `${}` wrapper) since it isn't an expression to evaluate, just a fixed sentinel a module places directly as a `target:` value — typically on the `okay` path of a final "ending unlocked" popup.

```yaml
- type: 'popup'
  layout: 'game_complete'
  content:
  - type: 'text'
    value: 'You have unlocked **The Long Winter** ending.'
  okay: 'Close'
  target: 'app::gameover'
```

---

## 5. Pattern Strings

Patterns appear in `switch / cases[i] / match`, `arr.countif(pattern)`, and in the `match:` field of conditional arrays.

### Value Patterns

| Form | Meaning | Examples |
|---|---|---|
| `value` | Equality (implied `=`) | `"yes"`, `"Biology"`, `3` |
| `=value` | Equality (explicit) | `"=yes"` |
| `>value` | Greater than | `">3"` |
| `>=value` | Greater than or equal | `">=3"` |
| `<value` | Less than | `"<5"` |
| `<=value` | Less than or equal | `"<=2"` |
| `!=value` | Not equal | `"!=0"` |

### Property Patterns

For arrays of custom types, patterns can test a named property:

```
"property: pattern"
```

| Example | Meaning |
|---|---|
| `"hearts: >=3"` | Element's `hearts` property is ≥ 3 |
| `"vp: >0"` | Element's `vp` property is > 0 |
| `"name: =Alice"` | Element's `name` property equals `"Alice"` |

```
all_players.countif("hearts: >=3") > 1
all_players.countif("vp: >0")
```

### Switch `match:` Field

The `match:` field on a switch case uses value patterns and can also be:

| Form | Description | Example |
|---|---|---|
| Integer | Exact equality | `match: 2` |
| String | Exact equality | `match: Biology` |
| `restext://Key` | String equality via locale key | `match: restext://Common_026` |
| List | Any of these values | `match: [16, 19]` |
| Pattern string | Comparison | `match: '>4'` |

When `match:` holds a `restext://` URI, load-time substitution replaces the URI with the locale string before evaluation.

```yaml
- type: 'switch'
  on: 'players'
  cases:
  - match: '>4'
    nodes: [...]
  - match: '>3'
    nodes: [...]
  default: [...]
```

---

## 6. Node Type Reference

### `text`

Displays human-readable content.

| Field | Type | Required | Description |
|---|---|---|---|
| `value` | string | yes | Formatted text (see §3) |
| `align` | string | no | Horizontal alignment: `left`, `center`, `right`, or `justified`. Omit to use the locale default (RTL-aware) |
| `lets` | list of strings | no | Names of `let` vars consumed by this value, for editor grouping |
| `style` | string | no | Open, module-extensible visual style vocabulary, styled entirely by module CSS |

```yaml
- type: 'text'
  value: '**To Battle**'

- type: 'text'
  value: 'All players take all their {icon:s3_weapontoken} tokens into their hands.'

- type: 'text'
  value: '{_rnd_BattleTime_1}'
  lets:
  - '_rnd_BattleTime_1'

- type: 'text'
  value: 'restext://ATOW-Preparations_001'
  align: 'center'
```

---

### `image`

Displays a standalone image asset with optional size and alignment. Produced when the source script wraps a lone `<sprite>` tag in a `<size=N>` tag — the extractor converts this pattern to an `image` node rather than an inline icon within a text node.

| Field | Type | Required | Description |
|---|---|---|---|
| `asset` | string | yes | Asset URI (e.g. `icon://scenariobox3d_war`) |
| `size` | string | no | Size hint preserved as-is from the source `<size=N>` tag (units unspecified) |
| `align` | string | no | Horizontal alignment: `left`, `center`, `right`, or `justified` |
| `title` | string | no | Formatted title/alt text (see §3) — rendered as the HTML `title`/`alt` attribute |
| `style` | string | no | Open, module-extensible visual style vocabulary, styled entirely by module CSS |

```yaml
- type: 'image'
  asset: 'icon://scenariobox3d_war'
  size: '200'
  align: 'center'
  title: 'A scene of war'
```

---

### `let`

Defines a passage-scoped variable by evaluating an expression. Never persisted to session state.

| Field | Type | Required | Description |
|---|---|---|---|
| `var` | string | yes | Variable name |
| `expr` | string | yes | Expression (see §4) |

**Hoisting**: A `let` variable is in scope for the entire passage from its assignment point, including after any `conditional` or `switch` that contains it. All branches of a conditional that a `let` variable might be accessed after must define it; accessing an unset `let` variable is a runtime error.

```yaml
# Choose-one random
- type: 'let'
  var: '_rnd_BattleTime_0'
  expr: '[nameA, nameB, nameC].shuffled("BattleTime_0")[0]'

# Hoisted from switch branches — all cases define chosen, so it's safe to use after
- type: 'switch'
  on: 'players'
  cases:
  - match: '>3'
    nodes:
    - type: 'let'
      var: 'chosen'
      expr: '[nameA, nameB, nameC, nameD].shuffled("BattleTime_1")[0]'
  default:
  - type: 'let'
    var: 'chosen'
    expr: '[nameA, nameB].shuffled("BattleTime_3")[0]'
- type: 'text'
  value: '{chosen} leads this round.'
  lets:
  - 'chosen'

# Sort session array into let var (source is not modified)
- type: 'let'
  var: 'ranked'
  expr: 'all_players.toSorted("descending", "vp")'

# Record literal — create a custom type value
- type: 'let'
  var: 'snapshot'
  expr: '{ name: current_player.name, hearts: charitytotal, vp: current_player.vp }'

# Aggregate
- type: 'let'
  var: 'topScore'
  expr: 'max(scoreA, scoreB, scoreC)'
```

---

### `assign`

Writes a value to a session variable. Persistent; all `assign` nodes encountered during passage execution are bundled with the following `link` into a single timeline snapshot.

| Field | Type | Required | Description |
|---|---|---|---|
| `var` | string | yes | Session variable name |
| `expr` | string | yes | Expression (see §4) |

```yaml
- type: 'assign'
  var: 'round'
  expr: 'round + 1'

- type: 'assign'
  var: 'wolves'
  expr: '"evil"'

- type: 'assign'
  var: 'wolves'
  expr: '["evil", "good"].shuffled("WolvesEvent_0")[0]'

- type: 'assign'
  var: 'all_players'
  expr: '[..all_players, { name: nameA, hearts: 3, vp: 0 }]'

- type: 'assign'
  var: 'candidates'
  expr: 'candidates[..^1]'
```

---

### `link`

A player-clickable link that navigates to another passage. Bundles preceding `assign` nodes into a timeline snapshot. Disabled by the app until every `input` currently rendered in the passage has a valid value (see §6 `input`) — clicking it commits all of those input values to their bound variables before running `onclick` and resolving `target`, as part of the same snapshot.

| Field | Type | Required | Description |
|---|---|---|---|
| `label` | string | yes | Link text (string formatting — see §3) |
| `style` | string | no | Open, module-extensible visual style vocabulary, styled entirely by module CSS (e.g. `link`, `button`) |
| `target` | string | no | Destination `passage_id`, or `'${expression}'` for a runtime-computed target (see §4). Optional when every reachable path through `onclick` is guaranteed to hit a `goto`, which preempts this — omitting both would leave the link with nothing to navigate to |
| `snapshot` | bool or string | no | Whether following this link creates a timeline snapshot. Defaults to `false` if absent. A string value means `true` *and* sets the timeline scrubber's label to that string in one step — overriding the destination passage's own `title` (the default label when `snapshot` is a bare `true`). A preempting `goto` inside `onclick` takes priority over this label — see `goto` below |
| `onclick` | list | no | `let`, `assign`, and `conditional` nodes executed on click before navigation. A `goto` among these preempts `target` |
| `audio` | object | no | Click-sound override — see §6 "Audio" below |

**Execution order when `onclick` is present:** on click, pending `input` values are committed first, then the engine executes all nodes in the `onclick` list, then evaluates `target` (unless a `goto` inside `onclick` fired, which preempts it). This matters when `target` is an expression referencing a variable and an `onclick` entry may assign that variable — the variable is resolved after the assignments run, not at render time.

**Omitting `target`:** a link whose only purpose is to run `onclick` logic that always ends in a `goto` — e.g. an exhaustive `conditional` where every branch's last node is a `goto` — can omit `target` entirely. If neither a preempting `goto` nor `target` resolves a destination, following the link runs its `onclick` effects against the live store and then does nothing else (mirrors a `popup` with neither `target` nor `onclose` set — see above).

```yaml
- type: 'link'
  label: 'restext://BattleTime_010' # "Click to begin the battle..."
  target: 'BattleCompleteReturn'
  snapshot: true

# With a custom timeline label — the string form of `snapshot` implies true
- type: 'link'
  label: 'Confront the mayor'
  target: 'Confrontation'
  snapshot: 'You chose to confront him'

# With inline state change — target is a literal passage_id
- type: 'link'
  label: '2 Players'
  target: 'PlayerNameIntro'
  snapshot: true
  onclick:
  - type: 'assign'
    var: 'players'
    expr: '2'

# With dynamic target — onclick may assign the target variable before navigation
- type: 'link'
  label: '{nameA}'
  target: '${feverheartnextPsg}'
  snapshot: true
  onclick:
  - type: 'assign'
    var: 'charity'
    expr: 'nameA'
  - type: 'conditional'
    if: 'feverheartnextPsg == "" || feverheartnextPsg == 0'
    then:
    - type: 'assign'
      var: 'feverheartnextPsg'
      expr: '["S5Fate1", "S5Fate2"].shuffled("?")[0]'
```

---

### `popup`

A modal overlay, either click-triggered (has `label`) or auto-displayed when the passage renders (no `label`, layout controls display). Content is evaluated eagerly, alongside the rest of the passage, against a sandboxed copy of the variable store — never the live one — so it may safely contain `assign` and `input` nodes (including a self-contained input flow: an `input` node plus an `okay` button) without those mutations reaching live state before the player actually accepts.

Okay/Cancel dismissal mirrors `link`'s own `target`/`onclick` shape: `onclose` runs first (its own `goto`, if any, preempts `target`), then `target` resolves the destination. `target` and `onclose` are both independently optional even when `okay` is present — an Okay button with neither still commits any pending `input` values in `content`, but otherwise just closes the popup in place with no navigation and no further engine round-trip (e.g. a purely informational popup that only needs an acknowledgement button).

| Field | Type | Required | Description |
|---|---|---|---|
| `label` | string | no | Button/link text that triggers the popup; omit for auto-display (layout-driven only) |
| `style` | string | no | Open, module-extensible visual style vocabulary, styled entirely by module CSS (e.g. `link`, `button`) |
| `layout` | string | no | Named layout definition (see §8) — overrides the popup's default visual treatment |
| `header` | list | no | Optional nodes rendered in a separate structural region, before `content` — evaluated eagerly the same way. Purely structural: the format doesn't prescribe what a header contains or how it's positioned, that's entirely up to module CSS. The extractor uses this for a Cradle `setupStyle` block's image, for example, but any node list is valid |
| `content` | list | no | Content nodes for the popup body — evaluated eagerly, at passage-render time (see the popup transaction model below). For layout-driven popups, may contain `let`/`conditional`/`switch` nodes evaluated the same way to bind layout properties. May also contain another `popup` node — see the nested-popup example below |
| `okay` | string | no | Okay button label (string formatting — see §3); only rendered if present. Clicking it commits any pending `input` values in `content`, runs `onclose`, then resolves `target` |
| `cancel` | string | no | Cancel button label; only rendered if present. Clicking it discards the popup's pending state entirely — no `onclose`, no `target`, no commit |
| `onclose` | list | no | `let`, `assign`, and `conditional` nodes run when Okay is clicked, before `target` is resolved — same shape and timing as `link.onclick`. A `goto` among these preempts `target` |
| `target` | string | no | Destination when Okay is clicked (unless preempted by a `goto` in `onclose`) — `passage_id` or `'${expression}'` (see §4) |
| `snapshot` | bool or string | no | Whether closing this popup via Okay creates a timeline snapshot. Defaults to `false` if absent. A string value means `true` *and* sets the timeline scrubber's label to that string in one step — overriding the destination passage's own `title` (the default label when `snapshot` is a bare `true`). A preempting `goto` inside `onclose` takes priority over this label — see `goto` above |
| `audio` | object | no | Background-music override while this popup is open, plus open/okay/cancel sound overrides — see §6 "Audio" below. A popup's `audio.music` only takes effect once it's actually open — an unopened popup never affects background music, matching how showing/hiding a popup is otherwise a pure display toggle |

**Popup transaction model:** the popup's `content` is rendered against a sandboxed copy of the variable store as soon as the passage renders — nothing is committed to the live store yet, and showing/hiding the popup is a pure display toggle that needs no further evaluation. Clicking Okay commits the sandbox's pending `input` values, runs `onclose` against it, merges it onto the live store, and navigates to the resolved destination, all as one transaction. Clicking Cancel discards the sandbox untouched. One trade-off: a popup's content is evaluated even if the player never opens or accepts it, so a seeded random draw inside `content` is "spent" regardless — this is safe because nothing else can mutate the live store while a popup sits unopened on an already-rendered passage.

Standard popup with body content and an Okay button:
```yaml
- type: 'popup'
  label: 'Setup Instructions'
  content:
  - type: 'text'
    value: 'Place the hospital token on space 1 of the hospital track.'
  okay: 'Begin'
  target: 'Hospital2'
  snapshot: true
```

Popup collecting a value via an `input` inside its content — guarded so it only auto-displays once (see the synthetic `{var}_submitted` pattern the extractor uses for this shape):
```yaml
- type: 'conditional'
  if: '!feverheart_submitted'
  then:
  - type: 'popup'
    content:
    - type: 'text'
      value: 'Enter total hearts collected...'
    - type: 'input'
      label: 'Total hearts'
      var: 'feverheart'
    okay: 'Continue'
    target: 'Feverheart'
    snapshot: true
    onclose:
    - type: 'assign'
      var: 'feverheart_submitted'
      expr: 'true'
```

Layout-driven popup — body owned by the layout, no content nodes:
```yaml
- type: 'popup'
  layout: 'setup'
  label: 'restext://S4Kill2_006'  # "Click to start the vote..."
  target: 'S4Kill3'
```

Nested popup — a `popup` node inside another popup's own `content:`. This is how a
countdown-then-reveal interaction (the reference app's `ViewBiddingSystem`, formerly special-cased
as the `voting`/`bidding` layout values) is built: the outer popup is a pure instructional
container with no `okay`/`cancel` of its own (only ever carried away by the inner popup's own
navigation), and the inner popup's own `label` renders as an ordinary trigger button *inline within
the outer's content* — see `Masterwork-Modules/my-fathers-work-template`'s
`countdown_instructions`/`countdown_action` layouts for the full worked example, including the CSS
that makes the inner popup's Okay button cover the full viewport, invisibly, until a countdown
animation finishes. Nesting needs no engine support beyond what already exists — a popup's
`content:` is rendered through the same node-list machinery as everything else, so a nested
popup's own sandboxed store just clones from its parent's, the same way the parent's clones from
the live store:
```yaml
- type: 'popup'
  layout: 'countdown_instructions'
  label: 'restext://S5Special1a_008'  # "click to start the bid..."
  content:
  - type: 'text'
    value: 'restext://Bidding_Instructions'
  - type: 'popup'
    layout: 'countdown_action'
    label: 'restext://Bidding_StartButton'  # "START BIDDING"
    content:
    - type: 'text'
      value: 'restext://Countdown_Three'
      style: 'countdown-3'
    okay: 'restext://Countdown_Reveal'
    target: 'S5Special1b'
```

Layout-driven popup with property bindings — content nodes bind values to layout properties at open time:
```yaml
- type: 'popup'
  layout: 'end_of_generation'
  label: 'restext://Common_190'  # "Click here at the end of the round..."
  snapshot: true
  target: 'ATOWSabotageIntro1'
  okay: 'Close'
  content:
  - type: 'let'
    var: 'title'
    expr: '"restext://Martial1_022"'
  - type: 'let'
    var: 'completedRound'
    expr: '-1'
  - type: 'text'
    value: 'restext://Common_069'    # body instruction text
```

Auto-display layout popup — no `label`, shown immediately when the passage renders. Always carries an
`okay` button even with no `target`/`onclose` of its own — without one there's no way to dismiss it,
since a popup with neither `Okay` nor `Cancel` renders no footer at all:
```yaml
- type: 'popup'
  layout: 'end_of_generation'
  okay: 'Confirm'  # reference app's own button caption (Main.unity ViewEndOfGeneration Accept button)
  content:
  - type: 'text'
    value: 'restext://TowardsWar_002'  # EOG instruction text
  - type: 'let'
    var: 'generation'
    expr: '3'
```

End-of-round acknowledgement popup — the extractor synthesizes this from a `PassageTracker.instance.
CheckProgress(current, next)` call site whose `current` passage has curated body text in
`--progress-map` (see `docs/extractor.md`), replacing what would otherwise be a bare navigation link.
The reference app (`ViewEndOfRound.SetEndOfRound`) shows this popup before advancing to `next`; the
`_ProgressRound` assign that used to sit directly in the link's `onclick` moves into `onclose` instead,
since it should only commit once the player has acknowledged the popup:
```yaml
- type: 'popup'
  layout: 'end_of_round'
  label: 'restext://Fever1_017'  # "Click here to continue to the next round..."
  target: 'FeverServe1'
  okay: 'restext://Common_008'   # "End of Round" (reference app's own button caption)
  snapshot: true
  onclose:
  - type: 'assign'
    var: '_ProgressRound'
    expr: '1'
  content:
  - type: 'text'
    value: 'restext://Fever1_019'   # "The Early Years of the First Generation has ended..."
  - type: 'text'
    value: 'restext://Common_010'   # "Then, perform all Start of Round actions..."
```

---

### `input`

A player-fillable field, rendered inline wherever it appears — directly in a passage, or inside a `popup`'s `content`. Implicitly required for text/number fields: each starts empty and editable when live; when the timeline is rewound to view history, it shows disabled and populated from that snapshot instead. A boolean field has no empty state — unchecked (`false`) is itself a valid value, so it's always considered filled, whether or not the player has touched it. There is no `onsubmit`/submit action of its own — any `link` in the same passage (or a popup's `okay` button) stays disabled until every currently-showing text/number `input` has a valid value (boolean inputs never block it), and following it commits all of them to their bound variables as part of its own snapshot (see §6 `link`/`popup`).

| Field | Type | Required | Description |
|---|---|---|---|
| `label` | string | no | Formatted label shown inline with the field (string formatting — see §3). Omit when a module renders the visible label itself as a separate `text` node beside the field instead |
| `style` | string | no | Open, module-extensible visual style vocabulary, styled entirely by module CSS |
| `var` | string | yes | Session variable to receive the value |
| `min` | int | no | Minimum accepted value. Only meaningful when `var`'s declared type is numeric |
| `max` | int | no | Maximum accepted value. Only meaningful when `var`'s declared type is numeric |

The field's value type — text, number, or checkbox — is **not** declared on the node — it's derived from `var`'s own declared type in the module's variable manifest: an integer-typed variable gets a number field, a boolean-typed variable gets a checkbox, anything else gets a text field. There's nowhere a mismatch between the two could come from, since they're the same value.

```yaml
- type: 'text'
  value: 'Count up all heart tokens collected by ALL players. Enter the total here.'
- type: 'input'
  label: 'Total hearts collected'
  var: 'charitytotal'
  min: 0
- type: 'link'
  label: 'Continue'
  target: 'Feverheart'
  snapshot: true
```

---

### `goto`

Unconditional navigation — no player interaction. A plain top-level `goto` never creates a timeline
snapshot. A `goto` placed inside a `link`'s `onclick` or a `popup`'s `onclose` is the one exception:
it preempts that action's own `target`, and its own `snapshot` field — if explicitly set — overrides
whether the resulting navigation creates a snapshot *at all* (not just its label), taking priority
over the enclosing `link`/`popup`'s own `snapshot`. This is what lets a single link branch
differently per outcome: e.g. a `conditional` inside `onclick` where one branch's `goto` forces a
snapshot and a sibling branch's forces none, even though both share the same enclosing link (see the
example below). Omitting `snapshot` on the `goto` means "inherit whatever the enclosing `link`/
`popup`'s own `snapshot` says" — the historical default, and the only behavior a plain top-level
`goto` has, since it has no enclosing action to override.

| Field | Type | Required | Description |
|---|---|---|---|
| `target` | string | yes | Destination `passage_id`, or `'${expression}'` resolving to one (see §4) |
| `snapshot` | bool or string | no | Overrides whether this navigation creates a timeline snapshot, when this `goto` fires from within a `link`'s `onclick` or a `popup`'s `onclose`. Absent means "inherit the enclosing action's own `snapshot`". A string value means `true` *and* sets the timeline scrubber's label to that string, taking priority over the enclosing `link`/`popup`'s own label. No effect on a plain top-level `goto` |

```yaml
- type: 'goto'
  target: 'PlayerNameIntro'

- type: 'goto'
  target: '${endingPassage}'
```

For conditional routing, wrap in `conditional`:

```yaml
- type: 'conditional'
  conditions:
  - if: 'wolves == "evil"'
    then:
    - type: 'goto'
      target: 'BonusPath'
  else:
  - type: 'goto'
    target: 'NeutralPath'
```

**Per-branch snapshot override** — the same enclosing link routes to either a bookmarked passage or
a transient intermediate one, decided at click time:

```yaml
- type: 'link'
  label: 'Continue'
  onclick:
  - type: 'conditional'
    if: 'winnerCount == 1'
    then:
    - type: 'goto'
      target: 'Ranking'
      snapshot: true       # a genuine decision point — worth its own timeline entry
    else:
    - type: 'goto'
      target: 'TieBreaker1' # an automatic in-between step — no bookmark of its own
      snapshot: false
```

A non-state-affecting `goto` like the `TieBreaker1` branch above still fully navigates there — it
just doesn't create a timeline entry. The player isn't stuck with no way back to it, though:
stepping back from anywhere further down the same non-snapshotted chain returns to the *last true
snapshot* (here, wherever `Ranking`'s link was originally reached from) in one step, skipping the
transient passages in between; stepping forward (or "return to present") goes straight back to
wherever the player actually was, without replaying them. See `Masterwork.Engine.GameSession`'s own
remarks on `StepBack`/`StepForward`/`JumpToPresent` for the exact mechanics.

---

### `section`

A visually-distinct content container. Optionally titled and collapsible.

| Field | Type | Required | Description |
|---|---|---|---|
| `title` | string | no | Section heading (string formatting — see §3) |
| `collapsed` | bool | no | If `true`, renders collapsed; player can expand. Default: `false` |
| `style` | string | no | Open, module-extensible visual style vocabulary, styled entirely by module CSS (e.g. `panel`, `well`, `quote`, `setup`) |
| `content` | list | yes | Content nodes |

```yaml
- type: 'section'
  title: '**Board of Trustees**'
  content:
  - type: 'text'
    value: 'Each player may now purchase one building from the market.'
  - type: 'link'
    label: 'Continue...'
    target: 'BuildPhase2'
    snapshot: true

- type: 'section'
  style: 'setup'
  content:
  - type: 'text'
    value: '**SETUP**'
  - type: 'break'
    style: 'paragraph'
  - type: 'text'
    value: 'Place the hospital token on space 1 of the hospital track.'
```

---

### `conditional`

Evaluates conditions in order; renders the first matching branch, or the `else` list if none match.

Two forms are used depending on the number of branches:

**Flat form** — exactly one if-branch, with an optional `else`: condition and body sit directly on the node, no `conditions:` wrapper needed.

| Field | Type | Required | Description |
|---|---|---|---|
| `if` | string | yes | Condition expression (see §4) |
| `then` | list | yes | Nodes rendered when the condition is true |
| `else` | list | no | Nodes rendered when the condition is false |

```yaml
- type: 'conditional'
  if: 'round >= 3'
  then:
  - type: 'assign'
    var: 'phase'
    expr: '"late"'

- type: 'conditional'
  if: 'nameA == ""'
  then:
  - type: 'text'
    value: 'Enter your name.'
  else:
  - type: 'text'
    value: 'Welcome back, {nameA}.'
```

**Multi-branch form** — two or more if-branches: branches are in a `conditions` list.

| Field | Type | Required | Description |
|---|---|---|---|
| `conditions` | list | yes | Ordered list of `if`/`then` branch objects |
| `conditions[i].if` | string | yes | Condition expression (see §4) |
| `conditions[i].then` | list | yes | Nodes rendered when this branch is taken |
| `else` | list | no | Nodes rendered when no branch matches |

```yaml
- type: 'conditional'
  conditions:
  - if: 'players >= 3'
    then:
    - type: 'text'
      value: 'Three or more players.'
  - if: 'players == 2'
    then:
    - type: 'text'
      value: 'Two players.'
  else:
  - type: 'text'
    value: 'Solo game.'
```

---

### `switch`

Tests a single variable against a set of cases (see §5 for match patterns).

| Field | Type | Required | Description |
|---|---|---|---|
| `on` | string | yes | Variable name to test |
| `cases` | list | yes | Ordered list of match/nodes case objects |
| `cases[i].match` | pattern | yes | Value(s) to match (see §5) |
| `cases[i].nodes` | list | yes | Nodes rendered when this case is taken |
| `default` | list | no | Nodes rendered when no case matches |

---

### `foreach`

Iterates over an array variable, rendering `do` once per element.

| Field | Type | Required | Description |
|---|---|---|---|
| `var` | string | yes | Loop variable name; in scope within `do` |
| `in` | string | yes | Name of the array variable to iterate |
| `do` | list | yes | Node list rendered for each element |

```yaml
- type: 'foreach'
  var: 'winner'
  in: 'winners'
  do:
  - type: 'text'
    value: '{winner.name} gains 5VP.'
  - type: 'break'
```

---

### `include_passage`

Embeds another passage's content inline.

| Field | Type | Required | Description |
|---|---|---|---|
| `target` | string | yes | `passage_id` to include, or `'${expression}'` resolving to one (see §4) |

---

### `record`

Records that the player has reached a named milestone. References an achievement defined in the module manifest (see §9).

- If the achievement is **boolean**, it is unlocked (if not already).
- If the achievement is **threshold**, the counter is incremented by 1 (up to the threshold; already-met thresholds are not incremented again).

| Field | Type | Required | Description |
|---|---|---|---|
| `id` | string | yes | Achievement identifier as defined in the module manifest |

```yaml
- type: 'record'
  id: 'ending_wolves_evil_1'

- type: 'record'
  id: 'endings_discovered'
```

---

### `break`

A line break within a content block.

| Field | Type | Required | Description |
|---|---|---|---|
| `style` | string | no | Open, module-extensible visual style vocabulary, styled entirely by module CSS |

```yaml
- type: 'break'
```

A paragraph separator (a larger visual gap than a plain break) is a `break` with `style: 'paragraph'` — module CSS decides what `style-paragraph` actually looks like; the engine doesn't distinguish them structurally.

```yaml
- type: 'break'
  style: 'paragraph'
```

---

### `checkpoint`

A named milestone in the timeline. Creates a labeled marker in the scrubber. Also used to mark the boundary of a generation phase.

| Field | Type | Required | Description |
|---|---|---|---|
| `id` | string | yes | Stable checkpoint identifier |
| `display` | string | no | Human-readable label in the timeline scrubber |
| `diagnostic` | string | no | Machine-readable label for test assertions |

```yaml
- type: 'checkpoint'
  id: 'generation_2_complete'
  display: 'Generation 2 Complete'
  diagnostic: 'gen2_end'
```

A generation-end sequence uses `section` for the summary and `checkpoint` for the boundary:

```yaml
- type: 'section'
  style: 'panel'
  content:
  - type: 'text'
    value: '**End of Generation 2**'
  - type: 'break'
    style: 'paragraph'
  - type: 'text'
    value: 'Resolve all pending experiments before continuing.'
- type: 'checkpoint'
  id: 'generation_2_complete'
  display: 'Generation 2'
- type: 'link'
  label: 'Continue to Generation 3'
  target: 'Gen3Start'
  snapshot: true
```

---

### `audio_track`

An in-passage audio playback element with a complete, module-styleable player UI (title, play/pause,
scrubber, current/total time) — content, not an override on something that already exists (compare
the `audio:` block described below). The first narration-capable node.

| Field | Type | Required | Description |
|---|---|---|---|
| `asset` | string | yes | Asset URI, typically `audio://vo/...` (see §8), or `${expr}` (see §4) — the track this element plays and lets the player scrub |
| `title` | string | no | Formatted display label shown alongside the playback controls (string formatting — see §3) |
| `style` | string | no | Open, module-extensible visual style vocabulary, styled entirely by module CSS |
| `autoplay` | bool or int | no | `true` (default) starts playback as soon as the element renders; `false` waits for the player to press play; an integer is a millisecond delay before autoplay begins |
| `bgm_behavior` | string | no | How background music behaves while this track is actually playing: `pause` (default), `duck`, or `none`. Has no effect before playback actually starts |

```yaml
- type: 'audio_track'
  asset: '${voiceGender == "female" ? "audio://vo/battletime_narration_f" : "audio://vo/battletime_narration_m"}'
  title: 'restext://BattleTime_NarrationTitle'  # "Listen to the Call to Arms"
  autoplay: false
  bgm_behavior: 'duck'
```

---

### Audio (`audio:` block on passage/popup/link)

The passage header, `popup`, and `link` nodes each accept an optional nested `audio:` mapping —
background-music and sound-effect overrides, kept nested rather than flat so a URI and its delay-ms
sibling stay grouped, and so this block reads distinctly from the unrelated `audio_track` node type
above.

| Field | Type | Required | Description |
|---|---|---|---|
| `audio.music` | string or `${expr}` | no | Background-track override — passage header and `popup` only. Absent means inherit from the module default (see §9); present-but-empty (`''`) means explicit silence while this passage/popup is topmost |
| `audio.on_display` | string or `${expr}` | no | Passage header only — SFX fired once when this passage becomes the active passage |
| `audio.on_display_delay_ms` | int | no | Passage header only — delay before `on_display` plays. Default `0` |
| `audio.open` / `audio.okay` / `audio.cancel` | string or `${expr}` | no | `popup` only — override the module's `popup_open`/`popup_close` SFX defaults (see §9); `okay` and `cancel` are independently overridable even though the module tier shares one `popup_close` bucket for both |
| `audio.open_delay_ms` / `audio.okay_delay_ms` / `audio.cancel_delay_ms` | int | no | `popup` only — delay before the matching sound plays. Default `0` |
| `audio.click` | string or `${expr}` | no | `link` only — overrides the module's `click` SFX default |
| `audio.click_delay_ms` | int | no | `link` only — delay before `click` plays. Default `0` |

**Resolution — "topmost active element wins":** at any instant, the effective background music is
resolved by walking outward from the deepest currently-open popup, through any enclosing popups, to
the current passage, to the module default (§9) — the first tier with a *present* `audio.music` key
wins (empty = silence, stop; non-empty = that value, stop; absent = check the next tier out). An
unopened popup never participates — its `audio.music` only counts once the popup is actually open.
Music changes always crossfade; there's no delay field for `music`, since a crossfade already covers
the transition. SFX fields (`on_display`, `open`, `okay`, `cancel`, `click`) are one-shot events, not
part of this stack — each just checks the node override, else the module's matching default bucket,
else silence.

**Theme audio is a separate concept**, not part of this resolution stack at all — it applies only to
the app's own pre-module screens (main menu, help, etc.) via a code-level theme contract, stopping the
instant a module loads to play and resuming when the player returns to the main menu.

---

## 7. Source Annotations

Extracted passage files include YAML comments injected by the extractor. These are informational only; the engine ignores them.

```yaml
# TheCostofDisease.cs:29543
- type: 'text'
  value: 'restext://Expedition3_001' # "**The Expedition Uncovers...**"
```

---

## 8. Module Layout/Chrome

### Layout as a structural/CSS hook

`layout` (on both passages and popups) is an open, module-extensible string. The app never
branches on its value to decide *what* to render — it always renders the same purely structural
regions (a passage's node list; a popup's `header`/`content`/`okay`/`cancel`) — the value is only
ever carried through as a `layout-{value}` CSS class for module stylesheets to target. Every layout
value — `hub`/`event`/`narration`/`introduction` on passages, `setup`/`end_of_generation`/
`end_of_round`/`reveal`/`prompt`/anything module-defined on popups — renders through the fully
generic path; all visual differentiation comes from module CSS keyed on the `layout-{value}` class
(see `Masterwork.App.Shared`'s `PassageView.razor`/`RenderedPopupView.razor`, and e.g.
`Masterwork-Modules/cost-of-disease/assets/style.css`'s `.mws-popup-overlay.layout-setup`
rules for a worked example). There is no manifest-declared `layouts:`/`popups:` registry — layout
values are just strings the extractor and a module's own CSS agree on.

`reveal` is one such extractor-emitted value (`V2Serializer.TransformPopup`'s fallback for a
click-triggered popup with no other special-purpose marker — see its own remarks): a private,
often lengthy passage of text handed to one player, ending in one or more real choice links, with
no `okay`/`target` of its own — the player leaves via whichever link they click, or backs out via
the popup's generic `cancel`. Cost of Disease's `Gen1-CreepyTrackRes` passage is a worked example.

`voting`/`bidding` used to be the one exception — a bespoke countdown-then-reveal component
(`VotingPopupContent`) rendered those two popup layout values specially, bypassing module CSS
entirely. Retired: a countdown-then-reveal interaction is fully expressible as an ordinary nested
popup (a `type: popup` node inside another popup's own `content:` — see §6's `popup` entry for the
worked example, and `Masterwork-Modules/my-fathers-work-template`'s `countdown_instructions`/
`countdown_action` layouts for the full pattern), so it needed no special-casing at all once
someone actually built it that way.

### Layout chrome (`layouts/{id}.mws.yaml`)

A module can attach optional node-list regions to a layout name, rendered around a passage's or
popup's own content without the app ever inspecting what those regions contain or what the layout
name means. One file per layout, in a `layouts/` folder alongside `passages/`/`passages-override/`:

```yaml
# layouts/hub_early.mws.yaml
format: 'mws/0.4'
layout_id: 'hub_early'
header:
- type: 'image'
  asset: 'image://progress/step{_ProgressRound}'
before_content: []
after_content: []
footer: []
```

`layout_id` is authoritative (not the filename, though they should match by convention — a mismatch
is a soft warning, not an error). All four regions — `header`, `footer`, `before_content`,
`after_content` — are optional node lists using the exact same vocabulary as a passage's own
`nodes:`, including `conditional`/`switch`/`let`/`assign`, evaluated against live variable state at
render time exactly like a passage body is. A layout name with no matching `layouts/*.yaml` file
simply renders no chrome — the normal case for most layout names.

Composition order, whether the layout name came from a passage or a popup:

```
header                                (outermost — above the passage's title/subtitle, or popup's own header)
passage's title/subtitle, or popup's own header region
before_content
passage's nodes, or popup's content
after_content
[popup's own footer: okay/cancel buttons]
footer                                (outermost — below everything else)
```

Because the lookup is unconditional, a module *can* attach chrome to `voting`/`bidding` too — but
since those two render entirely through their own bespoke component, any chrome attached there
isn't currently displayed.

Chrome logic is expected to read ordinary session variables the extractor or module content sets —
there is no engine-reserved variable name for this. For example, the Cost of Disease extractor
populates a `_ProgressRound` variable (an int, 1–9) at the passage-tracker checkpoints the reference
app uses to advance its own progress bar (see `--progress-map` in `docs/extractor.md`); a
`layouts/hub_early.mws.yaml`/`hub_middle.mws.yaml`/`hub_late.mws.yaml` chrome file is what actually turns that
into a visible progress indicator, entirely as module content — the engine and app have no built-in
notion of "a progress bar" at all.

**Timing note**: the assign lives in the `onclose` of the `layout: end_of_round` popup that replaces
the link *leaving* a round's hub passage (see §7's `popup` examples) — not on the hub passage itself,
and not committed until the player has acknowledged that popup's Okay button. So `_ProgressRound`
only advances on that transition, not on hub entry: while playing round *N*'s content (for
*N* = 1–9), the variable still holds whatever the *previous* round's transition set it to (`0` while
playing round 1, since no checkpoint has fired yet). It only reaches `9` once the player leaves round
9's hub and dismisses that popup — i.e. on the way *into* end-of-game/scoring, not while round 9 is
still being played. Chrome authored against this variable should treat it as "rounds completed so
far," not "the round currently being displayed" — e.g. a hub screen wanting to show "you are entering
round N" would need `_ProgressRound + 1`, not the raw value.

---

## 9. Module Definitions

A module is a package of passage files alongside a manifest that defines the types, variables, and achievements used across the module.

### Custom Types

```yaml
# In the module manifest
types:
  player:
    properties:
      name: string
      hearts: int
      vp: int
```

See §2 for how custom types are used in variables, expressions, and string formatting.

### Module Variables

Session variables are declared with their type and default value. They are instantiated at the start of each playthrough.

```yaml
variables:
  round:         { type: int,    default: 0 }
  wolves:        { type: string, default: "" }
  build:         { type: array,  default: [] }
  all_players:   { type: array,  items: player, default: [] }
  current_player: { type: player }
```

For an extracted module, this block normally lives in the extractor-owned `_variables.yaml` at
the module root — regenerated wholesale on every re-extraction, so hand edits there don't
survive. A module can additionally declare variables of its own in a `variables/` folder
(sibling to `passages/`/`passages-override/`/`layouts/`): zero or more `.yaml` files, each using
the exact same `variables:`/`default:` shape as `_variables.yaml`. Files load after
`_variables.yaml` and are applied with the same add-or-override-by-key semantics as
`passages-override/` and `layouts/*.yaml`: a variable name already declared in `_variables.yaml`
is replaced, a new name is added. Splitting declarations across multiple files (e.g.
`variables/scoring.yaml`, `variables/achievements.yaml`) is purely an authoring convenience —
the loader doesn't care about file names or count, just that everything under `variables/` ends
in `.yaml`.

### Achievements

Two achievement types are supported.

**Boolean** — unlocked once when a `record` node with the matching `id` is evaluated. Once unlocked, further `record` evaluations are ignored.

**Threshold** — has a target count. Each `record` evaluation increments the counter. When the counter reaches `threshold`, the achievement is unlocked. Further `record` evaluations are ignored.

Both types support:

| Field | Type | Description |
|---|---|---|
| `type` | string | `boolean` or `threshold` |
| `threshold` | int | (threshold only) Target count to unlock |
| `public` | bool | `true`: visible and described before unlock. `false`: secret — existence and description are hidden until unlocked |
| `label` | string | Display name (restext ref) |
| `description` | string | Description shown after unlock (restext ref) |
| `secret_label` | string | (optional) Label shown for secret achievements before unlock; defaults to a generic placeholder |
| `badge` | string | Asset URI for the achievement badge image |

```yaml
achievements:
  ending_wolves_evil_1:
    type: boolean
    public: false
    label: restext://ach_wolves_evil_label
    description: restext://ach_wolves_evil_desc
    badge: icon://ach_wolves_evil

  ending_wolves_good_1:
    type: boolean
    public: false
    label: restext://ach_wolves_good_label
    description: restext://ach_wolves_good_desc
    badge: icon://ach_wolves_good

  endings_discovered:
    type: threshold
    threshold: 8
    public: true
    label: restext://ach_all_endings_label
    description: restext://ach_all_endings_desc
    badge: icon://ach_all_endings
```

A passage that ends the module places both a specific achievement record and increments the collection counter:

```yaml
- type: 'record'
  id: 'ending_wolves_evil_1'
- type: 'record'
  id: 'endings_discovered'
```

### Audio

Module-level defaults for the topmost tier of the resolution stack described in §6's "Audio"
subsection — the fallback used whenever no open passage, popup, or link supplies its own override.
Unlike the node-level `audio:` fields, manifest audio fields are plain strings; they do not support
`${expr}` expression evaluation.

| Field | Type | Description |
|---|---|---|
| `audio.music.default_tracks` | array of string | Zero or more `audio://` URIs (see §6) forming the module's default background music. Zero entries means no default music; one entry plays as a single looping track; more than one plays as an auto-advancing playlist |
| `audio.music.order` | string | `sequence` (default) plays `default_tracks` in listed order, looping back to the start; `shuffle` reshuffles the playlist order each time the module-tier track becomes the active winner |
| `audio.sfx.transition` | array of string | Default sound(s) for a passage's `audio.on_display` when the passage itself doesn't override it. More than one entry means a random pick per firing |
| `audio.sfx.popup_open` | array of string | Default sound(s) for a popup's `audio.open` when the popup itself doesn't override it |
| `audio.sfx.popup_close` | array of string | Default sound(s) used for **both** a popup's `audio.okay` and `audio.cancel` when the popup itself doesn't override the respective field — there is no separate `popup_okay`/`popup_cancel` bucket at the module tier |
| `audio.sfx.click` | array of string | Default sound(s) for a link's `audio.click` when the link itself doesn't override it |

```yaml
audio:
  music:
    default_tracks:
      - 'audio://bgm/hospital_theme'
      - 'audio://bgm/hospital_theme_variant'
    order: 'sequence'
  sfx:
    transition:
      - 'audio://sfx/page_turn_1'
      - 'audio://sfx/page_turn_2'
    popup_open:
      - 'audio://sfx/popup_open'
    popup_close:
      - 'audio://sfx/popup_close'
    click:
      - 'audio://sfx/ui_click'
```

### Town Records

> **Design note (TBD):** Town Records are a persistent history of narrative events from a playthrough — "memories" the players discover that are stored per-session and persist in the module's town history across sessions. They are categorised and may be presented in a collection view. A `town_record` manifest entry defines each discoverable record (id, category, label, descriptive text); a node type (distinct from `record`) triggers writing a town record entry during play. The exact structure is deferred until the module packaging format is complete.

### Module Statistics

> **Design note (TBD):** Module statistics are aggregate counters that accumulate across all playthroughs of a module (e.g. total experiments completed, total games finished, total times a specific outcome was chosen). They are separate from per-playthrough achievements. Definition and increment mechanisms are deferred.

---

## 10. Complete Example

A passage from *A Time of War*:

```yaml
# ATimeOfWar.cs:9247
---
format: 'mws/0.4'
passage_id: 'BattleTime'
title: 'BattleTime'
layout: 'narration'
nodes:
# ATimeOfWar.cs:9251
- type: 'text'
  value: 'restext://BattleTime_001' # "**Glory and Recognition**"
- type: 'break'
# ATimeOfWar.cs:9255
- type: 'switch'
  on: 'players'
  cases:
  - match: '>4'
    nodes:
    - type: 'let'
      var: '_rnd_BattleTime_0'
      expr: '[nameA, nameB, nameC, nameD, nameE].shuffled("BattleTime_0")[0]'
  - match: '>3'
    nodes:
    - type: 'let'
      var: '_rnd_BattleTime_0'
      expr: '[nameA, nameB, nameC, nameD].shuffled("BattleTime_1")[0]'
  - match: '>2'
    nodes:
    - type: 'let'
      var: '_rnd_BattleTime_0'
      expr: '[nameA, nameB, nameC].shuffled("BattleTime_2")[0]'
  default:
  - type: 'let'
    var: '_rnd_BattleTime_0'
    expr: '[nameA, nameB].shuffled("BattleTime_3")[0]'
# ATimeOfWar.cs:9278
- type: 'let'
  var: '_rnd_BattleTime_1'
  expr: '[restext://BattleTime_002, restext://BattleTime_003, restext://BattleTime_004].shuffled("BattleTime_4")[0]'
- type: 'text'
  value: '{_rnd_BattleTime_1}'
  lets:
  - '_rnd_BattleTime_1'
- type: 'break'
  style: 'paragraph'
# ATimeOfWar.cs:9288
- type: 'text'
  value: 'restext://BattleTime_005' # "**To Battle**"
- type: 'break'
# ATimeOfWar.cs:9292
- type: 'text'
  value: 'restext://BattleTime_006' # "All players take all their {icon:s3_weapontoken} tokens..."
- type: 'break'
  style: 'paragraph'
# ATimeOfWar.cs:9320
- type: 'link'
  label: 'restext://BattleTime_010' # "Click to begin the battle..."
  target: 'BattleCompleteReturn'
  snapshot: true
- type: 'break'
```

---

*MWS format v0.5 — Masterwork project.*
