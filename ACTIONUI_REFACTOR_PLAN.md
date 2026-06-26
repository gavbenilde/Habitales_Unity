# Habitales — Action UI Refactor: Build Plan & Work-Orders

**Companion to:** `HABITALES_ARCHITECTURE.md` (the Laws) and `HABITALES_SYSTEM_INVENTORY.md` (§G Actions, §L UI).
**Status:** Slices 0–2 **LANDED 2026-06-27** (Sonnet swarm, Opus-orchestrated). All C# written + cross-checked; **human Inspector wiring is pending** (see end). Slice 3 (rename) **dropped** — controller kept the `ActionBarUI` class name for GUID safety, so no rename needed. Two deviations from the original plan: (a) files stayed in `_GAME/Action/` (no `UI/` subfolder move — avoids `.meta`/GUID risk); (b) the controller is `ActionBarUI`, not a renamed `ActionFlowController`.
**Goal:** Break the monolithic action-bar UI into a **controller + passive views** so the scene hierarchy is independent of the scripts, and so onboarding/HUD bind to one stable surface instead of scene layout.

---

## The problem we're solving

`ActionBarUI.cs` (337 LOC, **live**) is one MonoBehaviour doing five unrelated jobs, and it reaches into the GameObject hierarchy by string (`transform.Find("ActionIcon")`, `Find("BoogleButton")`). Two consequences:

1. **Moving anything in the scene breaks it** — the script is welded to a specific hierarchy.
2. **Onboarding/HUD can't bind cleanly** — they either poll fields or depend on layout.

`ActionUI.cs` (907 LOC, **dormant legacy**) is the old tile-first flow, superseded by ActionBarUI and unwired as a driver (Inventory §G, Open Question #2). **Decision (this plan): delete it.**

### The five jobs tangled in ActionBarUI today

| # | Job | Methods |
|---|---|---|
| 1 | **Flow state machine** | `currentCategory`/`currentAction`, `ArmAction`, `Disarm`, `EnterSelectionFor`, TileSelector coordination |
| 2 | **Category tabs** | tab wiring → `SelectCategory` |
| 3 | **Action strip** | `RebuildActionStrip`, `SpawnActionCard`, `SpawnLockCard`, `RefreshCardHighlights`, stringly `Find`, Boogle wiring |
| 4 | **Estimates/brush/confirm** | `RefreshEstimates`, confirm interactable, brush controls |
| 5 | **Lock modal + onboarding surface** | lock modal; `OnActionArmed`/`OnActionConfirmed`; `GetArmCueRect`/`GetConfirmButtonRect` |

---

## The communication model (the core decision)

**Namespace ≠ communication.** A namespace is compile-time tidiness only; it does nothing at runtime. We use one (`Habitales.UI.Actions`, a sub-namespace of the existing `Habitales.UI`) **and** a runtime **Controller ↔ View contract** that is what actually decouples the hierarchy:

- **Downward (Controller → View):** direct method calls through `[SerializeField]` references with **loud-fail** (S4 + Law 3). Views are passive renderers — they never decide flow.
- **Upward (View → Controller):** C# `event Action<…>` raised **on meaning, not mutation** (Law 2) — `OnCategorySelected`, `OnActionCardClicked`, `OnConfirmClicked`. Never on raw field writes.
- **Cross-system:** the Controller reads `ActionManager.Instance` / `ResourceManager.Instance` (true singletons) and holds `TileSelector` via `[SerializeField]` (non-singleton). **All writes route through the owning system's methods** (Law 1).
- **To Onboarding/HUD:** the **Controller is the single public surface** — Law-1 getters + meaning events. Views never talk to onboarding.

**Rejected: a global event bus / ScriptableObject-event channel.** This is one small module talking to one controller. A bus would trade today's visible coupling for invisible coupling and an untraceable call graph. Direct controller/view events stay greppable.

**Why this gives "hierarchy independent of scripts":** each View holds *only its own widget refs* and lives on whatever GameObject you like. The Controller finds Views via `[SerializeField]`, never via `GetComponentInChildren` or transform paths. Rearrange the hierarchy freely as long as each View keeps its serialized refs.

---

## Target decomposition

```
namespace Habitales.UI.Actions   (folder: _GAME/Action/UI/)

ActionFlowController   (brain · MonoBehaviour)
    - owns: currentCategory, currentAction, arm/disarm state machine
    - refs:  ActionManager.Instance, ResourceManager.Instance, [SerializeField] TileSelector
    - refs:  [SerializeField] the views below (loud-fail each)
    - PUBLIC SURFACE (frozen — onboarding/HUD bind here):
        Law-1 getters:  PlayerAction CurrentArmedAction
        meaning events: event Action<PlayerAction> OnActionArmed
                        event Action               OnActionConfirmed
        cue-rect facade: RectTransform GetArmCueRect(PlayerAction)   -> delegates to views
                         RectTransform GetConfirmButtonRect()        -> delegates to view

ActionCategoryBar      (view) — 4 tabs; event OnCategorySelected(ActionCategory);
                                SetActiveCategory(ActionCategory?); RectTransform GetTabRect(cat)

ActionStripView        (view) — owns strip content + prefab; Render(IEnumerable<PlayerAction>);
                                event OnActionCardClicked(PlayerAction); event OnLockClicked;
                                Highlight(PlayerAction); RectTransform GetCardRect(PlayerAction)

ActionCardView         (per-prefab component) — [SerializeField] icon/label/boogleButton refs;
                                Bind(PlayerAction, onClick, onBoogle). KILLS every transform.Find().

ActionEstimatePanel    (view) — brush/confirm/estimate texts; Render(in ActionEstimates);
                                SetVisible(bool); event OnConfirmClicked; RectTransform GetConfirmRect()

LockModalView          (view, small) — Show()/Hide(). May fold into the controller if trivial.
```

`ActionEstimates` is a small `readonly struct` (tileCount, days, fatigue) the controller computes and hands down — keeps estimate math in one place (S2), keeps the view dumb.

---

## Migration sequence (safe — ActionBarUI is live and onboarding depends on it)

Each slice compiles and runs; the public surface stays frozen until the last step.

### Slice 0 — Delete legacy `ActionUI`
- Decouple `InspectModeManager` first: it currently uses `ActionUI.CurrentState == ActionPanelState.InspectMode` as its **only** state holder (it already owns the panel/selector/button refs). Give it its own `bool _inspectMode`; drop the `ActionUI` field and `FindObjectOfType<ActionUI>()`. Inspect mode becomes self-contained — ActionBarUI has no inspect concept, so nothing else regresses.
- Delete `ActionUI.cs` (+ `.meta`). Remove the dormant `actionUI` field on `RunManager` (Inventory §A notes it's already marked DORMANT).
- Mark `ActionUI` **REMOVED** in the inventory (§G + Open Question #2 resolved).

### Slice 1 — `ActionCardView` (lowest risk, biggest quality win)
- Add `ActionCardView` to the card prefab with serialized `icon` / `label` / `boogleButton` refs + a `Bind(...)` method. Replace ActionBarUI's `transform.Find("ActionIcon")` / `Find("BoogleButton")` with `card.GetComponent<ActionCardView>().Bind(...)`. Touches one prefab + the spawn path only. Public surface unchanged.

### Slice 2 — Extract views behind the existing ActionBarUI
- Pull tabs → `ActionCategoryBar`, strip → `ActionStripView`, estimates/confirm → `ActionEstimatePanel`, lock → `LockModalView`. ActionBarUI keeps its name + public API and becomes a thin orchestrator delegating to the new views. RunManager + OnboardingDirector keep working untouched.

### Slice 3 — Rename to `ActionFlowController` (optional, last)
- ⚠️ **GUID risk:** renaming a MonoBehaviour orphans its scene component (missing-script) in all three scenes (`0.unity`, `casdasda.unity`, `Vertical Slice.unity`) — the same trap hit by BooglePanelUI (Inventory §J). **Either keep the class name `ActionBarUI`** for the controller **or re-pin the `.meta` GUID** so the scene component survives. Update `RunManager.actionBarUI` + `OnboardingDirector.actionBarUI` only if the type name changes.

### Slice 4 — Inventory
- Log the new module in `HABITALES_SYSTEM_INVENTORY.md` §G with the add-date; flip the §L/§N `ActionUI` rows to REMOVED; resolve Open Question #2.

---

## Laws every slice carries (Architecture §1, verbatim intent)
- **Law 1 — getters not setters.** Views read via getters; writes go through the owning system's methods.
- **Law 2 — hooks fire on meaning, not mutation.** View→controller events fire on click/confirm, never on field writes.
- **Law 3 — loud-fail scales with distance.** Every artist-touched `[SerializeField]` ref loud-fails with `this` highlighting if unwired.
- **S2 — one concept, one place.** Estimate math lives in the controller, not duplicated across views.
- **S4 — one reference model per field.** Singletons via `.Instance`; views/TileSelector via `[SerializeField]`. Never mixed.

## Human wiring (agents write C#; they cannot wire prefabs)
Each slice ends by listing exact Inspector steps: add view components to their GameObjects, assign each view's own widget refs, assign the views onto the controller, assign `TileSelector`. Law-3 errors name any unwired ref.

## Open follow-ups (not blocking this refactor)
- The `OnPlantSpawnedTween(cubeTransform)` empty stub (Inventory §G/§K) is a cube-era orphan — delete during Slice 2.
- `RefreshEstimates` runs every frame in `Update`; fine for now, candidate for event-driven later (mirrors the `HealthBarUI` polling note, Inventory §L).
