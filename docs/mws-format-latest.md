# Masterwork Script Format — v0.2 Reference

MWS (Masterwork Script) is the YAML-based passage format used to represent interactive narrative content for the Masterwork engine. Each `.mws.yaml` file is a single passage.

---

## 1. File Structure

Every passage file is a YAML document with a standard header followed by a `nodes:` list.

```yaml
format: mws/0.2
passage_id: Hospital1
title: The Hospital
tags:
- HUB
layout: hub
location:
  name: The Hospital
  icon: icon://hospital_icon
check_progress: Hospital0
nodes:
  - ...
```

### Header Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `format` | string | yes | Always `mws/0.2` |
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
- type: text
  value: '**Glory and Recognition**'

- type: text
  value: Turn to **The Cost of Disease** section. _(All tied players gain this bonus.)_
```

### Variable References

`{varName}` — resolved from the current scope (session variables and let variables).

`{varName.property}` — property access on a custom-typed variable.

```yaml
- type: text
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
- type: text
  value: All players take all their {icon:s3_weapontoken} tokens into their hands.
```

### i18n String References

`restext://Key` — resolved from the locale file loaded at module startup.

```yaml
- type: text
  value: restext://BattleTime_001 # "**Glory and Recognition**"
```

The `# "..."` comment is an inline preview for human readers; the engine ignores it.

---

## 4. Expression Language

Expressions appear in `let / expr`, `assign / expr`, and `conditional / branches[i] / condition`.

### Literals

| Kind | Syntax | Examples |
|---|---|---|
| Integer | Unquoted number | `0`, `42`, `-3` |
| String | Double-quoted | `"yes"`, `"Biology"` |
| Boolean | Keywords | `true`, `false` |
| Array | `[item, ...]` | `[1, 2, 3]`, `["a", "b"]` |
| Record | `{ prop: value, ... }` | `{ name: nameA, hearts: 3, vp: 0 }` |

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
| List | Any of these values | `match: [16, 19]` |
| Pattern string | Comparison | `match: '>4'` |

```yaml
- type: switch
  on: players
  cases:
  - match: '>4'
    nodes: [...]
  - match: '>3'
    nodes: [...]
  - default: true
    nodes: [...]
```

---

## 6. Node Type Reference

### `text`

Displays human-readable content.

| Field | Type | Required | Description |
|---|---|---|---|
| `value` | string | yes | Formatted text (see §3) |
| `lets` | list of strings | no | Names of `let` vars consumed by this value, for editor grouping |

```yaml
- type: text
  value: '**To Battle**'

- type: text
  value: All players take all their {icon:s3_weapontoken} tokens into their hands.

- type: text
  value: '{_rnd_BattleTime_1}'
  lets:
  - _rnd_BattleTime_1
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
- type: let
  var: _rnd_BattleTime_0
  expr: '[nameA, nameB, nameC].shuffled("BattleTime_0")[0]'

# Hoisted from switch branches — all cases define chosen, so it's safe to use after
- type: switch
  on: players
  cases:
  - match: '>3'
    nodes:
    - type: let
      var: chosen
      expr: '[nameA, nameB, nameC, nameD].shuffled("BattleTime_1")[0]'
  - default: true
    nodes:
    - type: let
      var: chosen
      expr: '[nameA, nameB].shuffled("BattleTime_3")[0]'
- type: text
  value: '{chosen} leads this round.'
  lets: [chosen]

# Sort session array into let var (source is not modified)
- type: let
  var: ranked
  expr: all_players.toSorted("descending", "vp")

# Record literal — create a custom type value
- type: let
  var: snapshot
  expr: '{ name: current_player.name, hearts: charitytotal, vp: current_player.vp }'

# Aggregate
- type: let
  var: topScore
  expr: max(scoreA, scoreB, scoreC)
```

---

### `assign`

Writes a value to a session variable. Persistent; all `assign` nodes encountered during passage execution are bundled with the following `navigation` into a single timeline snapshot.

| Field | Type | Required | Description |
|---|---|---|---|
| `var` | string | yes | Session variable name |
| `expr` | string | yes | Expression (see §4) |

```yaml
- type: assign
  var: round
  expr: round + 1

- type: assign
  var: wolves
  expr: '"evil"'

- type: assign
  var: wolves
  expr: '["evil", "good"].shuffled("WolvesEvent_0")[0]'

- type: assign
  var: all_players
  expr: '[..all_players, { name: nameA, hearts: 3, vp: 0 }]'

- type: assign
  var: candidates
  expr: candidates[..^1]
```

---

### `navigation`

A player-clickable link that navigates to another passage. Bundles preceding `assign` nodes into a timeline snapshot.

| Field | Type | Required | Description |
|---|---|---|---|
| `label` | string | yes | Link text (string formatting — see §3) |
| `style` | string | no | Visual style: `link` (default) or `button` |
| `target` | string | yes | Destination `passage_id` |
| `state_affecting` | bool | yes | `true` creates a timeline snapshot |
| `timeline_label` | string | no | Custom label for the timeline scrubber entry |
| `nodes` | list | no | `let` and `assign` nodes evaluated before navigation |

```yaml
- type: navigation
  label: restext://BattleTime_010 # "Click to begin the battle..."
  target: BattleCompleteReturn
  state_affecting: true

# With inline state change
- type: navigation
  label: 2 Players
  state_affecting: true
  nodes:
  - type: assign
    var: players
    expr: '2'
  target: PlayerNameIntro
```

---

### `popup`

A player-clickable label that reveals a modal overlay. Content is evaluated when the passage renders; popup nodes cannot contain `assign` or navigation nodes.

| Field | Type | Required | Description |
|---|---|---|---|
| `label` | string | yes | Button/link text (string formatting — see §3) |
| `style` | string | no | Visual style: `link` (default) or `button` |
| `chrome` | string | no | Named chrome definition (see §8) — overrides the popup's default visual treatment |
| `nodes` | list | no | Content nodes for the popup body; omitted if the chrome fully owns the popup UI |
| `onclose` | string | no | `passage_id` to navigate to when dismissed or when chrome-driven interaction completes |
| `button` | string | no | Dismiss button label; defaults to `"Close"` (no `onclose`) or `"Next"` (with `onclose`) |

Standard popup with body content:
```yaml
- type: popup
  label: Setup Instructions
  nodes:
  - type: text
    value: Place the hospital token on space 1 of the hospital track.
  onclose: Hospital2
  button: Begin
```

Chrome-driven popup (body owned entirely by the chrome; no `nodes`):
```yaml
- type: popup
  chrome: voting
  label: restext://S4Kill2_006  # "Click to start the vote..."
  state_affecting: false
  onclose: S4Kill3
```

---

### `input`

A player-clickable form that collects a value and navigates on submit. Can be cancelled without state change.

| Field | Type | Required | Description |
|---|---|---|---|
| `label` | string | yes | Button/link text shown before the form opens (string formatting — see §3) |
| `style` | string | no | Visual style: `link` (default) or `button` |
| `text` | string | yes | Instruction text displayed inside the form (string formatting — see §3) |
| `input_type` | string | yes | `string` or `number` |
| `store_in` | string | yes | Session variable to receive the submitted value |
| `onsubmit` | string | yes | `passage_id` to navigate to after submission |

```yaml
- type: input
  label: Enter total hearts collected...
  text: Count up all heart tokens collected by ALL players. Enter the total here.
  input_type: number
  store_in: charitytotal
  onsubmit: Feverheart
```

---

### `prompt`

An inline prompt — interrupts passage rendering until the player submits. Cannot be cancelled. Execution resumes immediately after the node.

| Field | Type | Required | Description |
|---|---|---|---|
| `text` | string | yes | Instruction text (string formatting — see §3) |
| `input_type` | string | yes | `string` or `number` |
| `store_in` | string | yes | Session variable to receive the submitted value |

```yaml
- type: prompt
  text: Enter player name A.
  input_type: string
  store_in: nameA
```

---

### `goto`

Unconditional navigation — no player interaction, no timeline snapshot.

| Field | Type | Required | Description |
|---|---|---|---|
| `target` | string | yes | Destination `passage_id`, or a `{varName}` expression resolving to one |

```yaml
- type: goto
  target: PlayerNameIntro

- type: goto
  target: '{endingPassage}'
```

For conditional routing, wrap in `conditional`:

```yaml
- type: conditional
  branches:
  - condition: wolves == "evil"
    nodes:
    - type: goto
      target: BonusPath
  - else: true
    nodes:
    - type: goto
      target: NeutralPath
```

---

### `section`

A visually-distinct content container. Optionally titled and collapsible.

| Field | Type | Required | Description |
|---|---|---|---|
| `title` | string | no | Section heading (string formatting — see §3) |
| `collapsed` | bool | no | If `true`, renders collapsed; player can expand. Default: `false` |
| `style` | string | no | One of: `section` (default), `panel`, `well`, `quote`, `setup` |
| `nodes` | list | yes | Content nodes |

```yaml
- type: section
  title: '**Board of Trustees**'
  nodes:
  - type: text
    value: Each player may now purchase one building from the market.
  - type: action
    label: Continue...
    type: navigation
    target: BuildPhase2
    state_affecting: true

- type: section
  style: setup
  nodes:
  - type: text
    value: '**SETUP**'
  - type: paragraph_break
  - type: text
    value: Place the hospital token on space 1 of the hospital track.
```

---

### `conditional`

Evaluates conditions in order; renders the first matching branch.

| Field | Type | Required | Description |
|---|---|---|---|
| `branches` | list | yes | Ordered list of branch objects |
| `branches[i].condition` | string | no | Condition expression (see §4) |
| `branches[i].else` | bool | no | `true` marks the fallback branch |
| `branches[i].nodes` | list | yes | Nodes rendered when this branch is taken |

---

### `switch`

Tests a single variable against a set of cases (see §5 for match patterns).

| Field | Type | Required | Description |
|---|---|---|---|
| `on` | string | yes | Variable name to test |
| `cases` | list | yes | Ordered list of case objects |
| `cases[i].match` | pattern | no | Value(s) to match (see §5) |
| `cases[i].default` | bool | no | `true` marks the fallback case |
| `cases[i].nodes` | list | yes | Nodes rendered when this case is taken |

---

### `foreach`

Iterates over an array variable, rendering `nodes` once per element.

| Field | Type | Required | Description |
|---|---|---|---|
| `var` | string | yes | Loop variable name; in scope within `nodes` |
| `in` | string | yes | Name of the array variable to iterate |
| `nodes` | list | yes | Node list rendered for each element |

```yaml
- type: foreach
  var: winner
  in: winners
  nodes:
  - type: text
    value: '{winner.name} gains 5VP.'
  - type: break
```

---

### `include_passage`

Embeds another passage's content inline.

| Field | Type | Required | Description |
|---|---|---|---|
| `target` | string | yes | `passage_id` to include |

---

### `record`

Records that the player has reached a named milestone. References an achievement defined in the module manifest (see §9).

- If the achievement is **boolean**, it is unlocked (if not already).
- If the achievement is **threshold**, the counter is incremented by 1 (up to the threshold; already-met thresholds are not incremented again).

| Field | Type | Required | Description |
|---|---|---|---|
| `id` | string | yes | Achievement identifier as defined in the module manifest |

```yaml
- type: record
  id: ending_wolves_evil_1

- type: record
  id: endings_discovered
```

---

### `break`

A line break within a content block.

```yaml
- type: break
```

---

### `paragraph_break`

A paragraph separator.

```yaml
- type: paragraph_break
```

---

### `checkpoint`

A named milestone in the timeline. Creates a labeled marker in the scrubber. Also used to mark the boundary of a generation phase.

| Field | Type | Required | Description |
|---|---|---|---|
| `id` | string | yes | Stable checkpoint identifier |
| `display_label` | string | no | Human-readable label in the timeline scrubber |
| `diagnostic_label` | string | no | Machine-readable label for test assertions |

```yaml
- type: checkpoint
  id: generation_2_complete
  display_label: Generation 2 Complete
  diagnostic_label: gen2_end
```

A generation-end sequence uses `section` for the summary and `checkpoint` for the boundary:

```yaml
- type: section
  style: panel
  nodes:
  - type: text
    value: '**End of Generation 2**'
  - type: paragraph_break
  - type: text
    value: Resolve all pending experiments before continuing.
- type: checkpoint
  id: generation_2_complete
  display_label: Generation 2
- type: action
  label: Continue to Generation 3
  type: navigation
  target: Gen3Start
  state_affecting: true
```

---

## 7. Source Annotations

Extracted passage files include YAML comments injected by the extractor. These are informational only; the engine ignores them.

```yaml
# TheCostofDisease.cs:29543
- type: text
  value: restext://Expedition3_001 # "**The Expedition Uncovers...**"
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

### Popup Chrome Definitions

A `popup` node's optional `chrome` field names a chrome definition that takes over the popup's entire visual treatment — including its own animations, UI controls, and interaction model. The chrome definition is declared in the module manifest or in a shared asset pack that the module depends on.

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

Chrome-driven popups follow a different lifecycle from content popups:

1. The chrome renders its own body — the passage `nodes` list is unused.
2. The chrome controls when the close/confirm action becomes available (e.g. after an animation or countdown completes).
3. On completion, the engine navigates to `onclose` (if set) as a state-affecting transition.

**Built-in chrome for MFW modules** (`MFW_Common_Assets`):

| Chrome name | Description |
|---|---|
| `voting` | Countdown timer → simultaneous reveal of voting tokens; navigates `onclose` on "Bid Complete" |
| `bidding` | Countdown timer → simultaneous reveal of bid amounts; navigates `onclose` on "Bid Complete" |

Both `voting` and `bidding` display a 3-count countdown (`1, 2, 3, Reveal`), then surface a "Reveal" button. The close/navigate action is hidden until players tap "Reveal". The distinction between modes is the animation and prompt text — the mechanics are identical.

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
- type: record
  id: ending_wolves_evil_1
- type: record
  id: endings_discovered
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
format: mws/0.2
passage_id: BattleTime
title: BattleTime
layout: narration
nodes:
# ATimeOfWar.cs:9251
- type: text
  value: restext://BattleTime_001 # "**Glory and Recognition**"
- type: break
# ATimeOfWar.cs:9255
- type: switch
  on: players
  cases:
  - match: '>4'
    nodes:
    - type: let
      var: _rnd_BattleTime_0
      expr: '[nameA, nameB, nameC, nameD, nameE].shuffled("BattleTime_0")[0]'
  - match: '>3'
    nodes:
    - type: let
      var: _rnd_BattleTime_0
      expr: '[nameA, nameB, nameC, nameD].shuffled("BattleTime_1")[0]'
  - match: '>2'
    nodes:
    - type: let
      var: _rnd_BattleTime_0
      expr: '[nameA, nameB, nameC].shuffled("BattleTime_2")[0]'
  - default: true
    nodes:
    - type: let
      var: _rnd_BattleTime_0
      expr: '[nameA, nameB].shuffled("BattleTime_3")[0]'
# ATimeOfWar.cs:9278
- type: let
  var: _rnd_BattleTime_1
  expr: >-
    [restext://BattleTime_002, restext://BattleTime_003, restext://BattleTime_004]
    .shuffled("BattleTime_4")[0]
- type: text
  value: '{_rnd_BattleTime_1}'
  lets:
  - _rnd_BattleTime_1
- type: paragraph_break
# ATimeOfWar.cs:9288
- type: text
  value: restext://BattleTime_005 # "**To Battle**"
- type: break
# ATimeOfWar.cs:9292
- type: text
  value: restext://BattleTime_006 # "All players take all their {icon:s3_weapontoken} tokens..."
- type: paragraph_break
# ATimeOfWar.cs:9320
- type: action
  label: restext://BattleTime_010 # "Click to begin the battle..."
  type: navigation
  target: BattleCompleteReturn
  state_affecting: true
- type: break
```

---

*MWS format v0.2 — Masterwork project.*
