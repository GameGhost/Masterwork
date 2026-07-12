# Masterwork Script Format — v0.4 Reference

MWS (Masterwork Script) is the YAML-based passage format used to represent interactive narrative content for the Masterwork engine. Each `.mws.yaml` file is a single passage.

---

## 1. File Structure

Every passage file is a YAML document with a standard header followed by a `nodes:` list.

```yaml
format: 'mws/0.4'
passage_id: 'Hospital1'
title: 'The Hospital'
tags:
- 'HUB'
layout: 'hub'
location:
  name: 'The Hospital'
  icon: 'icon://hospital_icon'
check_progress: 'Hospital0'
nodes:
  - ...
```

### Header Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `format` | string | yes | Always `mws/0.4`. Identifies which format revision produced the file — bump when re-extracting or hand-authoring against a newer version of this spec |
| `passage_id` | string | yes | Canonical passage identifier |
| `title` | string | no | Display title; defaults to `passage_id` |
| `tags` | list of strings | no | Source tags; drives layout inference |
| `layout` | string | yes | An open, module-extensible vocabulary. Built-in values: `hub`, `event`, `narration` (see below) |
| `debug` | bool | no | `true` for developer-only passages excluded from player builds |
| `location` | object | no | Location shown in app header. Fields: `name` (string), `icon` (asset URI) |
| `check_progress` | string | no | `passage_id` that must have been visited before this passage is valid to render |

### Layout Values

| Layout | Description |
|---|---|
| `hub` | Generation hub — sections with headings, collapsible bodies, multiple optional links |
| `event` | Full-page event card — narrative text with prominent bottom links |
| `narration` | Story passage; minimal chrome |

These are the only values the engine's own passage chrome renders specially; any other string is accepted and rendered generically (with a `layout-{value}` CSS class for module/asset-pack theming), since `layout` is deliberately an open vocabulary a module can extend. A generation-end summary is handled by `popup` nodes with `layout: end_of_generation` (see §9), which is unrelated to a passage-level `modal` layout. A module wanting a "reveal to one player" moment can compose one from ordinary `conditional`/`input`/`assign` nodes.

The passage `layout` selects the chrome from the module manifest or engine built-in defaults. Module-specific chrome is defined in the module manifest (see §9).

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

Fields that support expression values: `link.target`, `popup.target`, `goto.target`.

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

Precedence (high to low): `!` → `* / %` → `+ -` → `< <= > >= == !=` → `&&` → `||`

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
| `target` | string | yes | Destination `passage_id`, or `'${expression}'` for a runtime-computed target (see §4) |
| `snapshot` | bool or string | no | Whether following this link creates a timeline snapshot. Defaults to `false` if absent. A string value means `true` *and* sets the timeline scrubber's label to that string in one step — overriding the destination passage's own `title` (the default label when `snapshot` is a bare `true`). A preempting `goto` inside `onclick` takes priority over this label — see `goto` below |
| `onclick` | list | no | `let`, `assign`, and `conditional` nodes executed on click before navigation. A `goto` among these preempts `target` |

**Execution order when `onclick` is present:** on click, pending `input` values are committed first, then the engine executes all nodes in the `onclick` list, then evaluates `target` (unless a `goto` inside `onclick` fired, which preempts it). This matters when `target` is an expression referencing a variable and an `onclick` entry may assign that variable — the variable is resolved after the assignments run, not at render time.

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
| `content` | list | no | Content nodes for the popup body — evaluated eagerly, at passage-render time (see the popup transaction model below). For layout-driven popups, may contain `let`/`conditional`/`switch` nodes evaluated the same way to bind layout properties |
| `okay` | string | no | Okay button label (string formatting — see §3); only rendered if present. Clicking it commits any pending `input` values in `content`, runs `onclose`, then resolves `target` |
| `cancel` | string | no | Cancel button label; only rendered if present. Clicking it discards the popup's pending state entirely — no `onclose`, no `target`, no commit |
| `onclose` | list | no | `let`, `assign`, and `conditional` nodes run when Okay is clicked, before `target` is resolved — same shape and timing as `link.onclick`. A `goto` among these preempts `target` |
| `target` | string | no | Destination when Okay is clicked (unless preempted by a `goto` in `onclose`) — `passage_id` or `'${expression}'` (see §4) |
| `snapshot` | bool or string | no | Whether closing this popup via Okay creates a timeline snapshot. Defaults to `false` if absent. A string value means `true` *and* sets the timeline scrubber's label to that string in one step — overriding the destination passage's own `title` (the default label when `snapshot` is a bare `true`). A preempting `goto` inside `onclose` takes priority over this label — see `goto` above |

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
  layout: 'voting'
  label: 'restext://S4Kill2_006'  # "Click to start the vote..."
  target: 'S4Kill3'
```

Layout-driven popup with property bindings — content nodes bind values to layout properties at open time:
```yaml
- type: 'popup'
  layout: 'end_of_generation'
  label: 'restext://Common_190'  # "Click here at the end of the round..."
  snapshot: true
  target: 'ATOWSabotageIntro1'
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

Auto-display layout popup — no `label`, shown immediately when the passage renders:
```yaml
- type: 'popup'
  layout: 'end_of_generation'
  content:
  - type: 'text'
    value: 'restext://TowardsWar_002'  # EOG instruction text
  - type: 'let'
    var: 'generation'
    expr: '3'
```

---

### `input`

A player-fillable field, rendered inline wherever it appears — directly in a passage, or inside a `popup`'s `content`. Implicitly required: it starts empty and editable when live; when the timeline is rewound to view history, it shows disabled and populated from that snapshot instead. There is no `onsubmit`/submit action of its own — any `link` in the same passage (or a popup's `okay` button) stays disabled until every currently-showing `input` has a valid value, and following it commits all of them to their bound variables as part of its own snapshot (see §6 `link`/`popup`).

| Field | Type | Required | Description |
|---|---|---|---|
| `label` | string | yes | Formatted label shown inline with the field (string formatting — see §3) |
| `style` | string | no | Open, module-extensible visual style vocabulary, styled entirely by module CSS |
| `var` | string | yes | Session variable to receive the value |
| `min` | int | no | Minimum accepted value. Only meaningful when `var`'s declared type is numeric |
| `max` | int | no | Maximum accepted value. Only meaningful when `var`'s declared type is numeric |

The field's value type (text vs. number) is **not** declared on the node — it's derived from `var`'s own declared type in the module's variable manifest (an integer-typed variable gets a number field; anything else gets a text field). There's nowhere a mismatch between the two could come from, since they're the same value.

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

Unconditional navigation — no player interaction, no timeline snapshot of its own. A `goto` placed inside a `link`'s `onclick` or a `popup`'s `onclose` is the one exception: it preempts that action's own `target`, and if that action's own `snapshot` is truthy, the `goto`'s own `snapshot_label` (if set) takes priority over the enclosing `link`/`popup`'s own label for the resulting snapshot — see `link`/`popup` above.

| Field | Type | Required | Description |
|---|---|---|---|
| `target` | string | yes | Destination `passage_id`, or `'${expression}'` resolving to one (see §4) |
| `snapshot_label` | string | no | Custom label for the timeline scrubber entry, when this `goto` preempts a state-affecting `link`/`popup`'s target (see above). No effect on a plain `goto`, which never creates a snapshot |

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

## 7. Source Annotations

Extracted passage files include YAML comments injected by the extractor. These are informational only; the engine ignores them.

```yaml
# TheCostofDisease.cs:29543
- type: 'text'
  value: 'restext://Expedition3_001' # "**The Expedition Uncovers...**"
```

---

## 8. Module Layout/Chrome

The `layout` field selects visual chrome from the module manifest or the engine's built-in defaults. Modules can define custom chrome in their manifest:

```yaml
# Module manifest
layouts:
  generation_end:
    base: modal
    chrome: EndOfGeneration
```

A passage then uses:

```yaml
layout: generation_end
```

Custom layout/chrome allows module and asset pack authors to style passage types, popups, inputs, and UI elements without changing passage content.

### Popup Layout Definitions

A `popup` node's optional `layout` field names a layout definition that takes over the popup's entire visual treatment — including its own animations, UI controls, and interaction model. The layout definition is declared in the module manifest or in a shared asset pack that the module depends on.

```yaml
# In the module manifest or asset pack manifest
popups:
  voting:
    description: Simultaneous-reveal voting popup
    asset_pack: MFW_Common_Assets
    interaction: bidding_countdown  # built-in interaction type
    mode: voting
  bidding:
    description: Simultaneous-reveal bidding popup
    asset_pack: MFW_Common_Assets
    interaction: bidding_countdown
    mode: bidding
```

Layout-driven popups follow a different lifecycle from content popups:

1. The engine evaluates any `let`, `conditional`, and `switch` nodes in the popup's `content` list. The resulting variable values are bound to the layout's named properties before display.
2. The layout renders its own body using those properties. Standard content nodes (`text`, `link`, etc.) are not displayed.
3. The layout controls when the close/confirm action becomes available (e.g. after an animation or countdown completes).
4. On completion, the engine runs `onclose` (if any) and navigates to `target` (if set) as a state-affecting transition, same as an ordinary popup's Okay button (see §6 `popup`). When `passageName` is a computed layout property (from a conditional `let: passageName`), the layout uses that value for navigation instead.

**Built-in popup layouts for MFW modules** (`MFW_Common_Assets`):

| Layout name | Display trigger | Properties | Description |
|---|---|---|---|
| `voting` | Click (`label` required) | — | Countdown timer → simultaneous reveal of voting tokens; navigates to `target` on "Bid Complete" |
| `bidding` | Click (`label` required) | — | Countdown timer → simultaneous reveal of bid amounts; navigates to `target` on "Bid Complete" |
| `end_of_generation` | Auto or click | `title`, `completedRound`, `generation`, `passageName` | Full-screen End-of-Generation summary modal; updates progress bar if `completedRound ≥ 0`; navigates `target` or `passageName` on confirm |
| `setup` | Click (`label` required) | `_SetupImage` | Item-obtain setup panel; displays a setup image and instruction text with an ACCEPT button |

For `voting` and `bidding`: both display a 3-count countdown (`1, 2, 3, Reveal`), then surface a "Reveal" button. The close/navigate action is hidden until players tap "Reveal". The distinction between modes is the animation and prompt text — the mechanics are identical.

For `end_of_generation`: displays the `title` string and the passage `text` node(s) as the instruction body. The `completedRound` value (≥ 0) updates the app progress bar; `-1` skips the update. The modal can be triggered automatically (no `label`, used for top-level `S_OnEndOfGeneration` calls) or by a click (with `label`, used for `S_OnSetSpecialSetup` in expand-link fragments). Navigation target is `target` when set as a literal, or the `passageName` property (bound by conditional nodes in `nodes`) when computed at runtime.

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

*MWS format v0.4 — Masterwork project.*
