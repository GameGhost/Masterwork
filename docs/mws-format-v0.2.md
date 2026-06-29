# Masterwork Script Format — v0.2 Reference

MWS (Masterwork Script) is the YAML-based passage format used to represent interactive narrative content for the Masterwork engine. Each `.mws.yaml` file is a single passage.

This document describes format v0.2. For the v0.1 format as produced by the initial extractor pass, see `mws-format-v0.1.md`.

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
| `layout` | string | yes | One of: `hub`, `event`, `narration`, `private`, `menu`, `modal` |
| `debug` | bool | no | `true` for developer-only passages excluded from player builds |
| `location` | object | no | Location displayed in app header chrome; fields: `name` (string), `icon` (asset URI) |
| `check_progress` | string | no | `passage_id` that must have been visited before this passage is valid |

`location` replaces the v0.1 `set_location` node. Because set-location was always the first node in a passage in the source modules, it maps cleanly to a header field.

`check_progress` replaces the v0.1 `check_progress` node. It is a constraint enforced at passage load time, not mid-passage.

### Layout Values

| Layout | Description |
|---|---|
| `hub` | Generation hub — section headings + collapsible bodies, multiple optional links |
| `event` | Full-page event card — narrative text with prominent bottom links |
| `narration` | Pure story passage; minimal chrome |
| `private` | Full-screen cover until player confirms; used for secret information |
| `menu` | App navigation / main menu |
| `modal` | Full-screen modal panel; used for generation-end summaries and special events |

`modal` replaces the v0.1 `modal` node. The passage's nodes contain the modal body; `layout: modal` and optional `chrome` properties supply the visual frame. Passage-level layout chrome is defined in the module manifest (see §8).

---

## 2. Value Syntax

The `value` field on `text` nodes is a human-readable, i18n-translatable string. Style is expressed inline using markup tokens; there is no separate `style` field.

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

A heading-style text is expressed as a bold span within the value, not as a separate `style: bold` field. This keeps the style co-located with the string so translators see the full formatted text.

### Variable References

`{varName}` — resolved from session variables at render time.

### Inline Icons

`{icon:slug}` — resolved to the asset identified by `icon://slug`.

```yaml
- type: text
  value: All players take all their {icon:s3_weapontoken} tokens into their hands.
```

### Array Element Access

`{arr[1st]}` — resolves the value at the named ordinal index of array variable `arr`.

Index names: `1st`, `2nd`, `3rd`, `4th`, `5th`, ... — mapped to 0-based positions.

### i18n String References

`restext://Key` — resolved from the locale file at module startup.

```yaml
- type: text
  value: restext://BattleTime_001 # "**Glory and Recognition**"
```

The `# "..."` comment is an inline preview for human readers; the engine ignores it.

---

## 3. Expression Language

Expressions appear in `let / expr`, `assign / expr`, `action / nodes` (for `let` and `assign` nodes embedded before navigation), and `conditional / condition`.

The expression evaluator is implemented in the engine (Phase 1). The extractor produces v0.2 expressions during its transformation pass.

### Literals

| Kind | Syntax | Examples |
|---|---|---|
| Integer | Unquoted number | `0`, `42`, `-3` |
| String | Double-quoted | `"yes"`, `"Biology"` |
| Boolean | `true`, `false` | `true` |
| Array | `[item, ...]` | `[1, 2, 3]`, `["a", "b"]` |

### Variable References

Plain identifiers refer to the current variable scope:
- Session variables (module globals): always in scope
- `let` variables: in scope from their definition point to the end of the passage

```
round
wolves
_rnd_BattleTime_0
```

### Operators

**Math** (integer only; integer divide and modulo):

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
| `arr[1..3]` | Elements at indices 1 and 2 (exclusive upper) |
| `arr[1..]` | All elements from index 1 onward |
| `arr[..^1]` | All elements except the last |
| `str[2]` | Character at position 2 (returned as string) |
| `str[1..4]` | Substring from index 1 to 3 |

### Array Operations

All array operations are **immutable** — they return a new array and do not modify the source.

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
| `countif(pattern, a, b, ...)` | Count of values matching a condition pattern string |

**`rand_between`**: `seed_key` is a string literal uniquely identifying this random call within the module. The engine uses it to derive a stable PRNG offset from the master seed.

```
rand_between(1, 6, "Expedition3_0")
["a", "b", "c"].shuffled("BattleTime_0")[0]
```

**`countif`** pattern strings use the comparison operator syntax from §4:

```
countif(">0", scoreA, scoreB, scoreC)   # count of scores greater than 0
countif("==yes", flagA, flagB)           # count of flags equal to "yes"
```

### Pattern Expressions

In `switch / cases[i] / match` and for simplified conditional comparisons, a *pattern* is a string with a leading comparison operator:

```
'>4'    '<=2'    '>=3'    '<5'    '!=0'    '=3'
```

In switch `match:`, `=` means equality (since the variable being tested is implicit). In condition expressions, the full `var op value` form is used.

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

Variables are declared in `_variables.yaml` alongside the passage files:

```yaml
variables:
  round:   { type: int,    default: 0 }
  wolves:  { type: string, default: "" }
  build:   { type: array,  default: [] }
```

`let` variables are passage-scoped: they exist from their definition point until the passage finishes rendering. Their type is inferred from the first expression that produces them. They are never written to session state.

`assign` nodes write to session (module-global) variables.

---

## 6. Node Type Reference

### `text`

Displays human-readable content.

| Field | Type | Required | Description |
|---|---|---|---|
| `value` | string | yes | Text with inline markup; supports `{varName}`, `{icon:slug}`, `**...**`, `_..._`, `restext://Key` |
| `lets` | list of strings | no | Names of `let` vars consumed by this value (for editor grouping) |

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

There is no `style` field; all styling is inline. There is no `runs` form; use a single `value` with inline markup and restext references for mixed content.

---

### `let`

Defines a passage-scoped variable. Evaluated at render time; never persisted. Subsequent nodes in the same passage can reference the variable via `{var}` in `value` strings or `var` in expressions.

| Field | Type | Required | Description |
|---|---|---|---|
| `var` | string | yes | Variable name |
| `expr` | string | yes | Expression to evaluate |

```yaml
# Random integer
- type: let
  var: roll
  expr: rand_between(1, 6, "Expedition3_0")

# Choose-one from a list
- type: let
  var: _rnd_BattleTime_0
  expr: '["{nameA}", "{nameB}", "{nameC}"].shuffled("BattleTime_0")[0]'

# Read last element of an array (non-destructive)
- type: let
  var: top
  expr: scores[^1]

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

Writes a value to a session (module-global) variable. Persisted to the timeline snapshot on the next player action. All `assign` nodes encountered during passage execution are bundled with the subsequent `action` (navigation) into a single state change.

| Field | Type | Required | Description |
|---|---|---|---|
| `var` | string | yes | Session variable name |
| `expr` | string | yes | Expression to evaluate |

```yaml
# Direct assignment
- type: assign
  var: round
  expr: '7'

- type: assign
  var: wolves
  expr: '"yes"'

# Arithmetic
- type: assign
  var: tracker
  expr: tracker + 2

# Random persistent assignment
- type: assign
  var: wolves
  expr: '["evil", "good"].shuffled("WolvesEvent_0")[0]'

# Shuffled array
- type: assign
  var: build
  expr: '[1, 2, 3, 4, 5].shuffled("WolvesEvent_1")'

# Array push (immutable — produces new array)
- type: assign
  var: winners
  expr: '[..winners, nameA]'

# Array pop (remove last element)
- type: assign
  var: candidates
  expr: candidates[..^1]

# Array remove one value
- type: assign
  var: candidates
  expr: candidates.except(nameA)
```

---

### `action`

A player-interactive element. Rendered in the passage UI; activates when tapped.

#### Common Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `label` | string | yes | Displayed text |
| `style` | string | no | Visual style: `link` (default), `button` |
| `type` | string | yes | One of: `navigation`, `popup`, `prompt`, `menu` |

#### Type: `navigation`

Navigates to another passage. Bundles preceding `assign` nodes into a timeline snapshot.

| Field | Type | Required | Description |
|---|---|---|---|
| `target` | string | yes | Destination `passage_id` |
| `state_affecting` | bool | yes | `true` creates a timeline snapshot |
| `timeline_label` | string | no | Custom label for the timeline scrubber entry |
| `nodes` | list | no | `let` and `assign` nodes evaluated before navigation (inline effects) |

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
  label: 2 Players.
  type: navigation
  style: link
  state_affecting: true
  nodes:
  - type: assign
    var: players
    expr: '2'
  target: PlayerNameIntro
```

#### Type: `popup`

Displays a modal overlay. Evaluated with the passage; popup content cannot contain `assign` or navigation nodes.

| Field | Type | Required | Description |
|---|---|---|---|
| `nodes` | list | yes | Content nodes for the popup body |
| `onclose` | string | no | `passage_id` to navigate to when the popup is dismissed |
| `button` | string | no | Dismiss button label; defaults to `"Close"` if no `onclose`, `"Next"` if `onclose` is set |

```yaml
- type: action
  label: Setup Instructions
  type: popup
  style: link
  nodes:
  - type: text
    value: Place the hospital token on space 1 of the hospital track.
  - type: break
  - type: text
    value: Assign starting resources as shown in the reference card.
  onclose: Hospital2
  button: Begin
```

#### Type: `prompt`

Displays an input panel when tapped. On submit, stores the value and navigates.

| Field | Type | Required | Description |
|---|---|---|---|
| `text` | string | yes | Instruction shown in the input panel |
| `input_type` | string | yes | `string` or `number` |
| `store_in` | string | yes | Session variable name to receive the submitted value |
| `onsubmit` | string | yes | `passage_id` to navigate to after submission |

```yaml
- type: action
  label: Enter total hearts collected...
  type: prompt
  style: link
  text: Count up all the heart tokens collected by ALL players. Enter the total here.
  input_type: number
  store_in: charitytotal
  onsubmit: Feverheart
```

#### Type: `menu`

Returns to the app's main navigation / module selection screen.

```yaml
- type: action
  label: Return to Menu
  type: menu
  style: link
```

---

### `prompt`

An inline prompt node — encountered during passage evaluation and interrupts execution until the player submits. Cannot be cancelled. Execution resumes in the current passage after submission.

| Field | Type | Required | Description |
|---|---|---|---|
| `text` | string | yes | Instruction shown in the input panel |
| `input_type` | string | yes | `string` or `number` |
| `store_in` | string | yes | Session variable name to receive the submitted value |

```yaml
- type: prompt
  text: Enter player name A.
  input_type: string
  store_in: nameA
```

Use `prompt` (inline) when the input is mandatory and must be collected before passage content can be determined — for example, collecting player names at game start. Use `action` of type `prompt` when the input is triggered by a player-visible interaction that can be skipped or cancelled.

---

### `goto`

Unconditional navigation. Does not create a timeline snapshot; does not require player interaction. Used when the passage logic unconditionally routes to another passage.

| Field | Type | Required | Description |
|---|---|---|---|
| `target` | string | yes | Destination `passage_id` |

```yaml
- type: goto
  target: PlayerNameIntro
```

For conditional navigation, wrap `goto` in a `conditional`:

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

A visually-distinct content container. Replaces `section_heading`, `section_body`, and `setup_block` from v0.1.

| Field | Type | Required | Description |
|---|---|---|---|
| `title` | string | no | Section heading (value syntax; supports inline markup) |
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
  - type: break
  - type: action
    label: Continue...
    type: navigation
    target: BuildPhase2
    state_affecting: true

# Setup block (style: setup replaces v0.1 setup_block)
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
  title: Optional Reading
  collapsed: true
  nodes:
  - type: text
    value: Background lore for this event...
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
| `cases[i].match` | int, string, list, or pattern | no | Value(s) to match; see §4 |
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

A named milestone in the timeline. Creates a labeled marker in the timeline scrubber.

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

---

### `end_of_generation`

Signals the end of a generation phase. Triggers end-of-generation UI; may suspend the session.

| Field | Type | Required | Description |
|---|---|---|---|
| `generation` | int | yes | The generation number that just ended |
| `message` | string | no | Optional display message |

```yaml
- type: end_of_generation
  generation: 2
  message: Resolve all pending experiments before continuing.
```

---

## 7. Module Layout/Chrome

The `layout` field selects the passage chrome from the module manifest (or the engine's built-in defaults). This allows modules and asset packs to define custom visual treatments for passage types, popups, inputs, and UI elements without changing the passage content nodes.

Layout chrome is defined in the module manifest:

```yaml
# In module manifest (future spec)
layouts:
  generation_end:
    base: modal
    chrome: EndOfGeneration
```

A passage then uses:

```yaml
layout: generation_end
```

Custom layout chrome is a future capability. The v0.2 format reserves the `layout` field for this mechanism; the engine uses built-in defaults for all standard layout values.

---

## 8. Source Annotations

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

## 9. Complete Example

A passage from *A Time of War* updated to v0.2:

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

## 10. Changes from v0.1

### Removed nodes

| v0.1 node | v0.2 replacement |
|---|---|
| `link` | `action` with `type: navigation` |
| `expand_link` | `action` with `type: navigation` and `nodes:` for inline effects |
| `goto_menu` | `action` with `type: menu` |
| `section_heading` + `section_body` | `section` with `title:` |
| `setup_block` | `section` with `style: setup` |
| `input_prompt` | `prompt` (inline) or `action` with `type: prompt` |
| `set_location` | `location:` header field |
| `modal` | Passage with `layout: modal` |
| `check_progress` | `check_progress:` header field |
| `setup_notification` | `action` with `type: popup` |
| `effect` | `assign` (one per variable) |

### Changed nodes

| Node | Change |
|---|---|
| `text` | `template` renamed to `value`; `style` field removed (inline markup only); `runs` form removed |
| `let` | All sub-forms (`random`, `replace`, `pick_from`, `array`, `compute`, `pop`, `dequeue`, `sort`) replaced by single `expr` field |
| `goto` | `condition` field removed; use `conditional` to wrap conditional gotos |

### Preserved nodes

`conditional`, `switch`, `foreach`, `include_passage`, `break`, `paragraph_break`, `checkpoint`, `end_of_generation`

### Header changes

- `location:` object added (replaces `set_location` node)
- `check_progress:` string field added (replaces `check_progress` node)

### Extractor compatibility

The extractor produces v0.1 nodes internally, then runs a transformation pass to produce v0.2 output. v0.1 intermediate node types live in `Masterwork.Extractor`; v0.2 node types are the canonical types in `Masterwork.ModuleFormat`. See the extractor upgrade plan.

---

*MWS format v0.2 — Masterwork project. This document describes the target format for `Masterwork.ModuleFormat` and `Masterwork.Engine`.*
