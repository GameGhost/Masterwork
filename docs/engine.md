# Masterwork Engine — Session Model, Randomness, and Popup Transactions

Reference doc for `Masterwork.Engine`'s session/rewind model, seeded randomness, and the popup
sandbox-transaction mechanism. Complements `docs/mws-format-latest.md` (the passage/node format
itself) — this doc covers runtime behavior, not YAML shape. Keep it current alongside
`GameSession.cs`/`VariableStore.cs`/`SessionPrng.cs`; when in doubt, the code's own doc comments on
those three types are the ultimate source of truth and are kept detailed for exactly this reason.

---

## 1. Timeline and View State

Two layers, cleanly separated:

- **Timeline** (`GameSession.Timeline`, a list of `SessionSnapshot`) — the only durable state.
  Immutable, append-only during live play. Each entry captures the complete session variable store,
  the PRNG's per-seed-key occurrence counters, the passage about to render, and display/diagnostic
  labels — all as of **just before** that passage renders (not after). `HistoryIndex` is the current
  position; `Current`/`CurrentRender` read `Timeline[HistoryIndex]`/a cached render of it.
- **View state** (`GameSession.ViewState`) — mutable, transient, UI-facing only (input drafts,
  in-progress edits). Reset on every navigation and every rewind step. Never serialized as part of
  durable session state beyond what's already been committed to a variable.

Passage-scoped `let` variables belong to neither layer — evaluated fresh on every render from the
PRNG sequence and the live variable store, discarded immediately after.

### What creates a snapshot

A `link`/`popup` close creates a new timeline entry when `state_affecting`/`snapshot` resolves
`true` (see `mws-format-latest.md` §6 `link`/`popup`/`goto` for the exact field/precedence rules,
including a `goto` inside `onclick`/`onclose` overriding the enclosing action's own value). Every
`assign` that ran to get there is bundled into that one snapshot — assigns don't get their own
timeline entries.

### Non-state-affecting navigation and `ActiveState`

A `link`/`popup` resolved as non-state-affecting still fully navigates (`GameSession.RenderInPlace`)
— it just doesn't bookmark it. The live edge tracks **at most one** such divergence as
`GameSession`'s private `_activeState` (`Session/ActiveState.cs`), shaped like a `SessionSnapshot`
(same "state as of just before this passage renders" convention) but never added to `Timeline` — a
later in-place transition simply overwrites it, it never accumulates its own history.

- `StepBack()`'s first press away from the live edge shows the active state's own **anchor**
  snapshot (the last real timeline entry) instead of consuming a real timeline step — and this
  survives stepping back through any further amount of real history; it is **not** discarded by
  `StepBack` itself.
- `StepForward()`/`JumpToPresent()` restore the active state directly when arriving back at the live
  edge, rather than replaying whatever chain of in-place transitions produced it.
- Only `ResumeFromHere()` (branching live play from a historical point) or a new state-affecting
  navigation (which supersedes it with a real snapshot) discard it.
- Persisted through `SessionSave.ActiveState`, so resuming a save taken mid-chain doesn't lose it.

This is what makes an automatic multi-step chain (e.g. a run of tie-break rounds, or several
sequential `goto`s) rewind cleanly to "the real decision point that led here" in one step, rather
than either losing the in-between state entirely or bloating the timeline with bookmarks nobody
asked for.

### Recovery from a failed navigation

`FollowLinkAsync`/`ClosePopupAsync` commit input drafts and `onclick`/`onclose` side effects to the
live store **before** attempting to render the destination — so a render failure partway through
(e.g. a target passage that doesn't exist) can leave the live store ahead of what `Current`/
`CurrentRender` still reflect. `GameSession.RecoverFromFailedNavigation()` rolls the live variable
store and PRNG back to the last successfully-committed entry and re-renders it — recovering to the
active state if one was already pending and showing before the failed attempt, not the bare anchor.
Safe to call even when nothing failed (a no-op that just recomputes current state).

### `app::gameover` sentinel

A `target` field can resolve to the fixed literal `app::gameover` instead of a passage id (see
`mws-format-latest.md` §4.1). `FollowLinkAsync`/`ClosePopupAsync` special-case it: any `onclick`/
`onclose` logic (including a `record` achievement trigger) still runs and commits normally, but the
navigation itself doesn't happen — `GameSession.IsGameOverRequested` is set instead, and the App
takes over from there (autosave deletion, playthrough-memory recording, return to main menu).

---

## 2. Randomness — Model B (Seeded Lazy)

`SessionPrng` (`Session/SessionPrng.cs`) derives each `seed_key`'s draw from
`(masterSeed, seedKey, occurrence#)` — a fresh `Random` seeded from a SHA-256 hash of that triple —
rather than mutating one shared `Random` instance per key. This is the whole reason timeline rewind
is exact and cheap: restoring position is just restoring an integer occurrence count per key
(`SessionPrng.SnapshotOccurrences`/`RestoreOccurrences`, captured in every `SessionSnapshot` and
`ActiveState`), never replaying draw history draw-for-draw.

Two operations, both deterministic given the same `(masterSeed, seedKey, occurrence#)`:

- `RandBetween(min, max, seedKey)` — integer in `[min, max]` inclusive.
- `Shuffled(items, seedKey)` — Fisher-Yates permutation.

Every call against a given `seedKey` advances that key's own occurrence counter, independent of
every other key — so two different `rand_between(...)` calls sharing a `seed_key` in the same
passage render draw different values (occurrence 0, then occurrence 1), while replaying the same
render from a restored occurrence count reproduces the exact same sequence.

---

## 3. Popup Sandbox Transactions

A popup's `content` (and `header`, and any layout chrome) is rendered **eagerly**, alongside the
rest of its enclosing passage, against a sandboxed clone of the variable store
(`RenderedPopup.Sandbox`) — never the live store directly. This is what makes showing/hiding a
popup a pure UI toggle with no engine call involved: nothing needs to be (re-)evaluated at the
moment the player actually opens it, since it was already rendered when the passage was. Only
`ClosePopupAsync(actionId, accept: true)` (Okay) touches the engine — committing the sandbox to the
live store, running `onclose`, and navigating, all as one transaction. Cancel discards the sandbox
untouched; the caller doesn't strictly need to call `ClosePopupAsync` for Cancel at all, since
nothing session-side happens.

**Trade-off, accepted deliberately:** an unopened or never-accepted popup's content still gets
evaluated, so a seeded random draw inside it is "spent" even if the player never opens it. Safe
because nothing else can mutate the live store while a popup sits unopened on an already-rendered
passage.

### The commit is an overlay, not a replace

`VariableStore.CommitChangesTo` applies only the session variables **the sandbox itself changed**,
relative to its own `Clone()`-time baseline (`_cloneBaseline`) — an overlay onto whatever the live
store's current state happens to be, not `RestoreSession`'s wholesale replace.

This matters because a popup node isn't necessarily the last thing in its passage's own top-level
node list: a top-level `assign`/`let` sibling positioned **after** the popup runs directly against
the live store during that same render, well before the player ever opens/accepts the popup —
`PassageRenderer.RenderPopup` only clones the sandbox at the point the popup node itself is reached,
it doesn't pause the rest of the node list. A wholesale replace at accept-time would silently
discard that later sibling's effect. Real bug, found via a player-submitted save file: *A Time of
War*'s `AdvancedWeaponryIntro` sets `sepinc1`/`sepinc2`/... in `assign` nodes positioned after its
own setup popup; accepting that popup used to wipe them before `Martial1` (which reads `sepinc1`)
ever rendered. Fixed by making the commit an overlay keyed off the sandbox's own before/after diff.

### Nested popups: baseline propagates from the outermost ancestor

A `popup` node inside another popup's own `content:` (§6's nested-popup pattern — commonly a
`reveal`-layout outer popup with no `okay`/`target` of its own, and an inner popup, or an ordinary
link, as the real way out) clones its sandbox from its **parent** sandbox, not the live store
directly — so a sandbox may itself already be a clone. `VariableStore.Clone()` propagates
`_cloneBaseline` from the **outermost** ancestor (`_cloneBaseline ?? currentState`), not reset to
the immediate parent's current state, so `CommitChangesTo` on the innermost sandbox still detects a
change an ancestor sandbox made *before* the nested clone point as a real change relative to the
true live store — not something already "baked in" and therefore invisible to a same-level diff.

Real bug, same shape as the one above but one level deeper: *A Time of War*'s `RumorD2` has an
outer `layout: reveal` popup whose content includes `assign rumor2 = "visited"`, ahead of a nested
`layout: setup` popup that's the player's only way to actually leave (the outer has no `okay` of its
own). Accepting only the inner popup used to never write `rumor2` back to the live store: the
inner's own baseline, cloned from the outer's state *after* that assign already ran, had `"visited"`
already baked in, so the inner's own before/after diff saw no change to commit — `rumor2` never left
the abandoned outer sandbox, and the once-per-game rumor kept reappearing forever. Fixed by
propagating the baseline from the outermost ancestor instead of resetting it at each nesting level.
`RestoreSession` still seeds a clone's *working* data from its immediate parent (so in-progress
ancestor mutations stay visible to the nested popup's own rendering) — only the baseline used for
the eventual commit diff changed.

### Leaving a popup via a link, not `ClosePopupAsync`

A popup with no `okay`/`target` of its own (the `reveal`-layout pattern above) is typically left via
an ordinary `link` inside its content, not a nested popup's Accept — `ClosePopupAsync` is never
called on it at all. `FollowLinkAsync` special-cases this: when the followed link lives inside a
popup's content, it commits that popup's sandbox (`CommitInputs` then `CommitChangesTo`) before
running the link's own `onclick`/target resolution against the live store — otherwise any `assign` a
`reveal` popup's content made before reaching the exit link stays trapped in the abandoned sandbox
forever, the same class of bug as the nested-popup case above, just reached through a link instead
of `ClosePopupAsync`. Because the baseline-propagation fix already makes a single `CommitChangesTo`
call reflect every ancestor's own accumulated changes regardless of nesting depth, committing just
the *immediately* enclosing popup's sandbox here is sufficient.

---

## 4. Expression Evaluation

Covered in full in `mws-format-latest.md` §4/§4.1 (this is the YAML-facing spec; not duplicated
here). Runtime notes worth keeping separate from the format spec:

- `ExpressionEvaluator` is whitelist-based — no arbitrary code execution. Expressions are parsed and
  validated at module load time, not first-use.
- Template expansion (`{varName}`, `{varName.property}`, `{arr[N]}`) and double-quoted string
  literal interpolation *inside* an expression (`"...{expr}..."`) share one implementation
  (`IExpressionEvaluator.ExpandTemplate`) — see `VariableStore.ExpandTemplate`'s remarks.
- Variable type coercion between `bool`/`int`/`string` follows fixed hoisting rules (Cradle's own
  runtime stores everything as strings; the extractor's type-conflict warnings — see
  `docs/extractor.md` — are what happens when a variable was genuinely used as more than one type
  across the original source).

---

*Masterwork project — companion to `docs/mws-format-latest.md` and `docs/extractor.md`.*
