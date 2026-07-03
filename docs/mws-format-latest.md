# Masterwork Script Format — v0.3 Reference

MWS (Masterwork Script) is the YAML-based passage format used to represent interactive narrative content for the Masterwork engine. Each `.mws.yaml` file is a single passage.

---

## 1. File Structure

Every passage file is a YAML document with a standard header followed by a `nodes:` list.

```yaml
format: 'mws/0.3'
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
| `format` | string | yes | Always `mws/0.3` |
| `passage_id` | string | yes | Canonical passage identifier |
| `title` | string | no | Display title; defaults to `passage_id` |
| `tags` | list of strings | no | Source tags; drives layout inference |
| `layout` | string | yes | One of: `hub`, `event`, `narration`, `private`, `modal` |
| `debug` | bool | no | `true` for developer-only passages excluded from player builds |
| `location` | object | no | Location shown in app header. Fields: `name` (string), `icon` (asset URI) |
| `check_progress` | string | no | `passage_id` that must have been visited before this passage is valid to render |

### Layout Values

| Layout | Description |
|---|---|
| `hub` | Generation hub — sections with headings, collapsible bodies, multiple optional links |
| `event` | Full-page event card — narrative text with prominent bottom links |
| `narration` | Story passage; minimal chrome |
| `private` | Full-screen cover until the player confirms; used for secret per-player information |
| `modal` | Full-screen modal panel; used for generation-end summaries and special events |

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

`restext://` URIs can appear within expression strings. The URI token is replaced in-place; surrounding expression quotes are untouched:

```yaml
# After load-time substitution: warwinner == "Separatists"
- if: warwinner == "restext://Common_026"  # en-US.restext:33
# After load-time substitution: Separatists
- match: restext://Common_026              # en-US.restext:33
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

# Multi-line value — open with """ immediately after =, close with """ on its own line
UniCharity_008="""
Retrieve a **Charity Memorial** Estate Tile from the scenario box. {charity} III
may build this in their next available plot.

Return the Heart{icon:s1_hearttoken}token to the scenario box.
"""
```

**Rules:**
- One `Key=Value` entry per line; keys are alphanumeric with underscores (`[A-Za-z0-9_]+`)
- Lines starting with `#` are comments; blank lines are ignored
- Multi-line values: the `=` is followed immediately by `"""` and a newline; content continues until a line containing only `"""`; the trailing newline before the closing `"""` is discarded
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

Fields that support expression values: `navigation.target`, `goto.target`, `popup.onclose`, `input.onsubmit`.

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

```yaml
- type: 'image'
  asset: 'icon://scenariobox3d_war'
  size: '200'
  align: 'center'
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

Writes a value to a session variable. Persistent; all `assign` nodes encountered during passage execution are bundled with the following `navigation` into a single timeline snapshot.

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

### `navigation`

A player-clickable link that navigates to another passage. Bundles preceding `assign` nodes into a timeline snapshot.

| Field | Type | Required | Description |
|---|---|---|---|
| `label` | string | yes | Link text (string formatting — see §3) |
| `style` | string | no | Visual style: `link` (default) or `button` |
| `target` | string | yes | Destination `passage_id`, or `'${expression}'` for a runtime-computed target (see §4) |
| `state_affecting` | bool | yes | `true` creates a timeline snapshot |
| `timeline_label` | string | no | Custom label for the timeline scrubber entry |
| `onclick` | list | no | `let`, `assign`, and `conditional` nodes executed on click before navigation |

**Execution order when `onclick` is present:** on click, the engine executes all nodes in the `onclick` list first, then evaluates `target`. This matters when `target` is an expression referencing a variable and an `onclick` entry may assign that variable — the variable is resolved after the assignments run, not at render time.

```yaml
- type: 'navigation'
  label: 'restext://BattleTime_010' # "Click to begin the battle..."
  target: 'BattleCompleteReturn'
  state_affecting: true

# With inline state change — target is a literal passage_id
- type: 'navigation'
  label: '2 Players'
  target: 'PlayerNameIntro'
  state_affecting: true
  onclick:
  - type: 'assign'
    var: 'players'
    expr: '2'

# With dynamic target — onclick may assign the target variable before navigation
- type: 'navigation'
  label: '{nameA}'
  target: '${feverheartnextPsg}'
  state_affecting: true
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

A modal overlay, either click-triggered (has `label`) or auto-displayed when the passage renders (no `label`, layout controls display). Popup nodes cannot contain `assign` or navigation nodes.

| Field | Type | Required | Description |
|---|---|---|---|
| `label` | string | no | Button/link text that triggers the popup; omit for auto-display (layout-driven only) |
| `style` | string | no | Visual style: `link` (default) or `button` |
| `layout` | string | no | Named layout definition (see §8) — overrides the popup's default visual treatment |
| `content` | list | no | Content nodes for the popup body; for layout-driven popups, may contain `let`/`conditional`/`switch` nodes evaluated at open time to bind layout properties |
| `onclose` | string | no | Destination when dismissed or when layout-driven interaction completes — `passage_id` or `'${expression}'` (see §4) |
| `button` | string | no | Dismiss button label; defaults to `"Close"` (no `onclose`) or `"Next"` (with `onclose`) |

Standard popup with body content:
```yaml
- type: 'popup'
  label: 'Setup Instructions'
  content:
  - type: 'text'
    value: 'Place the hospital token on space 1 of the hospital track.'
  onclose: 'Hospital2'
  button: 'Begin'
```

Layout-driven popup — body owned by the layout, no content nodes:
```yaml
- type: 'popup'
  layout: 'voting'
  label: 'restext://S4Kill2_006'  # "Click to start the vote..."
  state_affecting: false
  onclose: 'S4Kill3'
```

Layout-driven popup with property bindings — content nodes bind values to layout properties at open time:
```yaml
- type: 'popup'
  layout: 'end_of_generation'
  label: 'restext://Common_190'  # "Click here at the end of the round..."
  state_affecting: true
  onclose: 'ATOWSabotageIntro1'
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

A player-clickable form that collects a value and navigates on submit. Can be cancelled without state change.

| Field | Type | Required | Description |
|---|---|---|---|
| `label` | string | yes | Button/link text shown before the form opens (string formatting — see §3) |
| `style` | string | no | Visual style: `link` (default) or `button` |
| `text` | string | yes | Instruction text displayed inside the form (string formatting — see §3) |
| `input` | string | yes | `string` or `number` |
| `var` | string | yes | Session variable to receive the submitted value |
| `onsubmit` | string | yes | Destination after submission — `passage_id` or `'${expression}'` (see §4) |

```yaml
- type: 'input'
  label: 'Enter total hearts collected...'
  text: 'Count up all heart tokens collected by ALL players. Enter the total here.'
  input: 'number'
  var: 'charitytotal'
  onsubmit: 'Feverheart'
```

---

### `prompt`

An inline prompt — interrupts passage rendering until the player submits. Cannot be cancelled. Execution resumes immediately after the node.

| Field | Type | Required | Description |
|---|---|---|---|
| `text` | string | yes | Instruction text (string formatting — see §3) |
| `input` | string | yes | `string` or `number` |
| `var` | string | yes | Session variable to receive the submitted value |

```yaml
- type: 'prompt'
  text: 'Enter player name A.'
  input: 'string'
  var: 'nameA'
```

---

### `goto`

Unconditional navigation — no player interaction, no timeline snapshot.

| Field | Type | Required | Description |
|---|---|---|---|
| `target` | string | yes | Destination `passage_id`, or `'${expression}'` resolving to one (see §4) |

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
| `style` | string | no | One of: `section` (default), `panel`, `well`, `quote`, `setup` |
| `content` | list | yes | Content nodes |

```yaml
- type: 'section'
  title: '**Board of Trustees**'
  content:
  - type: 'text'
    value: 'Each player may now purchase one building from the market.'
  - type: 'navigation'
    label: 'Continue...'
    target: 'BuildPhase2'
    state_affecting: true

- type: 'section'
  style: 'setup'
  content:
  - type: 'text'
    value: '**SETUP**'
  - type: 'paragraph_break'
  - type: 'text'
    value: 'Place the hospital token on space 1 of the hospital track.'
```

---

### `conditional`

Evaluates conditions in order; renders the first matching branch, or the `else` list if none match.

Two forms are used depending on the number of branches:

**Flat form** — exactly one if-branch and no else: condition and body sit directly on the node.

| Field | Type | Required | Description |
|---|---|---|---|
| `if` | string | yes | Condition expression (see §4) |
| `then` | list | yes | Nodes rendered when the condition is true |

```yaml
- type: 'conditional'
  if: 'round >= 3'
  then:
  - type: 'assign'
    var: 'phase'
    expr: '"late"'
```

**Multi-branch form** — two or more if-branches, or any if-branch with an `else`: branches are in a `conditions` list.

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

```yaml
- type: 'break'
```

---

### `paragraph_break`

A paragraph separator.

```yaml
- type: 'paragraph_break'
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
  - type: 'paragraph_break'
  - type: 'text'
    value: 'Resolve all pending experiments before continuing.'
- type: 'checkpoint'
  id: 'generation_2_complete'
  display: 'Generation 2'
- type: 'navigation'
  label: 'Continue to Generation 3'
  target: 'Gen3Start'
  state_affecting: true
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
2. The layout renders its own body using those properties. Standard content nodes (`text`, `navigation`, etc.) are not displayed.
3. The layout controls when the close/confirm action becomes available (e.g. after an animation or countdown completes).
4. On completion, the engine navigates to `onclose` (if set) as a state-affecting transition. When `passageName` is a computed layout property (from a conditional `let: passageName`), the layout uses that value for navigation instead.

**Built-in popup layouts for MFW modules** (`MFW_Common_Assets`):

| Layout name | Display trigger | Properties | Description |
|---|---|---|---|
| `voting` | Click (`label` required) | — | Countdown timer → simultaneous reveal of voting tokens; navigates `onclose` on "Bid Complete" |
| `bidding` | Click (`label` required) | — | Countdown timer → simultaneous reveal of bid amounts; navigates `onclose` on "Bid Complete" |
| `end_of_generation` | Auto or click | `title`, `completedRound`, `generation`, `passageName` | Full-screen End-of-Generation summary modal; updates progress bar if `completedRound ≥ 0`; navigates `onclose` or `passageName` on confirm |
| `setup` | Click (`label` required) | `_SetupImage` | Item-obtain setup panel; displays a setup image and instruction text with an ACCEPT button |

For `voting` and `bidding`: both display a 3-count countdown (`1, 2, 3, Reveal`), then surface a "Reveal" button. The close/navigate action is hidden until players tap "Reveal". The distinction between modes is the animation and prompt text — the mechanics are identical.

For `end_of_generation`: displays the `title` string and the passage `text` node(s) as the instruction body. The `completedRound` value (≥ 0) updates the app progress bar; `-1` skips the update. The modal can be triggered automatically (no `label`, used for top-level `S_OnEndOfGeneration` calls) or by a click (with `label`, used for `S_OnSetSpecialSetup` in expand-link fragments). Navigation target is `onclose` when set as a literal, or the `passageName` property (bound by conditional nodes in `nodes`) when computed at runtime.

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
format: 'mws/0.3'
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
- type: 'paragraph_break'
# ATimeOfWar.cs:9288
- type: 'text'
  value: 'restext://BattleTime_005' # "**To Battle**"
- type: 'break'
# ATimeOfWar.cs:9292
- type: 'text'
  value: 'restext://BattleTime_006' # "All players take all their {icon:s3_weapontoken} tokens..."
- type: 'paragraph_break'
# ATimeOfWar.cs:9320
- type: 'navigation'
  label: 'restext://BattleTime_010' # "Click to begin the battle..."
  target: 'BattleCompleteReturn'
  state_affecting: true
- type: 'break'
```

---

*MWS format v0.3 — Masterwork project.*
