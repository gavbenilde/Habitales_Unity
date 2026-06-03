# Habitales — Prototype Architecture & Design Memory

**As of:** May 11, 2026 | **Engine:** Unity (C#, URP-compatible) | **Scope:** 7-day prototype build
**Reading order:** This doc layers prototype intent on top of the April 5, 2026 reverse brief. If a system is not mentioned here, assume the reverse brief still applies. If a system is here, the prototype overrides apply.

---

## 1. Charter

**One-sentence promise:** A cozy-tense roguelite where you paint tropical land back to health with a small NGO team, wait to see who messages you about it, and try to hold it together before time runs out.

**Prototype's job:** Make 3 artists viscerally feel 4 things in 5–10 minutes — **paint, chat, unlock, lose-but-okay**.

**The 7-day rule:** Refactors are TODO notes, not detours. Daily check: *"Is this more playable than yesterday?"* Two consecutive nos means we're drifting.

---

## 2. Working Principles

- **Plan before coding.** Lay out the plan, ask clarifying questions, get sign-off, then implement.
- **Ask for files before proposing changes.** Don't guess method signatures or field names.
- **Minimal-scope changes.** Confine edits to the file in question when possible.
- **Post-implementation pass.** Compare against locked decisions; list deviations, edge case risks, ambiguous behaviors.
- **"I could clean this up while I'm here"** → make a TODO note, move on.
- **Forward-looking but earnest.** Capture watchouts. Surface side effects, guard clause behavior, and post-implementation wiring steps explicitly.

---

## 3. Current State vs. Prototype Target

The codebase is the post-April-5 build with `GameManager` already renamed to `RunManager` (Phase 0 complete). The reverse brief describes what *exists in code*. This doc describes what's *active for prototype* vs. dormant vs. forbidden.

Three buckets:
- **ACTIVE** — wired and shipping in the prototype.
- **DORMANT** — code remains in the project, do not delete, but is unwired or unused for prototype.
- **OUT OF SCOPE** — do not build, even if it seems easy. Refactor invitations go in a TODO.

---

## 4. Run Lifecycle (ACTIVE)

- `RunManager` (formerly `GameManager`) owns run-level state. Singleton, scene-scoped.
- **60 in-game day cutoff** replaces the 5-year (`daysPerYear × maxYears`) cutoff. The game-end check fires from `HandleTimeAdvanced` when `totalDays >= 60`.
- `bool isPrototypeRun = true` flag on `RunManager` gates prototype-only behavior. Production logic stays in code, branched.
- **Score = peak simultaneous thriving-tile count** (high-water mark, not cumulative). Tracked daily inside `HandleTimeAdvanced`.
- **First zone is deterministic** for artist predictability. Subsequent zones roll randomly from the 6 prototype profiles.
- **Zone unlock threshold lowered to ~60%** (tunable; `ZoneManager.GetTotalAverageHealth() >= 60`).
- Existing zone expansion + fire spawning + cascade pipeline preserved unchanged.

---

## 5. Architecture at a Glance

```
ResourceManager → WeatherManager (RollWeather on AdvanceTime / AdvanceTimeStepped)
ResourceManager → DayNightCycleHandler (OnTimeAdvanced → StartCycle)
ActionManager → TileManager, ResourceManager, DayNightCycleHandler (ResetForNewAction)
RunManager → ActionManager, TileManager, ZoneManager, ResourceManager,
             RunEndSnapshotUI, LevelUpScreenUI, PlayerProgressionSO
EventManager → ResourceManager (OnTimeAdvanced), ZoneManager (OnZoneGenerated)
DialogueManager → ResourceManager (AllWorkers, OnTimeAdvanced), ZoneManager (OnZoneGenerated)
ChatAppUI → DialogueManager (OnMessagesUpdated, OnUnreadChanged)
ActionUI → ActionManager, TileSelector
TileSelector → TileManager
ZoneManager → TileManager, ResourceManager
PlantedCubeEntity → PlantingProfileSO (color, stat array, tier scaling)
PersistenceService → PlayerProgressionSO (load on boot, save on run-end)
```

Communication remains C# events (`event Action<T>`), not Unity messages. `EventContext` static token bus unchanged.

---

## 6. Active Systems (Prototype-Wired)

### 6.1 Tile System — `TileManager`, `Tile`, `TileStats`, `TileVisualizer`

Unchanged from reverse brief.

- **All tiles default to `isAnalyzed = true`** at spawn. The reveal gate is effectively disabled.
- `TileStats` UI (per-tile colored-bar readout for the 6 substats; **note: distinct from `RegionHealthUI`, which is per-zone — flagged for post-prototype rename/consolidation pass**) is the always-visible substat readout. Bars only, no numbers. Already in the final build.
- Cascade, health formula, overlay state machine, contamination tint: all unchanged.

### 6.2 Zone System — `ZoneManager`, `ZoneProfile`

- **6 prototype `ZoneProfile` assets**, one per soil substat — each profile starts its tiles with one degraded substat as the headline issue:
  - Nutrient Balance
  - Soil Organic Matter
  - Soil Structure
  - Biological Activity
  - Water Dynamics
  - Erosion Resistance
- First zone is deterministic (artist-predictable starter). Subsequent zones roll randomly.
- Unlock threshold: ~60% average health.
- Generation pipeline (flood-fill blob, theme rolls, issue assignment, building placement, organic entity placement, worker reward, `OnZoneGenerated`) unchanged.
- Fire spawning via `VillageEntity.SpawnKainginFire()` preserved — fires must exist for Fire Suppression to mean anything.

### 6.3 Resource & Time — `ResourceManager`

- Unchanged.
- `AdvanceTimeStepped` remains the canonical path for real player actions.
- `OnGameOver` event repurposed: fires when `totalDays >= 60` in prototype runs.
- Worker fatigue, recovery, weather-K, named roster: unchanged.

### 6.4 Weather — `WeatherManager`

- Code unchanged.
- **Prototype runs lock weather to Sunny.** Implementation can be a `RollWeather` short-circuit when `RunManager.isPrototypeRun == true`. Forecast UI is out of scope, so no display layer.
- Multipliers, `weatherK`, fire bonuses still applied (Sunny path).

### 6.5 Day-Night Cycle — `DayNightCycleHandler`

Unchanged. Critical for `AdvanceTimeStepped`'s `WaitUntil(IsIdle)` gate.

### 6.6 Event System — `EventManager`, `EventPopupUI`, `GameEventSO`, `EventContext`

- Framework remains live for prototype because **kaingin fire events fire from `VillageEntity`** and the player needs the "fire just started" interruption to feel the chaos.
- The `linkedThread` field on `GameEventSO` is **kept null in prototype assets** — DialogueThreadSO authoring is out of scope, so any event that would have appended a chat thread simply does nothing on that field. Documented so Claude Code doesn't try to wire it.
- `IsEventPaused` still guards `HandleActionCompleted` cascade/zone-unlock. The architectural gap (no guard on `ActionManager.ExecuteAction` entry) remains a known stub.
- `EventContext` tokens still resolve; the live ones are unchanged.

### 6.7 Dialogue System — `DialogueManager`, Chat App

- Massively simplified for prototype.
- **40% chance per in-game day** (replaces the 13% gate): append a generic flavor message from a random worker with `actionsParticipated > 0`. Cooldown logic from the reverse brief can stay or be relaxed — the daily roll dominates.
- **Tab filtering:** `ChatAppUI` only shows tabs for workers/threads that have at least one message in history. The current behavior of listing all available workers is overridden in prototype.
- **Tier 1 Azi (first-use of a plant):** hardcoded `string.Format` — *"This is {plantName}! They can survive extreme {stat}."*
- **Tier 2 Azi (plant death):** hardcoded `string.Format` — *"The plant died. They could only survive extreme {stat}."* Stat = the plant's specialty (highest entry in the DnD array).
- **Tier 3 Azi:** replaced by the run-end speech bubble (see §6.10). Not a chat thread.
- All Azi messages are inline hardcoded — **no `DialogueThreadSO` authoring**.

### 6.8 Action System — `PlayerAction`, `ActionManager`, `ActionUI`

**Active actions (6 total):**

| Action | Category | Notes |
|:---|:---|:---|
| Plant A (procgen-shaped, hand-tuned) | Intervene | DnD array, HWB cube entity |
| Plant B (procgen-shaped, hand-tuned) | Intervene | DnD array, HWB cube entity |
| Plant C (procgen-shaped, hand-tuned) | Intervene | DnD array, HWB cube entity |
| Plant D (procgen-shaped, hand-tuned) | Intervene | DnD array, HWB cube entity |
| Fire Suppression | Emergency | Unchanged from reverse brief |
| Remove Withered (Remove Plant) | Cleanup | **New action.** Removes grayed-out dead `PlantedCubeEntity` |

**Category structure preserved** (Examine / Intervene / Emergency / Cleanup) for UI scaffolding consistency. Empty categories show only the lock card.

**Docked-card brush flow (new in prototype):**
1. Player picks category in `ActionUI`.
2. Player picks action → the action card **enlarges and docks to the side**; HUD hides.
3. Player single-clicks a tile → `TileSelector` enters flood-fill mode from that seed.
4. Slider drives radius / tile count.
5. Confirm/Cancel buttons commit or back out.
6. Multi-seed drag-painting deferred — single seed only for prototype.

**Lock card:** A single generic gray card with a lock icon appears at the end of each category list. Click → modal: *"Level up to unlock more techniques. [Continue]"* No mechanics beyond the modal.

**Boogle "?" button on each action card** → opens `BooglePanelUI` (a dedicated full-screen panel, not a hover tooltip) with the plant's name and specialty body text. Mental model: opening an "app" to look up a plant's info, like a Google search. Post-prototype the panel is expected to migrate into the **Tablet UI shell** alongside chat, messaging, and other in-world apps — so its singleton/standalone canvas pattern is intentionally lightweight and easy to re-parent.

**Action coroutine, fatigue, weather work-speed multiplier:** unchanged.

### 6.9 Planting Profile System (NEW) — `PlantingProfileSO`, `PlantingProfileGenerator`, `PlantedCubeEntity`, `HWBColor`

Lives under `Assets/SO/Planting/` and `Assets/Prototype/Scripts/`.

- **`PlantingProfileSO`** — the schema for both hand-tuned starters and procedural unlocks.
  - 6-element float array (the "DnD array"), balanced budget, power-law distribution — one specialty stat with the rest tapering.
  - HWB color: `hue` and `blackness` locked at profile generation, `whiteness` lerps at runtime based on tile health.
  - `tierCount` (2–4 per profile) — cube scales lerp evenly. 4-tier example: 0.25, 0.5, 0.75, 1.0. Final size always 1.0.
  - `plantName`, `profileID`, `seed` (for procedural plants).
- **Interpretation C** — the DnD array drives **both survival thresholds AND daily stat contributions**. The plant that survives extreme [X] is also the plant that improves [X]. Single source of truth, no parallel tables.
- **`PlantingProfileGenerator`** — produces procedural profiles. Same shape as starters; only the array values and color differ.
- **`PlantedCubeEntity`** — extends `TileEntity`. Replaces the seedling/sapling/tree pipeline. On `OnDailyUpdate`:
  - Reads tile substats, checks against survival array.
  - If surviving: contributes to substats per the array; advances tier when conditions allow.
  - If dying: grays out (final tint), marks itself withered, stops contributing. Removable via Remove Withered action.
- **`HWBColor`** — C# utility converting HWB → RGB for the cube's material color. No shader work.
- **Procedural unlocks are unbounded** — 50+ profiles across 30 runs is fine. Stored as profile IDs (or seeds) in `PlayerProgressionSO` / JSON save.

### 6.10 Run-End + Level-Up (NEW) — `RunSnapshot`, `RunEndSnapshotUI`, `LevelUpScreenUI`, Azi speech bubble

**Two separate screens.** Run-End is part of the game session; Level-Up is between run and main menu.

**Sequence on run-end:**
1. `RunManager.TriggerGameOver(reason)` fires.
2. **Azi speech bubble** appears first (replaces Tier 3 dialogue thread). Inline hardcoded message.
3. Speech bubble dismissed → **Run-End Snapshot Screen** shows.
4. Player continues → **Level-Up Screen** shows.
5. Level-Up Screen fades to main menu.

**`RunSnapshot`** data:
- `peakThrivingTileCount` (the score, high-water mark)
- `peakThrivingScreenshot` — `Texture2D` captured via `ScreenCapture.CaptureScreenshotAsTexture()` at the moment of peak thriving (re-captured each time the peak is exceeded).
- `endReason`, `totalDays`, `worldHealth`, `topWorker` — carried over from existing `EndGameData`.

**`RunEndSnapshotUI`:** Displays the screenshot + stat breakdown. Built fresh, but can reuse `EndGameScreenUI` infrastructure where convenient (zone pills, Employee of the Year portrait).

**`LevelUpScreenUI`:**
- XP bar lerps 0 → parent width.
- On level-up: popup VFX + SFX, image fades in/out at center.
- Unlock card revealed if a new plant profile is unlocked at this level.
- Fades to main menu on continue.
- **XP thresholds (tunable):** Level 2 = 10, Level 3 = 25, Level 4 = 50.

### 6.11 Persistence (NEW) — `PlayerProgressionSO`, JSON save

- **`PlayerProgressionSO`** is the **schema** — fields and defaults. Runtime mutation does not persist across sessions in builds.
- Actual save data lives in **JSON at `Application.persistentDataPath`**.
- Saved fields: `level`, `totalXP`, `unlockedPlantProfileIDs` (or seeds for procedural plants).
- **Save on run-end**, after Level-Up Screen processing.
- **Load on game start**, populates the in-memory `PlayerProgressionSO` instance.
- Persistence layer keeps the SO/JSON split explicit so Claude Code doesn't try to "fix" the SO to persist directly.

### 6.12 Visual Systems

- `TileStats` (per-tile colored-bar substat UI) — **already in the final build**, reused as the always-visible substat readout. Bars only, no numbers.
- `RegionOutlineRenderer`, `RegionHealthUI` (per-zone), `ResourceDisplay`, `EntityVisualizer`: unchanged.
- Cube visuals handled by `PlantedCubeEntity` driving a Unity primitive cube renderer with HWB-derived material color.

### 6.13 Polish (Phase 5 scope)

- Cube spawn tween (LeanTween, scale-up on plant).
- Subtle tile heal feedback (color pulse or ripple on substat improvement).
- Level-up SFX + popup VFX.
- Bar fill animation.
- Everything else: gray boxes are fine.

---

## 7. Dormant in Code — Do Not Delete

These exist in the codebase. They are unwired or unused in the prototype but stay intact for post-prototype work.

| System | Status |
|:---|:---|
| Tree / Sapling / Seedling entity pipeline | Disabled. Replaced by `PlantedCubeEntity`. Threshold-based logic + planned health-drift redesign both preserved on disk. |
| `AnalyzeSoilSampleAction` | All tiles default `isAnalyzed = true`, so the action is unused. |
| `EcologicalSurveyAction` | Issues are not a player-facing problem in prototype. Unused. |
| `InspectTrashAction` | Trash is not a player loop in prototype. Unused. |
| `ClearTrashAction` | Unused (no trash loop). |
| `StumpDeadTreeRemovalAction` | Unused. Distinct from the new Remove Withered action. |
| `CreateFirebreakAction` | Unused (Fire Suppression is the only Emergency action live). |
| `CoverCroppingAction` + cover crop entities | Unused. |
| `PlantTreesAction` (old) | Unused. Replaced by the 4 plant actions. |
| `ApplyFertilizerAction` | Unused. |
| Sticker system (`StickerSO`, `StickerLibrary`, sticker tray, trait responses) | Disabled. |
| Birthday system + birthday group shoutout | Disabled. |
| Worker message randomizer (13% gate + cooldown + template selection) | Replaced by the 40% daily flavor message. Code can stay; the new behavior overrides. |
| `WorkerMessageTemplateSO` authoring path | Unused. |
| `DialogueThreadSO` authoring path (general) | Unused. Tier 1/2 Azi is hardcoded. |
| Worker traits (mechanical effects beyond flavor) | Disabled. Trait field on `Worker` stays for display. |
| `FactoryEntity` daily logic | Was already a stub; remains stubbed. Factory placement still allowed but inert. |
| Trash entities (`TrashBioEntity`, `TrashNonBioEntity`) | Spawn paths can stay; no player loop interacts with them. |
| `BurningVillage` entity | Was never implemented; remains absent. |

---

## 8. Out of Prototype Scope — Do Not Build

Refactor invitations go in a TODO note. Do not introduce these even if they seem trivially small.

- Intensity slider (Quick / Standard / Thorough)
- Give-and-take warnings on action confirm
- Research Points UI / HUD
- Concurrent actions / Occupied worker state
- Soil substats as numerical readouts (colored bars only — already in build)
- Weather forecast UI / 14-day calendar
- Weather App phone tab
- Team App phone tab
- Multi-seed drag-painting (single seed flood-fill only)
- Data-snapshot for gallery (literal `ScreenCapture` only)
- `GameSignals` static event channel (hardcode subscriptions inline)
- `DialogueResponder` MonoBehaviour (hardcode inline)
- Tier 3 Azi as a chat thread (replaced by speech bubble)
- Contamination as a player-facing problem
- Standalone Phytoremediation, Plant Nitrogen-Fixing Species, Containment Trench, Emergency Erosion Barrier actions
- Issue reveal gating UI changes
- Firebreak auto-removal logic
- Main menu polish beyond the level-up fade target
- `GameEventSO` asset authoring for new events
- `DialogueThreadSO` asset authoring for any new threads

---

## 9. Known Stubs / Hard TODOs (Carried Over)

| Location | Status | Notes |
|:---|:---|:---|
| `RunManager.AbortCurrentAction()` | Stubbed | Partial tile progress on interrupt not applied. Acceptable for prototype. |
| `IsEventPaused` → `ActionManager` | Partial | Guards `HandleActionCompleted` only, not `ExecuteAction` entry. Acceptable for prototype. |
| `FactoryEntity.OnDailyUpdate()` | Stubbed | Acceptable for prototype. |
| `VFXManager` | Shell | Level-up VFX may route through it; otherwise no callers. |
| `BooglePanelUI` | Standalone canvas | Lives as its own singleton overlay in prototype. Post-prototype: migrates into the Tablet UI shell as one of several in-world "apps" (chat, messaging, plant lookup, etc.). The current implementation is deliberately self-contained so re-parenting under a Tablet root requires no refactor. |

---

## 10. Folder Structure

```
Assets/SO/Planting/                  PlantingProfileSO assets (starters + generated)
Assets/SO/Progression/               PlayerProgressionSO schema
Assets/SO/Zones/Prototype/           6 substat-themed ZoneProfile assets
Assets/Prototype/Scripts/            Prototype-scoped C# (planting, profile gen, run snapshot)
Assets/Prototype/UI/                 Prototype UI (run-end snapshot, level-up screen, docked card)
Assets/_GAME/Prototype/              Top-level prototype directory; CLAUDE.md lives here
```

---

## 11. Phase Plan Reference

| Day | Phase | Focus |
|:---|:---|:---|
| 1 | 0 + 1a | Rename (DONE), folders, stubs, 60-day cutoff, peak-thriving tracking, screenshot capture |
| 2 | 1b + 1c | Run-End Snapshot screen + Azi speech bubble + Level-Up Screen |
| 3 | 2 + 3a | JSON persistence + first plant working end-to-end |
| 4 | 3b | Profile generator + 4 starters + Boogle `?` panel + Tier 1/2 Azi |
| 5 | 3c + 4 | Docked-card brush mode + lock card + 40% daily worker chat + tab filtering |
| 6 | 5 | Painting polish + level-up SFX/VFX + tile heal feedback |
| 7 | 6 | Playtest, Sentence Test, fix worst feel issue, ship |

---

## 12. Naming + Conventions

- `RunManager` is the prototype-temporary name for the former `GameManager`. Production may rename back or split further; flagged.
- `isPrototypeRun` bool on `RunManager` gates prototype-only behavior. Production logic stays branched, not deleted.
- Zone and Region are interchangeable terms in the current codebase. **Flagged for post-prototype consolidation pass** (see TileStats vs. RegionHealthUI note in §6.1).
- New prototype code lives under `Assets/Prototype/` or `Assets/SO/`. Avoid sprinkling prototype-only files into core gameplay folders.

---

## 13. The Sentence Test (Day 7)

The playtest north star: a player should be able to describe what they did in one sentence that touches all four feelings — *"I painted some plants, a worker messaged me about it, I unlocked a new one, and then I ran out of time but it was okay."*

If the sentence doesn't form, the feel issue is in whichever feeling failed. Fix that one thing on Day 7.
