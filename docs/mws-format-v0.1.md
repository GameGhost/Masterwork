# MasterWork Script Format — v0.1 Reference

MWS (MasterWork Script) is the YAML-based passage format used to represent interactive narrative content for the MasterWork engine. Each `.mws.yaml` file is a single passage.

---

## 1. File Structure

Every passage file is a YAML document with a standard header followed by a `nodes:` list.

```yaml
format: mws/1.0
passage_id: Fever1
title: Fever1
tags:
- HUB
layout: hub
nodes:
  - ...
```

### Header Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `format` | string | yes | Always `mws/1.0` |
| `passage_id` | string | yes | Canonical passage identifier; matches the original Twine passage name |
| `title` | string | no | Display title; defaults to `passage_id` |
| `tags` | list of strings | no | Original Twine tags; drives layout inference |
| `layout` | string | yes | One of: `hub`, `event`, `narration`, `private`, `menu` |
| `debug` | bool | no | `true` for developer-only passages excluded from player builds |

### Layout Values

| Layout | Tag(s) | Description |
|---|---|---|
| `hub` | `ck`, `HUB` (case-insensitive) | Generation hub — section headings + collapsible bodies, multiple optional links |
| `event` | `ck2` | Full-page event card — narrative text with prominent bottom links |
| `narration` | *(none)* | Pure story passage; minimal chrome |
| `private` | *(private gate active)* | Full-screen cover until player confirms; used for secret information |
| `menu` | *(menu passage)* | App navigation / main menu |

---

## 2. Template Syntax

The `template` field on text nodes is a human-readable, i18n-translatable string. It supports inline markup and placeholder tokens.

### Variable References

`{varName}` — resolved from session variables at render time.

```yaml
- type: text
  template: All players take all their {icon:s3_weapontoken} tokens into their hands.
```

### Inline Icons

`{icon:slug}` — resolved to the asset identified by `icon://slug`.

```yaml
- type: text
  template: The **least** {icon:creepy_icon} player **loses 7VP.**
```

### Inline Style Markup

Within a normally-styled template string, use inline markup for styled spans:

| Markup | Meaning |
|---|---|
| `**...**` | Bold span |
| `_..._` | Italic span |

Example:

```yaml
- type: text
  template: Turn to **The Cost of Disease** section. _(All tied players gain this bonus.)_
```

### Array Element Access

`{arr[1st]}` — resolves the value at the named index of array variable `arr`.

Index names: `1st`, `2nd`, `3rd`, `4th`, `5th`, ... — mapped to 0-based positions.

### i18n String References

`restext://Key` — resolved from the locale file loaded at module startup. Used when the module's strings have been extracted for translation.

```yaml
- type: text
  template: restext://BattleTime_001 # "Glory and Recognition"
  style: bold
```

The `# "..."` comment is an inline preview for human readers; the engine ignores it.

---

## 3. Condition Expression Syntax

Conditions appear in `conditional` branch `condition:` fields and `goto` node `condition:` fields.

### Comparison

```
varName op value
```

| Operator | Meaning |
|---|---|
| `==` | Equal |
| `!=` | Not equal |
| `<` | Less than |
| `<=` | Less than or equal |
| `>` | Greater than |
| `>=` | Greater than or equal |

Values: unquoted integers (`2`, `10`) or quoted strings (`"yes"`, `"Biology"`).

```
players == 2
wolves == "evil"
round >= 7
```

### Boolean Negation

```
!varName
```

Evaluates as `true` when `varName` is falsy (`0`, `""`, or not set).

```
!twopage
!seedy
```

### Logical Operators

```
a && b        # both must be true
a || b        # either must be true
```

Operator precedence: `!` > `&&` > `||`. Use parentheses if needed (the evaluator supports them).

---

## 4. Switch `match:` Patterns

The `match:` field on a switch case can be:

| Form | Description | Example |
|---|---|---|
| Integer | Exact equality | `match: 2` |
| String | Exact equality | `match: Biology` |
| List | Any of these values | `match: [16, 19]` |
| Pattern string | Comparison | `match: '>4'` |

Pattern strings use a leading operator followed immediately by the value: `'>4'`, `'<=2'`, `'>=3'`, `'<5'`, `'!=0'`, `'==3'`.

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

| Type | Description | Example values |
|---|---|---|
| `int` | Integer | `0`, `7`, `-1` |
| `string` | Text string | `""`, `"yes"`, `"Biology"` |
| `array` | Ordered list | `[]`, `[1, 2, 3]` |

Variables are declared in `_variables.yaml` alongside the passage files:

```yaml
variables:
  round:   { type: int,    default: 0 }
  wolves:  { type: string, default: "" }
  build:   { type: array,  default: [] }
```

### Array Indexers

Array element access in templates uses named ordinal indices:

| Name | Index |
|---|---|
| `1st` | 0 |
| `2nd` | 1 |
| `3rd` | 2 |
| `4th` | 3 |
| `5th` | 4 |

Template: `{build[1st]}` → first element of the `build` array variable.

---

## 6. `VarRandom` — Random Value Types

Used in `effect / var_random` and `let / random` nodes.

### `choose-one`

Pick one value from a list.

```yaml
random:
  type: choose-one
  values:
  - '{nameA}'
  - '{nameB}'
  - '{nameC}'
  seed_key: Fever1_0
```

Values may be strings, integers, or variable references (`{varName}`). String values are also extracted to the locale file when i18n is enabled.

### `range`

Pick a random integer between `min` and `max` inclusive.

```yaml
random:
  type: range
  min: 1
  max: 6
  seed_key: Expedition3_0
```

### `shuffled_array`

Produce a shuffled copy of the values list (all values, in random order).

```yaml
random:
  type: shuffled_array
  values: [1, 2, 3, 4, 5]
  seed_key: build_order_0
```

### `seed_key`

Every `VarRandom` node has a `seed_key` — a stable string identifier assigned by the extractor. The engine uses it to look up the deterministic PRNG offset for this call, ensuring the same game seed always produces the same random outcomes.

Format: `PassageId_N` where N is a 0-based counter reset per passage.

---

## 7. `SortSpec`

Used in `effect / var_sort` and `let / sort`.

| Field | Type | Required | Description |
|---|---|---|---|
| `direction` | string | yes | `ascending` or `descending` |
| `property` | string | no | For arrays of objects: the property name to sort on |
| `from` | string | no | For `let / sort`: name of the source array variable to sort into this let var |

```yaml
# Sort an array in-place (effect)
- type: effect
  var_sort:
    scores:
      direction: descending
      property: vp

# Sort a source array into a let var (does not modify the source)
- type: let
  var: sorted_build
  sort:
    from: build
    direction: ascending
```

---

## 8. Node Type Reference

### `text`

Displays human-readable content. Has two mutually exclusive forms.

**Template form** (preferred for all translatable text):

| Field | Type | Required | Description |
|---|---|---|---|
| `template` | string | yes | The text string; supports `{varName}`, `{icon:slug}`, `**...**`, `_..._`, `restext://Key` |
| `style` | string | no | Uniform style: `bold` or `italic` |
| `lets` | list of strings | no | Names of `let` vars consumed by this template (for editor grouping) |

```yaml
- type: text
  template: YELLOW FEVER - Early Years
  style: bold

- type: text
  template: If the number of Clue symbols {icon:s2_hunttokenbackred} collected is equal to **{_rnd_Expedition3_0}** or more, the Creature is found and the hunt is a success.

- type: text
  template: '{_rnd_BattleTime_1}'
  lets:
  - _rnd_BattleTime_1
```

**Runs form** (for mixed text/icon nodes that need separate localization keys):

| Field | Type | Required | Description |
|---|---|---|---|
| `runs` | list | yes | Each run is `{text, style, asset_ref}` |
| `runs[i].text` | string | no | Literal text fragment |
| `runs[i].style` | string | no | `bold` or `italic` for this run only |
| `runs[i].asset_ref` | string | no | Asset URI, e.g. `icon://angrymob_icon` |

```yaml
- type: text
  runs:
  - asset_ref: icon://angrymob_icon
  - text: ANGRY MOB
    style: bold
```

A node has either `template` or `runs`, never both.

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

### `let`

A passage-scoped variable. Evaluated at render time; discarded after the passage renders. Never persisted to session state.

| Field | Type | Required | Description |
|---|---|---|---|
| `var` | string | yes | Variable name, available via `{var}` in subsequent templates |
| `random` | VarRandom | no | Random value assignment (choose-one, range, shuffled_array) |
| `replace` | VarReplace | no | String replacement operation |
| `pick_from` | string | no | Pick a random element from a named array variable |
| `array` | list of strings | no | Assemble a temporary array from named variable values |
| `compute` | string | no | Aggregate expression: `max(a, b, ...)`, `min(...)`, `countif("pattern", a, b, ...)` |
| `pop` | string | no | Pop the last element from a named array variable |
| `dequeue` | string | no | Dequeue (shift) the first element from a named array variable |
| `sort` | SortSpec | no | Sort a named array into this var (source array is not modified) |

```yaml
# Random selection
- type: let
  var: _rnd_BattleTime_0
  random:
    type: choose-one
    values:
    - '{nameA}'
    - '{nameB}'
    seed_key: BattleTime_3

# Pick random element from a session array variable
- type: let
  var: chosen
  pick_from: candidates

# Temporary array from variables
- type: let
  var: playerNames
  array:
  - nameA
  - nameB
  - nameC

# Aggregate
- type: let
  var: topScore
  compute: max(scoreA, scoreB, scoreC)

# Sort into let var (non-destructive)
- type: let
  var: ranked
  sort:
    from: scores
    direction: descending
    property: vp
```

**VarReplace** fields:

| Field | Type | Description |
|---|---|---|
| `source` | string | Source variable name |
| `find` | string or list | Value(s) to find and replace |
| `with` | string | Replacement value |

---

### `effect`

Applies persistent state changes to session variables.

| Field | Type | Description |
|---|---|---|
| `var_sets` | map | Direct assignment: `{varName: value}` |
| `var_math` | map | Arithmetic: `{varName: "+N"}`, `"-N"`, `"*N"` |
| `var_random` | map | Random assignment: `{varName: VarRandom}` |
| `var_push` | map | Append to array: `{arrayName: value}` |
| `var_pop` | string | Pop last element from array (discards result) |
| `var_sort` | map | Sort array in-place: `{arrayName: SortSpec}` |
| `var_remove` | map | Remove a value from array: `{arrayName: value}` |

```yaml
# Direct assignment
- type: effect
  var_sets:
    round: 7
    wolves: yes

# Arithmetic
- type: effect
  var_math:
    tracker: '+2'
    charitytotal: '+0'

# Persistent random assignment (use for vars referenced later in other passages)
- type: effect
  var_random:
    wolves:
      type: choose-one
      values:
      - evil
      - good
      seed_key: WolvesEvent_0
    build:
      type: shuffled_array
      values: [1, 2, 3, 4, 5]
      seed_key: WolvesEvent_1

# Array operations
- type: effect
  var_push:
    winners: '{nameA}'
- type: effect
  var_pop: candidates
- type: effect
  var_remove:
    candidates: '{nameA}'
```

**`let` vs `effect / var_random`:** Use `let` for inline random values that feed directly into the current passage's text and are not referenced elsewhere. Use `effect / var_random` for variables that are saved to session state and may be read in other passages or in later conditional logic.

---

### `foreach`

Iterates over the values in an array variable, rendering `nodes` once per element.

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
    template: '{winner} gains 5VP.'
  - type: break
```

---

### `conditional`

Evaluates conditions in order and renders the first matching branch.

| Field | Type | Required | Description |
|---|---|---|---|
| `branches` | list | yes | Ordered list of branch objects |
| `branches[i].condition` | string | no | Condition expression; if omitted, this is the else branch |
| `branches[i].else` | bool | no | `true` marks the fallback branch (evaluated if all conditions fail) |
| `branches[i].nodes` | list | yes | Nodes rendered when this branch is taken |

Exactly one of `condition` or `else: true` must appear per branch. Branches are evaluated in order; the first matching branch wins.

```yaml
- type: conditional
  branches:
  - condition: '!twopage'
    nodes:
    - type: setup_block
      nodes:
      - type: text
        template: SETUP
        style: bold
  - condition: seedy == "yes"
    nodes:
    - type: text
      template: The angry mob has already been placed.
  - else: true
    nodes:
    - type: text
      template: Place the angry mob token on its starting space.
```

---

### `switch`

Tests a single variable against a set of cases. More efficient than a `conditional` for multi-way dispatch on one value.

| Field | Type | Required | Description |
|---|---|---|---|
| `on` | string | yes | Variable name to test |
| `cases` | list | yes | Ordered list of case objects |
| `cases[i].match` | int, string, list, or pattern | no | Value(s) to match; see §4 |
| `cases[i].default` | bool | no | `true` marks the fallback case |
| `cases[i].nodes` | list | yes | Nodes rendered when this case is taken |

```yaml
- type: switch
  on: players
  cases:
  - match: 2
    nodes:
    - type: text
      template: Two-player rules apply.
  - match:
    - 3
    - 4
    nodes:
    - type: text
      template: Standard rules apply.
  - match: '>4'
    nodes:
    - type: text
      template: Extended rules apply.
  - default: true
    nodes:
    - type: text
      template: Unknown player count.
```

---

### `link`

A navigation link the player can tap to advance to another passage. Creates a timeline snapshot when `state_affecting` is true.

| Field | Type | Required | Description |
|---|---|---|---|
| `label` | string | yes | Displayed link text |
| `target` | string | yes | Destination `passage_id` |
| `state_affecting` | bool | yes | `true` if following this link should create a timeline snapshot |
| `timeline_label` | string | no | Custom display label for the timeline scrubber entry |

```yaml
- type: link
  label: Click to begin the battle...
  target: BattleCompleteReturn
  state_affecting: true
```

---

### `goto`

Unconditional (or conditional) navigation. Used inside expand-link bodies and conditional branches to route to another passage without a player tap.

| Field | Type | Required | Description |
|---|---|---|---|
| `target` | string | yes | Destination `passage_id` |
| `condition` | string | no | If present, navigation only occurs when the condition is true |

```yaml
- type: goto
  target: PlayerNameIntro

- type: goto
  target: BonusPath
  condition: wolves == "evil"
```

---

### `goto_menu`

Returns to the app's main menu.

| Field | Type | Required | Description |
|---|---|---|---|
| `target` | string | yes | Always `main_menu` |

```yaml
- type: goto_menu
  target: main_menu
```

---

### `expand_link`

An in-place link that, when tapped, expands inline content at its position without navigating away. The expansion may include effects and navigation nodes.

| Field | Type | Required | Description |
|---|---|---|---|
| `label` | string | yes | Displayed link text |
| `state_affecting` | bool | yes | `true` if expansion should create a timeline snapshot |
| `expand_nodes` | list | yes | Nodes rendered inline when the link is tapped |

```yaml
- type: expand_link
  label: 2 Players.
  state_affecting: true
  expand_nodes:
  - type: effect
    var_sets:
      players: 2
  - type: goto
    target: PlayerNameIntro
```

---

### `include_passage`

Embeds another passage's content inline at this point. The included passage is rendered as-is; no timeline snapshot is created.

| Field | Type | Required | Description |
|---|---|---|---|
| `target` | string | yes | `passage_id` of the passage to include |

```yaml
- type: include_passage
  target: CommonHeader
```

---

### `section_heading`

A labeled section heading in a `hub` layout passage. Visually prominent; groups the `section_body` that follows.

| Field | Type | Required | Description |
|---|---|---|---|
| `text` | string | yes | Heading text (not a template; not interpolated) |

```yaml
- type: section_heading
  text: Board of Trustees
```

---

### `section_body`

Contains the body content for a section in a `hub` layout. Rendered below its corresponding `section_heading`.

| Field | Type | Required | Description |
|---|---|---|---|
| `nodes` | list | yes | Content nodes for this section |

```yaml
- type: section_body
  nodes:
  - type: text
    template: Each player may now purchase one building from the market.
  - type: break
  - type: link
    label: Continue...
    target: BuildPhase2
    state_affecting: true
```

---

### `setup_block`

A visually-distinct block for setup instructions. Rendered with a special style (card, sidebar, or inset depending on layout).

| Field | Type | Required | Description |
|---|---|---|---|
| `nodes` | list | yes | Setup instruction nodes |

```yaml
- type: setup_block
  nodes:
  - type: text
    template: SETUP
    style: bold
  - type: paragraph_break
  - type: text
    template: Place the hospital token on space 1 of the hospital track.
```

---

### `input_prompt`

Presents a modal input panel to collect a string or number from the player. The submitted value is stored in a session variable and persisted in the timeline snapshot.

| Field | Type | Required | Description |
|---|---|---|---|
| `prompt_id` | string | yes | Stable identifier for this prompt; must be unique within the module |
| `text` | string | yes | Instruction shown in the input panel |
| `input_type` | string | yes | `string` or `number` |
| `store_in` | string | yes | Session variable name to receive the submitted value |
| `resume_passage` | string | no | Passage to re-enter after submission (usually the same passage, for the resume branch) |

```yaml
- type: input_prompt
  prompt_id: Feverheart
  text: Count up all the heart tokens collected by ALL players. Please enter the total number here.
  input_type: number
  store_in: charitytotal
  resume_passage: Feverheart
```

---

### `set_location`

Updates the location indicator shown in the app header chrome.

| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string | no | Location display name |
| `icon` | string | no | Asset URI for the location icon, e.g. `icon://village` |

```yaml
- type: set_location
  name: The Hospital
  icon: icon://hospital_icon
```

---

### `setup_notification`

Triggers a floating setup notification card. Displayed as an overlay with instructions before the player proceeds to the next passage.

| Field | Type | Required | Description |
|---|---|---|---|
| `title` | string | no | Notification card title |
| `text` | string | no | Notification body text |
| `next_passage` | string | no | Passage navigated to after the player dismisses the notification |

```yaml
- type: setup_notification
  next_passage: Scoring
```

---

### `check_progress`

Validates that the player has reached the expected point in the passage graph. Prevents skipping ahead by direct passage navigation.

| Field | Type | Required | Description |
|---|---|---|---|
| `current_passage` | string | yes | The passage where this check is placed |
| `target_passage` | string | yes | The passage that must have been visited previously |

```yaml
- type: check_progress
  current_passage: Hospital3
  target_passage: Hospital2
```

---

### `checkpoint`

A named milestone in the timeline. Creates a labeled marker in the timeline scrubber; also used by the test runner for assertion points.

| Field | Type | Required | Description |
|---|---|---|---|
| `id` | string | yes | Stable checkpoint identifier |
| `display_label` | string | no | Human-readable label shown in the timeline scrubber |
| `diagnostic_label` | string | no | Machine-readable label for test assertions and save-file diagnostics |

```yaml
- type: checkpoint
  id: generation_2_complete
  display_label: Generation 2 Complete
  diagnostic_label: gen2_end
```

---

### `modal`

Displays a full-screen modal panel with instructional content. Used for generation-end summaries and special events.

| Field | Type | Required | Description |
|---|---|---|---|
| `chrome` | string | no | Modal border/decoration style |
| `body` | string | no | Main body text |
| `round` | int | no | Generation/round number displayed in the modal header |
| `next` | string | no | Passage to navigate to when the modal is dismissed |
| `instruction` | string | no | Additional instruction text |

```yaml
- type: modal
  chrome: EndOfGeneration
  round: 3
  next: Scoring
```

---

### `end_of_generation`

Signals the end of a generation phase to the engine. Triggers end-of-generation UI and may suspend the session until the physical game phase is resolved.

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

## 9. Source Annotations

Extracted passage files include YAML comments injected by the extractor. These are informational only; the engine ignores them.

```yaml
# TheCostofDisease.cs:29539       ← method declaration line
---
format: mws/1.0
passage_id: Expedition3
...
nodes:
# TheCostofDisease.cs:29543       ← first source line producing this node
- type: text
  template: The Expedition Uncovers...
```

---

## 10. Complete Example

A real extracted passage from *A Time of War* demonstrating `switch`, `let`, `text` with `lets`, and `link`:

```yaml
# ATimeOfWar.cs:9247
---
format: mws/1.0
passage_id: BattleTime
title: BattleTime
layout: narration
nodes:
# ATimeOfWar.cs:9251
- type: text
  template: Glory and Recognition
  style: bold
- type: break
# ATimeOfWar.cs:9255
- type: switch
  on: players
  cases:
  - match: '>4'
    nodes:
    - type: let
      var: _rnd_BattleTime_0
      random:
        type: choose-one
        values:
        - '{nameA}'
        - '{nameB}'
        - '{nameC}'
        - '{nameD}'
        - '{nameE}'
        seed_key: BattleTime_0
  - match: '>3'
    nodes:
    - type: let
      var: _rnd_BattleTime_0
      random:
        type: choose-one
        values:
        - '{nameA}'
        - '{nameB}'
        - '{nameC}'
        - '{nameD}'
        seed_key: BattleTime_1
  - default: true
    nodes:
    - type: let
      var: _rnd_BattleTime_0
      random:
        type: choose-one
        values:
        - '{nameA}'
        - '{nameB}'
        seed_key: BattleTime_3
# ATimeOfWar.cs:9278
- type: let
  var: _rnd_BattleTime_1
  random:
    type: choose-one
    values:
    - I alone deserved recognition for my glorious, bombastic, chimerical, transformative
      creations that defied the rigors of known science. This was the era of {_rnd_BattleTime_0} III!
    - The fate of the world rested in my hands.
    - Fame meant little to me if it did not carry with it the power to alter the world.
    seed_key: BattleTime_4
- type: text
  template: '{_rnd_BattleTime_1}'
  lets:
  - _rnd_BattleTime_1
- type: paragraph_break
# ATimeOfWar.cs:9292
- type: text
  template: All players take all their {icon:s3_weapontoken} tokens into their hands.
- type: paragraph_break
# ATimeOfWar.cs:9320
- type: link
  label: Click to begin the battle...
  target: BattleCompleteReturn
  state_affecting: true
- type: break
```

---

*MWS format v0.1 — MasterWork project. This document describes the format as produced by `MasterWork.Extractor` and consumed by `MasterWork.Engine`.*
