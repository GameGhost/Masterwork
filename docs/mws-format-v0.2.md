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

The passage `layout` selects the chrome from the module manifest or engine built-in defaults. Module-specific chrome is defined in the manifest (see §8).

---

## 2. String Formatting

The `value` field on `text` nodes, and any other field that contains user-visible text, is a human-readable string that supports inline formatting tokens. This section defines the full string formatting syntax.

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

`{varName}` — resolved from the current variable scope at render time. Session variables and `let` variables are both in scope.

```yaml
- type: text
  value: 'Round {round} of {maxRound}'
```

### Array Element Access

`{arr[N]}` — resolves the element at 0-based index `N` of array variable `arr`. Supports C# range index syntax:

| Syntax | Meaning |
|---|---|
| `{arr[0]}` | First element |
| `{arr[1]}` | Second element |
| `{arr[^1]}` | Last element |
| `{arr[^2]}` | Second-to-last element |

```yaml
- type: text
  value: 'First player: {playerNames[0]}, Last player: {playerNames[^1]}'
```

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

## 3. Expression Language

Expressions appear in `let / expr` and `assign / expr` nodes, and in `conditional / branches[i] / condition` fields.

The expression evaluator is implemented in the engine. The extractor produces expressions during its v0.2 transformation pass.

### Literals

| Kind | Syntax | Examples |
|---|---|---|
| Integer | Unquoted number | `0`, `42`, `-3` |
| String | Double-quoted | `"yes"`, `"Biology"` |
| Boolean | Keywords | `true`, `false` |
| Array | `[item, ...]` | `[1, 2, 3]`, `["a", "b"]` |

### Variable References

Plain identifiers refer to the current scope:
- Session variables (module globals): always in scope
- `let` variables: in scope from their assignment point to the end of the passage (see §6 `let` for hoisting behaviour)

```
round
wolves
_rnd_BattleTime_0
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

**Logic:**

```
a && b    a || b    !a
```

Precedence (high to low): `!` → `* / %` → `+ -` → `< <= > >= == !=` → `&&` → `||`

Use parentheses to override precedence.

### Index and Range Syntax

Uses C# range semantics:

| Syntax | Meaning |
|---|---|
| `arr[0]` | First element (0-based) |
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
| `arr[index-or-range]` | Element access or slice (see above) |
| `arr.shuffled(seed_key)` | A new array with the same elements in random order |
| `arr.toSorted(dir)` | A new sorted array; `dir` is `"ascending"` or `"descending"` |
| `arr.toSorted(dir, property)` | Sort an array of objects by a named property |
| `arr.except(value)` | A new array with all occurrences of `value` removed |
| `arr.except(other_arr)` | A new array with all elements in `other_arr` removed |
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
| `str[index-or-range]` | Character or substring (see range syntax above) |
| `str + other` | Concatenate two strings |

### Built-in Functions

| Function | Description |
|---|---|
| `rand_between(min, max, seed_key)` | Random integer in `[min, max]` inclusive |
| `max(a, b, ...)` | Maximum of a variadic list of integers |
| `min(a, b, ...)` | Minimum of a variadic list of integers |
| `countif(pattern, a, b, ...)` | Count of values matching a pattern string |

**`rand_between`**: `seed_key` is a string literal that uniquely identifies this random call within the module. The engine uses it to derive a stable PRNG offset from the master seed, ensuring the same game seed always produces the same outcomes.

```
rand_between(1, 6, "Expedition3_0")
["a", "b", "c"].shuffled("BattleTime_0")[0]
```

**`countif`** pattern strings use the comparison operator format from §4:

```
countif(">0", scoreA, scoreB, scoreC)
countif("==yes", flagA, flagB)
```

### Pattern Expressions

In `switch / cases[i] / match` and for single-value comparisons, a *pattern* is a string with a leading comparison operator applied against an implicit variable:

```
'>4'    '<=2'    '>=3'    '<5'    '!=0'    '=3'
```

`=` in a pattern means equality (not assignment). In full condition expressions, the `var == value` form is used.

---

## 4. Switch `match:` Patterns

The `match:` field on a switch case can be:

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

## 5. Variable Types

| Type | Description | Default |
|---|---|---|
| `int` | Integer | `0` |
| `string` | Text string | `""` |
| `array` | Ordered list | `[]` |

Variables are declared in `_variables.yaml`:

```yaml
variables:
  round:   { type: int,    default: 0 }
  wolves:  { type: string, default: "" }
  build:   { type: array,  default: [] }
```

**Session variables** are module-global. They persist across passages and are written only by `assign` nodes.

**Let variables** are passage-scoped. They exist from their assignment point until the passage finishes rendering. Type is inferred from the producing expression.

---

## 6. Node Type Reference

### `text`

Displays human-readable content.

| Field | Type | Required | Description |
|---|---|---|---|
| `value` | string | yes | Formatted text string (see §2) |
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
| `expr` | string | yes | Expression to evaluate (see §3) |

**Hoisting**: `let` variables are scoped to the entire passage from their assignment point. A `let` declared inside a `conditional` branch or `switch` case is accessible in nodes that follow the branch. If a branch is not taken, any `let` declared only in that branch is not set; accessing an unset `let` is a runtime error.

```yaml
# Passage-level let — always in scope below this point
- type: let
  var: roll
  expr: rand_between(1, 6, "Expedition3_0")

# Let inside a switch — hoisted to passage scope after the switch
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
# chosen is accessible here because all cases define it
- type: text
  value: '{chosen} leads this round.'
  lets:
  - chosen

# Sort a session array into a let var without modifying the source
- type: let
  var: ranked
  expr: scores.toSorted("descending", "vp")

# Assemble an array from session variables
- type: let
  var: allNames
  expr: '[nameA, nameB, nameC]'

# Aggregate
- type: let
  var: topScore
  expr: max(scoreA, scoreB, scoreC)
```

---

### `assign`

Writes a value to a session variable. Persistent; included in the next timeline snapshot. All `assign` nodes encountered during passage execution are bundled with the following `action` (navigation) into a single state change.

| Field | Type | Required | Description |
|---|---|---|---|
| `var` | string | yes | Session variable name |
| `expr` | string | yes | Expression to evaluate (see §3) |

```yaml
- type: assign
  var: round
  expr: '7'

- type: assign
  var: wolves
  expr: '"yes"'

- type: assign
  var: tracker
  expr: tracker + 2

- type: assign
  var: wolves
  expr: '["evil", "good"].shuffled("WolvesEvent_0")[0]'

- type: assign
  var: build
  expr: '[1, 2, 3, 4, 5].shuffled("WolvesEvent_1")'

# Append to array
- type: assign
  var: winners
  expr: '[..winners, nameA]'

# Remove last element
- type: assign
  var: candidates
  expr: candidates[..^1]

# Remove a specific value
- type: assign
  var: candidates
  expr: candidates.except(nameA)
```

---

### `action`

A player-interactive element rendered in the passage UI.

#### Common Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `label` | string | yes | Displayed text (supports string formatting — see §2) |
| `style` | string | no | Visual style: `link` (default) or `button` |
| `type` | string | yes | One of: `navigation`, `popup`, `prompt` |

#### Type: `navigation`

Navigates to another passage. Bundles preceding `assign` nodes into a timeline snapshot.

| Field | Type | Required | Description |
|---|---|---|---|
| `target` | string | yes | Destination `passage_id` |
| `state_affecting` | bool | yes | `true` creates a timeline snapshot |
| `timeline_label` | string | no | Custom label for the timeline scrubber entry |
| `nodes` | list | no | `let` and `assign` nodes evaluated before navigation |

```yaml
- type: action
  label: restext://BattleTime_010 # "Click to begin the battle..."
  type: navigation
  target: BattleCompleteReturn
  state_affecting: true
```

With inline effects:

```yaml
- type: action
  label: 2 Players
  type: navigation
  state_affecting: true
  nodes:
  - type: assign
    var: players
    expr: '2'
  target: PlayerNameIntro
```

#### Type: `popup`

Displays a modal overlay. Popup content is evaluated when the passage renders; it cannot contain `assign` or navigation nodes.

| Field | Type | Required | Description |
|---|---|---|---|
| `nodes` | list | yes | Content nodes for the popup body |
| `onclose` | string | no | `passage_id` to navigate to when the popup is dismissed |
| `button` | string | no | Dismiss button label; defaults to `"Close"` if no `onclose`, `"Next"` if `onclose` is set |

```yaml
- type: action
  label: Setup Instructions
  type: popup
  nodes:
  - type: text
    value: Place the hospital token on space 1 of the hospital track.
  onclose: Hospital2
  button: Begin
```

#### Type: `prompt`

Displays an input panel when activated. On submit, stores the value and navigates. Can be cancelled, which dismisses the panel without any state change.

| Field | Type | Required | Description |
|---|---|---|---|
| `text` | string | yes | Instruction shown in the input panel (string formatting — see §2) |
| `input_type` | string | yes | `string` or `number` |
| `store_in` | string | yes | Session variable name to receive the submitted value |
| `onsubmit` | string | yes | `passage_id` to navigate to after submission |

```yaml
- type: action
  label: Enter total hearts collected...
  type: prompt
  text: Count up all the heart tokens collected by ALL players. Enter the total here.
  input_type: number
  store_in: charitytotal
  onsubmit: Feverheart
```

---

### `prompt`

An inline prompt — encountered during passage evaluation and interrupts execution until the player submits. Cannot be cancelled. Execution resumes in the current passage immediately after the prompt node.

| Field | Type | Required | Description |
|---|---|---|---|
| `text` | string | yes | Instruction shown in the input panel (string formatting — see §2) |
| `input_type` | string | yes | `string` or `number` |
| `store_in` | string | yes | Session variable name to receive the submitted value |

```yaml
- type: prompt
  text: Enter player name A.
  input_type: string
  store_in: nameA
```

Use `prompt` (inline) when the input is mandatory and blocks passage rendering — for example, collecting player names at game start. Use `action` of type `prompt` when the input is an optional player-triggered interaction that can be skipped.

---

### `goto`

Unconditional navigation. Does not require player interaction and does not create a timeline snapshot. Used when passage logic unconditionally routes to another passage.

| Field | Type | Required | Description |
|---|---|---|---|
| `target` | string | yes | Destination `passage_id`, or an expression that evaluates to a `passage_id` string |

```yaml
- type: goto
  target: PlayerNameIntro

# Dynamic target — evaluates the expression to determine the passage
- type: goto
  target: '{endingPassage}'
```

For conditional routing, wrap `goto` in a `conditional`:

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
| `title` | string | no | Section heading (string formatting — see §2) |
| `collapsed` | bool | no | If `true`, section renders collapsed; player can expand. Default: `false` |
| `style` | string | no | Visual style: `section` (default), `panel`, `well`, `quote`, `setup` |
| `nodes` | list | yes | Content nodes |

```yaml
# Standard section with heading
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

# Setup instructions block
- type: section
  style: setup
  nodes:
  - type: text
    value: '**SETUP**'
  - type: paragraph_break
  - type: text
    value: Place the hospital token on space 1 of the hospital track.

# Collapsible section
- type: section
  title: Optional Background
  collapsed: true
  nodes:
  - type: text
    value: Background lore text...
```

---

### `conditional`

Evaluates conditions in order; renders the first matching branch.

| Field | Type | Required | Description |
|---|---|---|---|
| `branches` | list | yes | Ordered list of branch objects |
| `branches[i].condition` | string | no | Condition expression (see §3) |
| `branches[i].else` | bool | no | `true` marks the fallback branch |
| `branches[i].nodes` | list | yes | Nodes rendered when this branch is taken |

Exactly one of `condition` or `else: true` per branch. Branches are evaluated in order; the first match wins.

```yaml
- type: conditional
  branches:
  - condition: '!twopage'
    nodes:
    - type: section
      style: setup
      nodes:
      - type: text
        value: '**SETUP**'
  - condition: seedy == "yes"
    nodes:
    - type: text
      value: The angry mob has already been placed.
  - else: true
    nodes:
    - type: text
      value: Place the angry mob token on its starting space.
```

---

### `switch`

Tests a single variable against a set of cases.

| Field | Type | Required | Description |
|---|---|---|---|
| `on` | string | yes | Variable name to test |
| `cases` | list | yes | Ordered list of case objects |
| `cases[i].match` | int, string, list, or pattern | no | Value(s) to match (see §4) |
| `cases[i].default` | bool | no | `true` marks the fallback case |
| `cases[i].nodes` | list | yes | Nodes rendered when this case is taken |

---

### `foreach`

Iterates over an array variable, rendering `nodes` once per element.

| Field | Type | Required | Description |
|---|---|---|---|
| `var` | string | yes | Loop variable name; available as `{var}` within `nodes` |
| `in` | string | yes | Name of the array variable to iterate |
| `nodes` | list | yes | Node list rendered for each element |

```yaml
- type: foreach
  var: winner
  in: winners
  nodes:
  - type: text
    value: '{winner} gains 5VP.'
  - type: break
```

---

### `include_passage`

Embeds another passage's content inline at this point.

| Field | Type | Required | Description |
|---|---|---|---|
| `target` | string | yes | `passage_id` of the passage to include |

```yaml
- type: include_passage
  target: CommonHeader
```

---

### `record`

Records that the player has achieved or witnessed a named event in this module. Used for tracking module endings and other notable milestones. The engine persists these records across sessions for the player's module progress.

| Field | Type | Required | Description |
|---|---|---|---|
| `id` | string | yes | Unique record identifier within this module |

```yaml
- type: record
  id: ending_wolves_evil_1
```

Records are evaluated when the node is encountered during passage rendering, before any navigation. Their identifiers are referenced in the module manifest to define achievement groups (e.g. "all endings found"):

```yaml
# _variables.yaml or module manifest (future spec)
record_groups:
  all_endings:
    requires_all:
      - ending_wolves_evil_1
      - ending_wolves_good_1
      - ending_hunters_evil_1
      - ending_hunters_good_1
```

**Note on the original modules**: In the source modules, ending achievements were recorded implicitly when the player navigated to a passage with an `END-` prefix (e.g. `END-WolvesEvil1`). The extractor must identify these passages and insert a `record` node.

---

### `break`

A line break within a content block.

```yaml
- type: break
```

---

### `paragraph_break`

A paragraph separator — larger vertical gap than `break`.

```yaml
- type: paragraph_break
```

---

### `checkpoint`

A named milestone in the timeline. Creates a labeled marker in the scrubber; also used to signal the end of a generation phase, replacing what was previously a separate end-of-generation node.

| Field | Type | Required | Description |
|---|---|---|---|
| `id` | string | yes | Stable checkpoint identifier |
| `display_label` | string | no | Human-readable label shown in the timeline scrubber |
| `diagnostic_label` | string | no | Machine-readable label for test assertions |

```yaml
- type: checkpoint
  id: generation_2_complete
  display_label: Generation 2 Complete
  diagnostic_label: gen2_end
```

A generation-end sequence uses a `section` for the summary content and a `checkpoint` to mark the boundary, followed by `action` nodes for continuing:

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
# TheCostofDisease.cs:29539
---
format: mws/0.2
passage_id: Expedition3
...
nodes:
# TheCostofDisease.cs:29543
- type: text
  value: restext://Expedition3_001 # "**The Expedition Uncovers...**"
```

---

## 8. Module Layout/Chrome

The `layout` field selects visual chrome from the module manifest or the engine's built-in defaults. This allows modules and asset packs to define custom visual treatments for passage types, popups, inputs, and UI elements.

```yaml
# Module manifest (future spec)
layouts:
  generation_end:
    base: modal
    chrome: EndOfGeneration
```

A passage then uses:

```yaml
layout: generation_end
```

Custom layout chrome is a planned capability. The engine uses built-in defaults for all standard layout values defined in §1.

---

## 9. Complete Example

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
