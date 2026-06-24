# Habitales — System Inventory (companion to HABITALES_ARCHITECTURE.md)

> ⚠️ **CONTRIBUTION RULE (team discipline — read before you ship code).**
> **Every new feature, system, script, or content type MUST be logged in this file *immediately* — in the same change that adds it — and the entry MUST record the date it was added** (absolute, e.g. *"added 2026-06-24"*). This is not optional cleanup for "later." The inventory is the map everyone (human or agent) reads *before* touching anything; if new work isn't logged the moment it lands, the map rots and the next person builds on a false picture. Put the row in the correct lettered section (A–P) with Source / LOC / Status / notes, and either tag the row with its add-date or add a dated *"Targeted update YYYY-MM-DD"* callout like the §I dialogue rework. If you delete something, mark it **REMOVED** here — don't just drop the row.

**Purpose:** The architecture doc says *what should be*. This says *what exists, where, and whether it's live* — the map a subagent reads before porting anything, so it never reproduces dormant behavior or treats the running build as legacy.

**Last sweep:** 2026-06-06 (#2) — **agent-swarm resweep**: every `_GAME/**` + relevant `_UTILITIES/**` C# file was re-read in full by a dedicated sub-agent and reported back; the five largest files (RunManager, RegionManager, TileManager, ActionUI, TileSelector) were each read by **two independent agents and cross-checked** (all agreed on substantive facts). LOCs below are now **exact**, not approximate. This sweep corrects several stale claims from sweep #1 — most importantly: the **event-pause entry-gate is now CLOSED** (in `ActionManager`, not `RunManager`); **`ActionUI` is now the LEGACY flow and `ActionBarUI` is the active driver**; **`VFXManager` is ACTIVE, not a shell**; the **Inspect/Examine loop is LIVE, not dormant**; and **all RegionManager spawns now route through `SpawnById`**. **Files deleted on disk are recorded as REMOVED, not DORMANT.**

**How to read a row:** `Source` = where the authoritative code lives. `Status`:
- **ACTIVE** — wired and shipping in the current prototype build.
- **DORMANT** — code remains on disk, do **not** delete, but is unwired/unused for the prototype. *Do not port as if live.*
- **NEW** — renovation/prototype code that did not exist pre-renovation.
- **SCAFFOLD** — renovation skeleton/seam, on disk but not yet load-bearing. Real wiring lands in a later phase.
- **SUPPORT** — small types/helpers a system depends on.
- **REMOVED** — was on disk at an earlier sweep, now deleted. Listed so a stale reference doesn't resurrect it.

**The three-source model (see HABITALES_ARCHITECTURE.md → Sources of Truth):**
1. `_GAME/Prototype/CLAUDE.md` — authoritative **design/behavior spec** (May 11 2026). Read first for ACTIVE/DORMANT status + overrides.
2. `_GAME/**` (live code) — authoritative **implementation** of the spine + most systems, prototype-active via `isPrototypeRun` branches. **Not legacy.**
3. `_GAME/Scripts/**` — the **NEW renovation tree** (`Habitales.Core/Entities/Actions/Meta/...`). Phase 0–1 has landed here; the entity sub-tree is now **load-bearing** (all spawns route through it), but several pieces (`ActionSO`, `IEntityEventSink`, `WeatherVFXController`) remain SCAFFOLD until later phases wire them in.

All paths below are relative to `Habitales/Assets/`. **LOC is exact as of this sweep.**

> 🗂️ **Targeted update 2026-06-24 — Folder reorganization (paths below are pre-reorg; translate via this table).** The parallel `Scripts/` renovation tree and the per-feature `_API` subfolders were **dissolved**, and all ScriptableObjects moved to a new top-level `_SO/` (sibling of `_GAME` and `_ART`). Code now lives in flat feature folders under `_GAME/`. **No GUIDs changed** (every `.meta` moved with its file), so all scene/prefab/asset references survive. **Source-column paths in the rows below have NOT yet been rewritten — apply this translation:**
>
> | Old path (as written below) | New path |
> |---|---|
> | `_GAME/<Feature>/_API/X.cs` | `_GAME/<Feature>/X.cs` (e.g. `Tile/_API/TileManager.cs` → `Tile/TileManager.cs`) |
> | `_GAME/_API/RunManager.cs` | `_GAME/Core/RunManager.cs` |
> | `_GAME/Scripts/Core/*` | `_GAME/Core/*` (GameBootstrap, GameLog, IEntityEventSink, StatTypes) |
> | `_GAME/Scripts/Entities/**` | `_GAME/Entities/**` (incl. `Behaviours/`) |
> | `_GAME/Scripts/Meta/RunEndCoordinator.cs` | `_GAME/Results/RunEndCoordinator.cs` |
> | `_GAME/Scripts/Actions/ActionSO.cs` | `_GAME/Action/ActionSO.cs` |
> | `_GAME/Scripts/UI/**` | `_GAME/UI/**` (incl. `Messaging/`, `Narrative/`, `GameplayHudGate.cs`) |
> | `_GAME/Scripts/Editor/ConversationEditor.cs` | `_GAME/Dialogue/Editor/ConversationEditor.cs` |
> | `_GAME/Scripts/Onboarding/**` | `_GAME/Onboarding/**` |
> | any `*.asset` (SO) under `_GAME/...` | `_SO/{Entities,Dialogue,Events,Regions,Action,Progression}/...` |
>
> **Final `_GAME/` feature folders:** Action, Core, Dialogue (+`Editor/`), Entities (+`Behaviours/`), Events, Onboarding (+`CoachMarks/`,`Juice/`), Prototype, Results, Tile, UI (+`Messaging/`,`Narrative/`), Utilities, Weather, Zone. The `Habitales.*` namespaces were **left intact** (folder no longer mirrors namespace — accepted tradeoff; the empty `_NamespaceAnchor.cs` stubs were deleted). Abandoned: the `Scripts/` tree and all `_API/` subfolders. **Orphans left in place** (reference deleted scripts — clean up later): `_GAME/Dialogue/Tabs/*`, `_GAME/Dialogue/Threads/*` (old DialogueTab/Thread model), `_GAME/SO/Planting/_Archive/*` (cube plants).
>
> ⚠️ **Still undocumented in the rows below — owe a full sweep:** the **Onboarding** subsystem (`_GAME/Onboarding/**`, ~16 files: `OnboardingDirector`, `OnboardingBootstrap`, `OnboardingContent`, `OnboardingBeatId`, `DragGhostInset`, `CoachMarks/*`, `Juice/*`), the **Narrative popup** UI (`_GAME/UI/Narrative/*`: `NarrativePopupManager`, `DialoguePopupView`, `SideNarrativeBubble`, `PopupStyle`), and the **Messaging** UI (`_GAME/UI/Messaging/*`: `MessagingAppIconUI`, `RibbonUI`) + `_GAME/UI/GameplayHudGate.cs`. These are `Habitales.Onboarding`/`Habitales.UI` and were never added to a lettered section — they need proper rows.

---

## A. Core / Run

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **RunManager** | `_GAME/_API/RunManager.cs` | **765** | ACTIVE · singleton · `[DefaultExecutionOrder(-100)]` | Global namespace. Day-advance heartbeat is `HandleTimeAdvanced(int days)` — a per-day `for` loop whose **exact order** is: (1) assemble `TickContext` + `tileManager.UpdateAllEntities(in ctx)` → (2) `CascadeTileUpdates()` → (3) `EvaluateThresholds()` → (4) `CheckCollapseCondition()` → (5) `TryFlagRegionUnlock(GetTotalAverageHealth())` → (6) `EvaluateThrivingPeak()` → (7) `healthHistory.Add` → (8) prototype day-cap `TriggerGameOver` if `TotalDays >= prototypeRunDays` → (9) `tileManager.RefreshAllVisuals()` → (10) `OnDayResolved?.Invoke`. **NOTE the entity-tick runs BEFORE cascade** (arch §2.1 lists cascade as step 3 *after* entity update — code matches in spirit: entities then cascade). Holds **12 serialized system refs** (incl. `actionUI`, explicitly commented DORMANT). Only **2** `FindObjectOfType` fallbacks remain (`actionBarUI`, `regionOutlineRenderer`); the rest use `.Instance`. Delegates run-end meta to `RunEndCoordinator.ProcessRunEnd`. |
| **GameBootstrap** | `_GAME/Scripts/Core/GameBootstrap.cs` | **88** | SCAFFOLD → validator · `[DefaultExecutionOrder(-1000)]` | `Habitales.Core`. Validation runs in **`Start()`** (not `Awake` — at `-1000` its own Awake runs before sibling `Instance`s are set). Validates all **8** core singletons present + optionally `EntityRegistry` (warns if empty). `static bool BootSucceeded`. Full ordered init is still Phase 3. |
| **GameLog** | `_GAME/Scripts/Core/GameLog.cs` | **23** | NEW · SCAFFOLD | `Habitales.Core`. Static category toggles: `Core/Action/Cascade/Dialogue/RegionGen/Entity`. Defaults: Core/Action/Dialogue/RegionGen = on; Cascade/Entity = off (muted). Config-SO binding is later. |

**Cascade detail (`CascadeTileUpdates`, RunManager):** global (`GetAllTiles()`), iterates `cascadeIterations` (serialized, default **3**), lerp factor `diffusionRate` (serialized `[Range(0.05,0.5)]`, default **0.15**), 4-neighbour (`GetAdjacentTiles`), snapshot-then-apply. Syncs the `Contaminated` overlay on `tile.tv` at the **>60** contamination threshold. (Iteration/lerp are serialized defaults, changeable in Inspector.) `EvaluateThresholds` writes `tile.lastTier`/`tile.tierSeeded` (Tier crossings → `OnTileTierChanged`).

**RunManager public surface (Law-1):** `IsEventPaused {get;private set;}`, `ZoneUnlockThreshold`, `PeakThrivingCount`, `PeakScreenshot`, `TileManager`, `DiffusionRate`, `CascadeIterations` (all read-only). Events: `OnDayResolved(int)`, `OnTileTierChanged`, `OnRegionUnlockReady`, `OnRegionUnlocked`. Methods: `PauseForEvent()`/`ResumeFromEvent()`, `UnlockNextRegion()`, `TriggerGameOver`. **There is no `CurrentDay`/`CurrentYear`/`WorldHealth`/`IsSimulationPaused` on RunManager** — clock lives on ResourceManager; avg health on RegionManager.

**RunManager holes / corrections vs sweep #1:**
- ⚠️ **`IsEventPaused` is NEVER read inside RunManager** (both cross-check agents confirm). It is *set* by `PauseForEvent`/`ResumeFromEvent` and *read* by `ActionManager.ExecuteAction` (entry gate, §G). The old claim "guards `HandleActionCompleted` completion only" is **wrong** — `HandleActionCompleted` is an intentionally empty no-op; the real gate is the entry-gate in ActionManager. **The arch §3.4 interrupt-before-action gap is now CLOSED.**
- `AbortCurrentAction()` is still **stubbed** (logs + `// TODO: apply partial tile progress here`).
- Game-over has **three** triggers: collapse (`criticalPercent >= collapseThreshold` 90%), prototype day-cap (`TotalDays >= prototypeRunDays`, **default 60**, "Field Season Complete"), and `resourceManager.OnGameOver` (fires at `TotalDays >= 100`, §D) → `HandleGameOver`. **The 60-vs-100 mismatch is real and unresolved** — RunManager caps the prototype run at 60; ResourceManager's own 100-day cap would only ever fire in a non-prototype/100-day run.
- `EventManager.DrainQueue` (arch heartbeat step 5) is **not** strictly ordered inside the heartbeat — it runs via EventManager's own `OnTimeAdvanced` subscription (noted in a code comment; strict ordering "awaits the control-inversion").

**Singleton model — RESOLVED (consistent).** Eight core singletons with pinned init order: **−200** ResourceManager / WeatherManager / TileManager → **−150** RegionManager → **−100** ActionManager / EventManager / DialogueManager / RunManager → everything else 0. Remaining `FindObjectOfType` calls target only non-singletons (TileSelector, ActionUI, ActionBarUI, RegionOutlineRenderer, DayNightCycleHandler — the last still grabbed by `ActionManager` at runtime).

---

## B. Tiles

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **Tile / TileStats** | `_GAME/Tile/_API/Tile.cs` | **60** | ACTIVE · CANON · POCO | 8 stats; `soilComposite` = mean of first 6; `CalculateHealth()` = `(soilComposite + vegetationCover + (100-contamination))/3`, capped 33 @ contam>60. Carries runtime `lastTier` + `tierSeeded` (arch §2.1b). Holds `entity` (TileEntity), `issues`, `tv` (overlay list), `regionID`. `isAnalyzed`/`issuesRevealed` default-true. |
| **TileManager** | `_GAME/Tile/_API/TileManager.cs` | **614** | ACTIVE · CANON · singleton · `[-200]` | **Triple** dict: `tileCache` (Vector2Int→Tile), `tileGameObjects` (Tile→GameObject), `activeVFX` (Tile→VisualEffect). Entity API is now **fully SO-driven**: `SpawnById(tile,id)`, `SpawnFromDef(tile,def)`, `ReplaceWithSO(tile,def)`, `RemoveEntity(tile)`. `SpawnEntity<T>`/`TransformEntity<T>`/`UpdateEntitiesInRegion` are **REMOVED**. `UpdateAllEntities(in TickContext)` iterates a snapshot (`GetAllTiles()` returns a fresh list). `RefreshAllVisuals()` bulk-syncs once/day. Visual name uses `entity.entityId`; VFX uses `def.vfxKey`. Serialized `entityRegistry` + boot `ValidateAll()` with Law-3 loud-fail. Calls `VFXManager.Instance`. No `FindObjectOfType`. `ModifyTileStats` distributes `soilDelta ÷6`. |
| **TileSelector** | `_GAME/Tile/_API/TileSelector.cs` | **728** | ACTIVE · non-singleton | Global ns. Single-select + Adjacent/NonAdjacent click multi-select + drag-paint FloodFill (multi-source 4-dir BFS, Fisher-Yates shuffled, occupied tiles = walls, sliding 8-adjacent seed window capped at `ceil(max/seedDivisor)`, max from `ResourceManager.AvailablePeople / action.MinPeoplePerTile`). Per-frame `Update`: hover raycast + click + paint + ESC. Gated by `ActionManager.IsActionRunning` + `EventManager.IsShowingEvent`. Fires `OnTileSelected/OnTileDeselected/OnMultiSelectionConfirmed/OnMultiSelectExited` (subscribed by ActionBarUI/ActionUI). Reads `ResourceManager`, plays `AudioManager`/`FMODEvents`, calls `OverflowTipSpawner.Instance`. Resolves managers via `.Instance` (no internal `FindObjectOfType`). Large — split if renovated. |
| **TileVisualizer** | `_GAME/Tile/_API/TileVisualizer.cs` | **162** | ACTIVE | Per-tile material tint + substat colored bars; `SetVisualState(TileVisualState)`; contamination tint @ >60; instantiates/destroys Firebreak overlay GameObject from `tv`. ⚠️ `GetHealthColor()` is **commented out** (health-color mapping dormant; returns white). |
| **EntityVisualizer** | `_GAME/Tile/_API/EntityVisualizer.cs` | **93** | ACTIVE | **Def-only now**: sprite from `def.tileSprite`; legacy per-type Sprite fields + string-switch fallback **removed**. Billboarding in `LateUpdate` (cosmetic, RunManager untouched, arch S3). Warns on missing def/sprite. |
| **Region rendering** | `RegionOutlineRenderer.cs` (**146**), `RegionBoundaryMeshBuilder.cs` (**93**, static util), `RegionHealthUI.cs` (**73**) | — | ACTIVE | Outline mesh + per-region health bar. `RegionOutlineRenderer` reads `RegionManager.Instance.GetRegionHealth`, drives `RegionHealthUI`, has a `[FormerlySerializedAs("zoneManager")]` field (confirms the Zone→Region rename). |
| **Tile support types** | `TileIssue.cs` (15), `IssueType.cs` (10, 7 values), `TileIssueLibrary.cs` (76, static, 7 issues w/ per-stat multipliers), `TileOverlayType.cs` (6 → `Firebreak/Contaminated/IssueMarker`), `TileVisualState.cs` (10 → 7 states) | — | SUPPORT | Issue/overlay enums + library. |

---

## C. Entities ✅ (cutover COMPLETE — generic SO model is the only model)

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **TileEntity (base)** | `_GAME/Tile/_API/TileEntity.cs` | **17** | ACTIVE · THIN base | `Habitales.Entities`. Now a thin abstract base: `health`, `entityId`, `def` (TileEntitySO), `abstract OnDailyUpdate(Tile, in TickContext)`. **No legacy `entityType`, no per-kind subclasses** — the 10 hand-coded kinds are deleted. Only `GenericTileEntity` extends it. |
| **PlantedCubeEntity** | `_GAME/Prototype/Scripts/PlantedCubeEntity.cs` | — | **REMOVED** | Disposable cube entity. |

> ✅ **The hand-coded tree pipeline port is DONE.** All entities are now data-driven `TileEntitySO` assets resolved through `EntityRegistry`; bespoke ones (Fire, Village) use behaviour hooks. The §P entity sub-tree below is **load-bearing**, not scaffold: every spawn site (actions + RegionManager) routes through `TileManager.SpawnById`.

> 🗂️ **Targeted update 2026-06-24 — TileEntitySO Artifice pass + Promote-gate / daily-effects cleanup.** `TileEntitySO` is now authored through **Artifice Toolkit** (`[BoxGroup]` sections, `[PreviewSprite]` on `tileSprite`, `[EnableIf]` conditionals — requires Artifice's global drawer toggle ON in the editor). Three model changes in `StatTypes.cs`/`TileEntitySO.cs`/`GenericTileEntity.cs`: **(1)** new `StatGate` struct (stat/comparator/threshold only) now types `promoteWhen` — replaces the shared `StatCondition` so the promote gate no longer exposes a redundant `outcome`/`transformTarget` that duplicated `nextStage`; serialized data migrates by field-name match. `promoteWhen` is `[EnableIf]`-hidden unless `requirePromoteCondition` is ticked. **(2)** new `DailyStatDeltas` struct = 8 `[Range(-10,10)]` sliders (the 8 writable substats, no SoilComposite); field `plantDailyDeltas` shows for **Plant** category only, while the legacy `dailyEffects` list now shows for non-Plant only. Runtime applies both (the inactive one is zero/empty). **(3)** All 10 Plant `_SO/Entities/*` assets migrated: `dailyEffects` arrays → `plantDailyDeltas` blocks, arrays emptied. Non-plant entities keep `dailyEffects`. **OnValidate still MISSING (§N).**

---

## D. Resource / Time

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **ResourceManager** | `_GAME/Results/Resources/ResourceManager.cs` | **244** | ACTIVE · Law-1 exemplar · singleton · `[-200]` | Clock has **three** advance methods: `AdvanceTime(int)` (batch debug), `AdvanceOneDay()` (single-day primitive → fires `OnTimeAdvanced(1)`), `AdvanceTimeStepped(coroutine)` (per-day: `AdvanceOneDay` then `WaitUntil(DayNightCycleHandler.IsIdle)`). Worker roster via `WorkerFactory.GenerateBatch`. **Fatigue reads `WeatherManager.Instance.FatigueK` on demand** (Law-1; no mirrored field/subscription). Law-1 getters (all computed): `CurrentYear`, `TotalDays`, `TotalPeople`, `AvailablePeople`, `RecoveringPeopleCount`, `AllWorkers`, `AvailableWorkers`, `FatiguedWorkers`, `ResearchPoints`. ⚠️ **`OnGameOver` fires at `totalDays >= 100`** in all three advance methods — there is **no 60-day concept in this file**; the 60-day cap is RunManager's `prototypeRunDays`. `maxYears` serialized field is **dead** (never used). |
| **Worker / Factory** | `Worker.cs` (50, `[Serializable]` POCO, 10-value `WorkerTrait` enum **display-only**), `WorkerFactory.cs` (70, static, 30 Filipino first + 20 last names, `using Habitales`), `WorkerPortraitPool.cs` (33), `ResourceDisplay.cs` (74) | — | ACTIVE | Roster + portraits. `ResourceDisplay` is clean (subscribes/unsubscribes `OnTimeAdvanced`/fatigue/recovery; no dangling refs after recent edits). |
| **DayNightCycleHandler** | `_UTILITIES/Graphics/Shaders/Scripts/DayNightCycleHandler.cs` | **88** | ACTIVE · critical | `static bool IsIdle {get;private set;}` is the exact gate `AdvanceTimeStepped`'s `WaitUntil` polls — set false at `StartCycle`, true after `RunCycles`. Subscribes `ResourceManager.OnTimeAdvanced`. Lives outside `_GAME/`. |

---

## E. Weather

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **WeatherManager** | `_GAME/Weather/WeatherManager.cs` | **152** | ACTIVE · singleton · `[-200]` | Data-driven. `WeatherState` enum (Sunny/Cloudy/Rainy/Stormy) + serialized `WeatherProfile` table (per-state `workSpeedMultiplier/fatigueK/fireSpreadMultiplier/fireBonusDamage`). Law-1 **property** getters: `WorkSpeedMultiplier`, `FatigueK`, `FireSpreadMultiplier`, `FireBonusDamage` — plus legacy `GetX()` wrappers (used by RunManager's TickContext build). Coded `DefaultProfile()` seeds unauthored states + Law-3 warn. Dry/Wet weighted rolls (days 1–182 / 183–365). Fires `OnWeatherChanged`. Prototype locks to Sunny. |
| **WeatherVFXController** | `_GAME/Weather/WeatherVFXController.cs` | **68** | NEW · SCAFFOLD | Cosmetic half (arch S3). Subscribes `OnWeatherChanged`; per-state `WeatherVFXBinding` toggles VFX GameObjects + fires a `UnityEvent` for bespoke FX. Needs Inspector wiring; guards against missing WeatherManager. |

---

## F. Regions (renamed from Zones)

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **RegionManager** | `_GAME/Zone/RegionManager.cs` | **898** | ACTIVE · singleton · `[-150]` | Global ns, folder still `Zone/`. Pipeline (`GenerateNewRegion`): `FindSeedTile` → `FloodFillShape` (calls `FillEnclosedHoles` internally) → `SpawnRegionTiles` (profile-scaled stats) → `RollTheme` → `AssignIssues` → `PlaceBuildings` → `PlaceOrganicEntities` → `ApplyForcedEntities` → `AnimateTiles` → `IncreaseTotalPeople(workerReward)` → `OnRegionGenerated`. **ALL entity spawns now route through `tileManager.SpawnById(tile, id)`** — buildings (`"village"`/`"factory"`), organic (`"tree_mature"`/`"tree_sapling"`/`"tree_seedling"`/`"deadtree"`/`"stump"`/`"trash_bio"`), and forced (a name→id switch each branch calling `SpawnById`). `GetTotalAverageHealth()` + `GetRegionHealth(int)` are read-only. No `FindObjectOfType`; no direct `tile.stats` writes (stats built into a `TileStats` passed to `SpawnTile`). **Largest single file.** ⚠️ Dead code: `WeightedPick` + `AddNeighborsToCandidates` (both uncalled, superseded by `AddCandidates`). |
| **Region data** | `RegionProfile.cs` (94), `RegionTheme.cs` (10, enum), `RegionGenerationResult.cs` (28, POCO), `ForcedEntityPlacement.cs` (12, struct) | — | ACTIVE | Renamed from `Zone*`, still global-ns. `RegionProfile` spawn-chance fields: `matureTreeSpawnChance`, `saplingSpawnChance`, `seedlingSpawnChance`, `deadTreeSpawnChance`, `stumpSpawnChance`, `bioTrashSpawnChance`; plus per-stat ranges, theme weights, `maxVillages/maxFactories` + spawn chances, `workerReward`, `flowFalloff`/`enclosureBonus`, `forceSpecificEntities` + `forcedEntities`. ⚠️ **`ForcedEntityPlacement.entityType` is still a legacy display-name `string`** (e.g. "Mature Tree") resolved by RegionManager's switch — the arch §3.6 *struct* migration to a `TileEntitySO`/`entityId` ref is **NOT done**, though the spawn *mechanism* (`SpawnById`) is. `OnRegionUnlocked`/`UnlockedRegions`/`ZoneCount` getters remain **absent** (unlock flow lives on RunManager). |

---

## G. Actions

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **ActionManager** | `_GAME/Action/_API/ActionManager.cs` | **184** | ACTIVE · singleton · `[-100]` | Coroutine + tile-distribution-across-days. Resolves `TileManager` via `.Instance` in `Start`. ✅ **Entry-gate present:** `ExecuteAction` checks `RunManager.Instance.IsEventPaused` (lines 83–87) and blocks action start while an event popup is up — this **closes arch §3.4**. `IsActionRunning` property; `OnActionCompleted` event; `actionUsageCounts` dict. ⚠️ Still grabs `FindObjectOfType<DayNightCycleHandler>()` (line 116) — a non-singleton; remaining cleanup target. |
| **PlayerAction (base)** | `_GAME/Action/_API/PlayerAction.cs` | **75** | ACTIVE | Abstract base. Efficiency `days = ceil(BaseDays / sqrt(ppl/MinPpl))` clamp `MinDays`; `CanExecute` preflight; `FatigueMultiplierPerTile` (default 2.0). Helpers `AnyTileHasEntity<T>` (legacy) **and** `AnyTileHasEntityId(tiles, id)` (new data-driven check). |
| **ActionBarUI** | `_GAME/Action/_API/ActionBarUI.cs` | **337** | ACTIVE · **primary action flow** | The **NEW Phase-4 action-first docked bar** (category → action → click tile → confirm). Resolves `ActionManager.Instance` + `FindObjectOfType<TileSelector>`; subscribes `OnTileSelected`/`OnMultiSelectionConfirmed`. **No pause-gate of its own** (the gate is in ActionManager). ⚠️ Phase-5 stub `OnPlantSpawnedTween()` is an empty orphan referencing the removed cube `cubeTransform`; line-205 TODO for variant-group dedup. |
| **ActionUI** | `_GAME/Action/_API/ActionUI.cs` | **907** | DORMANT (legacy) | The **OLD tile-first docked-card flow**, superseded by `ActionBarUI` and unwired as the main driver (RunManager's `actionUI` ref is marked DORMANT). Still in scene/compiles. Uses `ActionIconConfig` (not `ActionSO`); `FindObjectOfType<TileSelector>` fallback; commits via `ActionManager.ExecuteAction` with **no pause-gate**. Dead code: `GetCounterGradientColor` (uncalled), retired flood-fill slider, `// TODO: assign lock sprite`. Biggest UI file — if the team commits to ActionBarUI, this is a deletion candidate. |
| **Action UI support** | `DPadLayoutGroup.cs` (338, ns `UnityEngine.UI`, custom LayoutGroup), `CategoryButton.cs` (53), `ActionIconConfig.cs` (88, SO), `ActionPanelState.cs` (12, enum), `ActionCategory.cs` (52, enum+ext), `TrashVisualConfig.cs` (8, SO) | — | ACTIVE | `ActionIconConfig` (`GetSpriteForAction(name)`) is the live icon source until Phase 5's `ActionSO`. `TrashVisualConfig` holds bio/nonBio sprite-variant lists; referenced by `InspectTrashAction` (see bug below). |
| **Live actions (5 registered)** | `PlantTreesAction.cs` (30), `ApplyFertilizerAction.cs` (36), `ClearTrashAction.cs` (28), `StumpDeadTreeRemovalAction.cs` (29), `InspectTrashAction.cs` (48, ns `Habitales.Entities`) | — | ACTIVE | Registered in `ActionManager.RegisterActions`. ClearTrash/Stump use `AnyTileHasEntityId` + `RemoveEntity`; PlantTrees uses `SpawnById("tree_seedling")` + factory `entityId` check. ⚠️ **`ApplyFertilizerAction` writes `tile.stats.*` DIRECTLY — a LIVE Law-1 violation** (should route through `ModifyTileStats`). ⚠️ `InspectTrashAction.visualConfig` is a `[SerializeField]` on a `new`-constructed plain class → **always null at runtime** (sprite-swap silently disabled; `isInspected` now lives in `GenericTileEntity.hookState`). |
| **Dormant actions (4)** | `FireSuppressionAction.cs` (34), `CreateFirebreakAction.cs` (40), `AnalyzeSoilSampleAction.cs` (44), `EcologicalSurveyAction.cs` (26) | — | DORMANT | Commented out of `RegisterActions`. **FireSuppression dormanted 2026-06-24** when Emergency was retired to 3 authorable groups (its category had no other home). ⚠️ `CreateFirebreakAction` writes `tile.tv` + `tile.stats.vegetationCover` **directly** (Law-1 fix needed before reactivation). `AnalyzeSoilSample`/`EcologicalSurvey` write `tile.isAnalyzed`/`issuesRevealed` directly. |
| **Removed actions** | `CoverCroppingAction.cs`, `Prototype/PlantingAction.cs`, `Prototype/RemoveWitheredAction.cs` | — | **REMOVED** | Cube-slice + CoverCrop (superseded by the CoverCrop behaviour-hook plan). |

> 🗂️ **Targeted update 2026-06-24 — Action Creator (data-driven actions).** Actions are now authorable as data, mirroring the TileEntitySO/GenericTileEntity model. New pieces (all `_GAME/Action/`):
> - **`ActionSO.cs`** — NEW · author-facing data half. Now Artifice-attributed (`BoxGroup`/`PreviewSprite`) like TileEntitySO — **no custom editor**; the inspector *is* the "Action Creator." Holds identity, `icon`, 3-value `ActionGroup` (Examine/Intervene/Cleanup), `selectionMode`, the `effects` list, the cost fields, and an (unwired) **Boogle** group (lore → info tooltip → images, for a future action info popup). Replaces the old `category`/`baseDays`-only stub. `variantGroupName` **removed** (unused).
> - **`ActionEffect.cs`** (`ActionEffect` + `ActionEffectType` enum) — SUPPORT · one authorable per-tile effect. `PlaceEntity` (spawns a `TileEntitySO` by id, only if tile empty) or `CustomBehavior` (delegates to an `ActionEffectHook`). `EnableIf` shows only the chosen kind's field. ⚠️ **Must be a `[Serializable] struct`, not a class** — Artifice's reorderable list view throws `ArgumentOutOfRange` in `SwapChildren` when dragging class-typed elements; all working lists in the project (StatChange/StatCondition) are structs.
> - **`ActionEffectHook.cs`** — NEW · abstract SO escape hatch, the action-side twin of `EntityBehaviourHook`. `Apply(Tile, TileManager)` per tile.
> - **`GenericPlayerAction.cs`** — NEW · runtime half. Wraps an `ActionSO`, maps `ActionGroup`→`ActionCategory`, runs `effects` in `ExecuteOnTile`. Cost math inherited unchanged from `PlayerAction`.
> - **`ActionRegistry.cs`** — NEW · SO listing every authored `ActionSO` (twin of `EntityRegistry`); `ActionManager` reads it (new `actionRegistry` serialized field) and registers each as a `GenericPlayerAction` via `RegisterAuthoredActions()`. Drag the registry asset onto ActionManager in the scene.
> - **`SelectionMode`** gained a `Single` value (one tile; routed through multi-select with the cap forced to 1 in `TileSelector.EnterMultiSelectMode`). **`PlayerAction`** gained `virtual Sprite Icon` (GenericPlayerAction returns `ActionSO.icon`; `ActionUI` prefers it over `ActionIconConfig`).
> - **Emergency tab hidden** in both `ActionUI` and `ActionBarUI` (`emergencyButton`/`emergencyTab` set inactive). The `ActionCategory.Emergency` enum value is **kept** so the dormant fire actions still compile.
> - **`ActionBarUI` now honors `selectionMode`** (new `EnterSelectionFor(Tile)` helper; both arm + tile-click route FloodFill→flood, else→multi-select). Disarm guard widened from `IsFloodFillMode` to `IsMultiSelectMode`. ⚠️ **Behavior change:** existing actions that declared non-FloodFill modes (e.g. `ClearTrash`=NonAdjacent, `StumpDeadTreeRemoval`) now use click-multi-select in the active bar instead of always flood-filling.

---

## H. Events

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **EventManager** | `_GAME/Events/_API/EventManager.cs` | **259** | ACTIVE · singleton · `[-100]` | Subscribes `ResourceManager.OnTimeAdvanced` + `RegionManager.OnRegionGenerated`; evaluates triggers (OnDay/OnHealthThreshold/Random/OnZoneUnlock/Manual). **`FireEventByID(id)`** is the manual entry (Village kaingin fires `"first_kaingin"`). **Calls `RunManager.PauseForEvent`/`ResumeFromEvent`** to pause the sim during a popup → this is what drives the ActionManager entry-gate (§G). `IsShowingEvent` gates TileSelector input. `RefreshGlobalContext` writes tokens to `EventContext` each tick. `GameEventSO.linkedThread` is now a **`ConversationSO`** (retyped by the 2026-06-23 dialogue rework) → conditional `DialogueManager.AppendConversation(ev.linkedThread)`. |
| **Event support** | `EventContext.cs` (123, static token bus: `Set/SetOverride/Get/Resolve` + focus target), `EventCameraHandler.cs` (177, ns `UTILITIES.Camera`, singleton, coroutine `PanTo`/`ZoomTo`/`ReturnToOrigin`, used by ActionUI + EventManager), `EventPopupUI.cs` (96, singleton, `Show(GameEventSO,…)`/`Hide`), `GameEventSO.cs` (38, SO), `GameEventRegistry.cs` (8, SO list) | — | ACTIVE | Arch §6 turns these into the meaning-event seam layer (STUB; see `IEntityEventSink` §P). |

---

## I. Dialogue / Chat

> **Targeted update 2026-06-23 — ConversationSO rework (Phase 1).** The old linear `DialogueThreadSO`/`WorkerMessageTemplateSO`/`DialogueTabSO` model was fully replaced (no migration — no dialogue had been authored). Branching node model now authored via `ConversationSO`. **Phase 1 (data model + custom editor + runtime rework) landed; Phase 2 (interactive choice walker + choice UI in ChatAppUI) NOT built — `ChoicePayload` is skipped at resolve time.** Not yet compiled in Unity at time of writing.

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **DialogueManager** | `_GAME/Dialogue/_API/DialogueManager.cs` | **729** | ACTIVE · singleton · `[-100]` · REWORKED | `Habitales.Dialogue`. Worker daily roll still **40%/day** (`MESSAGE_ROLL_CHANCE = 0.40f`) but per-worker cooldown is now **`COOLDOWN_DAYS = 2`** (was 25); **`s_flavorLines` DELETED** — the roll now picks a weighted worker then a **non-repeated** `ConversationSO` from its `trait ∪ universal` pool (`registry.GetWorkerDailyPool`), tracked via `WorkerTabData.lastSentTemplateIDs` (keyed by asset name). `AppendConversation(SO|name)` routes by `conversation.channel` to the three fixed tabs (`"group"`/`"azi"`/`"bob"`); `AppendWorkerConversation(SO,Worker)` → per-worker DM. `ResolveConversationLines(SO,…)` walks the thread **linearly** (Content/Sticker emit; **`ChoicePayload` skipped — Phase 2**). `Worker`-sender bound to one consistent name/portrait at append time (temp weighted-random worker if a Worker-sender appears in GroupChat). Azi Tier 1/2 inline (`AppendAziMessage`) preserved. `RunBirthdayCheck` fires `AppendConversation("birthday_group_shoutout")` (live code, dormant on data). Sticker runtime (`HandleStickerSent` + trait responses) preserved. Writes `EventContext.SetOverride`; subscribes `ResourceManager.OnTimeAdvanced` + `RegionManager.OnRegionGenerated`. |
| **Conversation model** | `DialogueEnums.cs` (29), `MessagePayload.cs` (41), `MessageNode.cs` (19), `ConversationSO.cs` (26, SO), `CharacterProfileSO.cs` (37, SO) | — | **NEW · live** | `Habitales.Dialogue`. The authored data model. Hardcoded enums `DialogueChannel{GroupChat,Azi,Bob,Worker}`, `DialogueSpeaker{Player,Azi,Bob,Worker}`, `DialogueTrigger{DailyRoll}`. `[SerializeReference]` payload hierarchy `MessagePayload` → `ContentPayload`/`StickerPayload`/`ChoicePayload` (variable-count `ChoiceOption`s, each w/ own label + child thread that merges back). `ConversationSO` = `{trigger, channel, personality+universal, List<MessageNode> thread}`. `CharacterProfileSO` homes Azi/Bob `displayName`+`portrait`+expressions (replaces the deleted per-thread `cast`; also re-homes `ExpressionEntry`). |
| **ConversationEditor** | `_GAME/Scripts/Editor/ConversationEditor.cs` | **512** | **NEW · editor-only** | `Habitales.Dialogue.EditorTools`. Custom `[CustomEditor(typeof(ConversationSO))]`. Operates on the target directly (avoids `SerializedProperty` for the SerializeReference tree). Trigger/channel dropdowns + channel-gated personality dropdown (10 traits + "Universal"); recursive thread with **channel-constrained** sender dropdowns, conditional payload-type fields (Choice = Player only), nested variable-count choices, Azi/Bob expression dropdowns sourced from `CharacterProfileSO` via `AssetDatabase`. Structural list edits are **deferred** past the render loop (avoids IMGUI layout-mismatch errors). |
| **Chat App UI** | `Dialogue/App/ChatAppUI.cs` (252), `ChatBubbleUI.cs` (54), `TabRowUI.cs` (53), `StickerButtonUI.cs` (30) | — | ACTIVE | **Namespace `Habitales.UI`** (not global). `ChatAppUI` subscribes `DialogueManager.OnMessagesUpdated`/`OnUnreadChanged`; tab filter shows only tabs with ≥1 message body. **Unchanged by the rework** — still consumes `GetChatLines`/`GetTabPreviews`. The Phase-2 choice-button UI lands here. |
| **Dialogue support** | `DialogueRuntimeTypes.cs` (109), `DialogueRegistry.cs` (130, SO) | — | SUPPORT / LIVE · REWORKED | `Habitales.Dialogue`. `RuntimeChatEntry` now holds a direct `ConversationSO conversation` ref (was `threadID` string); `FromThread`→`FromConversation`. `DialogueRegistry` now holds `List<ConversationSO> conversations` + **fixed-tab config** (`groupChatName`/`groupChatPortrait`, `aziProfile`/`bobProfile`, `fallbackWorkerPortrait`) + `GetConversation(name)` / `GetWorkerDailyPool(trait)`. The old `DialogueTabSO` is absorbed into this config. |
| **Stickers** | `StickerSO.cs` (16), `StickerLibrary.cs` (55) | — | **ACTIVE (was DORMANT)** | Now live on **two** paths: the runtime sticker tray + trait/birthday responses (`HandleStickerSent`) AND authored `StickerPayload` nodes (picked by SO in the editor). |
| **Removed by rework** | `DialogueThreadSO.cs`, `WorkerMessageTemplateSO.cs`, `DialogueTabSO.cs`, `DialogueTypes.cs` (+ `.meta`) | — | **REMOVED** | Replaced by the `ConversationSO` model. `SenderResolution`/`SpeakerProfile`/`DialogueLine` gone; `ExpressionEntry` relocated to `CharacterProfileSO`. Listed so stale refs don't resurrect them. |

---

## J. Results / Progression / Level-Up

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **EndGame** | `EndGameScreenUI.cs` (243, singleton), `EndGameData.cs` (44, POCO), `ZonePillUI.cs` (31) | — | ACTIVE | Run-end snapshot screen. `RunManager.BuildEndGameData` assembles `EndGameData` (zone pills, Employee of the Year, favourite/avoided action, most-chatted worker) + XP fields from `RunEndCoordinator.RunEndSummary`. EndGameScreenUI's level-up/`PlayerProgressionSO` fields are DORMANT (handoff moved to MainMenu). |
| **RunSnapshot** | `_GAME/Prototype/Scripts/RunSnapshot.cs` | **37** | NEW · ACTIVE · POCO | Peak-thriving count + `Texture2D` screenshot at peak (`TryRecordPeak`, `CaptureRoutine`). Owned by RunManager. TODO: screenshot captures HUD too (prod should use a separate world-cam RenderTexture). |
| **Level-Up** | `LevelUpScreenUI.cs` (210), `PlayerProgressionSO.cs` (72, SO schema), `ProgressionPersistence.cs` (94, static), `PendingLevelUp.cs` (5) | — | NEW · ACTIVE | XP bar + unlock card + fade-to-menu. **Cube refs fully stripped from active paths**; cube-era fields (`unlockedPlantSeeds/Names`, `unlockPool`) retained **inert** in the SO + JSON for save forward-compat only. `PendingLevelUp` is a 5-line dead placeholder kept for `.meta` GUID stability (level-up now driven by `lastSeen*` snapshot diff on `MainMenu.Start`). |
| **RunEndCoordinator** | `_GAME/Scripts/Meta/RunEndCoordinator.cs` | **80** | NEW · ACTIVE · singleton | `Habitales.Meta`. **Single progression load point** (`Start` → `ProgressionPersistence.Load`, was dual-loaded). `ProcessRunEnd(thriving,degraded,critical,peak)` → XP weights (critical **0.1** / degraded **0.6** / thriving **2.4** per tile) → `progression.AddXp` → `Save` → returns `RunEndSummary` (xp + hardcoded `GenerateAziLine`). Called by `RunManager.TriggerGameOver`. F10 `ResetProgression`. XP unlocks nothing functional in Alpha. |
| **Azi UI** | `AziSpeechBubbleUI.cs` (78) | — | NEW · ACTIVE · singleton | Run-end Azi bubble shown before EndGameScreen; self-heals disabled root; callback-driven. |
| **MainMenu** | `_GAME/UI/MainMenu.cs` (86) | — | ACTIVE | Menu hub: level/XP display, level-up overlay via `lastSeen*` diff, scene loads, F-key reset. Uses `ProgressionPersistence` static + `PlayerProgressionSO`. |
| **BooglePanelUI** | `_GAME/Prototype/UI/BooglePanelUI.cs` | — | **REVIVED · ACTIVE · singleton** (2026-06-24) | "?" lookup overlay, **re-created ActionSO-driven**. `Show(PlayerAction)` renders `ActionSO` Boogle data (`displayName` + `lore` + `infoTooltip` + first `supplementaryImages`). Script GUID re-pinned to `f9f9fd92…` so the existing `BooglePanel.prefab` / scene wiring resolves (was a dangling missing-script after the cube-slice removal). Opened by the `?` `BoogleButton` on each action card, now wired in `ActionUI.CreateActionCard`. |

---

## K. Cube planting slice — REMOVED

The disposable cube-planting prototype is fully deleted. Recorded so stale refs don't resurrect it:

| File | Now |
|---|---|
| `Prototype/Scripts/PlantingProfileSO.cs` · `PlantingProfileGenerator.cs` · `GeneratedPlantRegistry.cs` · `HWBColor.cs` · `PlantedCubeEntity.cs` · `PlantingAction.cs` · `RemoveWitheredAction.cs` | **REMOVED** |
| `Prototype/Scripts/BooglePanelUI.cs` | **REVIVED** (2026-06-24) — re-created ActionSO-driven at `Prototype/UI/BooglePanelUI.cs`; see §J. |
| `Action/PlayerActions/CoverCroppingAction.cs` | **REMOVED** (superseded by the planned CoverCrop behaviour hook) |

`EntityRegistry` (§P) is the **sole** registry going forward. Orphan cube remnant: `ActionBarUI.OnPlantSpawnedTween()` empty stub (§G) — safe to delete.

---

## L. UI / Misc / Utilities

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **Inspect / Examine** | `InspectPanelUI.cs` (252), `ExamineResultPopupUI.cs` (204, singleton), `InspectModeManager.cs` (92, singleton) | — | **LIVE (corrected)** | ⚠️ **Not dormant.** `InspectPanelUI` is always-on (unconditional health display; issues/substats gated by default-true `isAnalyzed`/`issuesRevealed`, so they show). `ExamineResultPopupUI` is **called from `ActionManager.FinishAction`** and uses `entityId` ("trash_bio"/"trash_nonbio") checks (migration confirmed). `InspectModeManager` is partly live — `ActionUI` calls `Instance.ExitInspectMode()`. |
| **HUD / Menu** | `HealthBarUI.cs` (97), `UnlockNextZoneButtonUI.cs` (72), `ObjectiveBannerUI.cs` (54), `OverflowTip.cs` (48), `OverflowTipSpawner.cs` (44, singleton), `HoverToolttip.cs` (41, typo'd filename) | — | ACTIVE | ⚠️ `HealthBarUI` **polls** `RegionManager.GetTotalAverageHealth()` every frame in `Update` (arch wants event-driven). `UnlockNextZoneButtonUI` subscribes `RunManager.OnRegionUnlockReady`/`OnRegionUnlocked`, calls `UnlockNextRegion()`. `ObjectiveBannerUI` subscribes the same two events. `OverflowTipSpawner.Instance` used by TileSelector. |
| **EntityArtTool** | `_GAME/Utilities/EntityArtTool.cs` | **170** | DORMANT (corrected) | ⚠️ Runtime MonoBehaviour (no `#if UNITY_EDITOR`), driven only by Inspector ContextMenus — **no codebase callers**, `Start` auto-gen commented out. Does **not** reference `TileEntitySO`/`entityId` (raw sprite placement). As-is it is *not* a usable seed for arch §7 without rework. |
| **VFXManager** | `_UTILITIES/Graphics/VFX/Scripts/VFXManager.cs` | **122** | **ACTIVE (corrected)** · singleton | ⚠️ Not a shell. `SpawnVFX(key,pos,…)` (3 overloads) + `DestroyVFX`. **Live callers:** `TileManager` (2×SpawnVFX + 2×DestroyVFX), `ActionManager` (`SpawnVFX("Default",pos)` on execute). |
| **ClickVFX** | `_UTILITIES/Graphics/VFX/Scripts/ClickVFX.cs` (35) | — | DORMANT | Raycast click-VFX listener; no callers; direct `Instantiate` (not routed through VFXManager). |
| **Camera** | `_UTILITIES/Camera/CameraDrag.cs` (68), `CameraZoomOrtho.cs` (53) | — | ACTIVE | ns `_UTILITIES.Camera`. RMB pan / scroll zoom (cosmetic per-frame; arch S3). `CameraDrag` respects `EventCameraHandler.Instance` pan state. |
| **Audio / util** | `AudioManager.cs` (26, singleton `instance`), `FMODEvents.cs` (26, singleton `instance`), `ColorUtils.cs` (18, static `LerpHSV`), `UIButtonAutoHook.cs` (32), `UIAlphaHitbox.cs` (13) | — | SUPPORT | `AudioManager.instance`/`FMODEvents.instance` (lowercase) used by TileSelector for click SFX. ⚠️ `UIButtonAutoHook` (class is `UIButton`) has a duplicate-`Start()` quirk + dead `mainCamera` field. |

---

## M. Out-of-scope / third-party (do not touch)

- `Assets/1_Archive/` — pre-existing old code.
- `Assets/Plugins/FMOD/**` — third-party audio middleware.
- `Assets/_UTILITIES/LeanTween/**` — third-party tweening (4115-LOC `LeanTween.cs` etc.); **excluded from this sweep**.

---

## N. Known stubs / hard TODOs (refreshed)

| Location | Status |
|---|---|
| `RunManager.AbortCurrentAction()` | Stubbed — partial tile progress on interrupt not applied. |
| Event-pause **entry-gate** | ✅ **CLOSED** — `ActionManager.ExecuteAction` checks `RunManager.IsEventPaused` (was the arch §3.4 gap). |
| `RunManager` 60-vs-100 game-over | **Unresolved** — RunManager caps prototype at `prototypeRunDays` (60); ResourceManager's `OnGameOver` is at 100. Pick one canonical threshold. |
| `EventManager.DrainQueue` heartbeat ordering | Runs via EventManager's own `OnTimeAdvanced` sub, not strict heartbeat step-5. Awaits control-inversion. |
| `ApplyFertilizerAction` Law-1 | **LIVE violation** — writes `tile.stats.*` directly; route through `ModifyTileStats`. |
| `CreateFirebreakAction` Law-1 | Direct `tile.stats`/`tile.tv` writes (dormant; fix before reactivation). |
| `InspectTrashAction.visualConfig` | Always null (`[SerializeField]` on `new` plain class). Sprite-swap disabled. |
| `TileEntitySO.OnValidate` | **MISSING** — arch §6b/§7 loud-fail validation (entityId unique/non-blank, stat-type, orphan nextStage) is not implemented yet. |
| `VillageBehaviourHook` event routing | Grabs `EventManager.Instance.FireEventByID` directly (S1 TODO at line 24 — route via `ctx.Events`). |
| `IEntityEventSink` vocabulary | STUB — `Raise` + `EntitySpawned`/`EntityDied` only; vocabulary deferred (arch §6.2). |
| `WeatherVFXController` bindings | SCAFFOLD — wire rain/godray/storm objects + thunder-flash events in Inspector. |
| Dialogue **Phase 2** (choice runtime) | NOT built — `ResolveConversationLines` skips `ChoicePayload`. Needs an interactive walker that pauses for player selection + choice-button UI in `ChatAppUI`; `OnSelectChoice` is stubbed (no side effects yet). |
| Dialogue data setup | Post-rework Unity wiring: author `CharacterProfileSO` for Azi/Bob + assign on `DialogueRegistry`; recreate a `ConversationSO` named `birthday_group_shoutout` or the birthday shout-out won't fire. |
| `ActionUI` (907) | Now legacy/dormant vs `ActionBarUI`; deletion candidate once ActionBarUI is committed. |
| `ActionSO` | NEW SCAFFOLD — not yet wired (Phase 5 supersedes `ActionIconConfig`). |
| `EntityArtTool` | Dormant; not TileEntitySO-aware — rework needed for arch §7. |
| `GameBootstrap` ordered init | SCAFFOLD — presence-validates in `Start`; bootstrap-driven ordered init is Phase 3. |
| `RegionManager` dead code | `WeightedPick` + `AddNeighborsToCandidates` uncalled. |
| `UIButtonAutoHook` | Duplicate-`Start()` quirk + dead `mainCamera` field. |

---

## P. Renovation scaffolding — `_GAME/Scripts/**` (`Habitales.*`)

The entity sub-tree is now **load-bearing** (every spawn routes through it). `ActionSO`, `IEntityEventSink`, and the namespace anchors remain SCAFFOLD.

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **StatTypes** | `_GAME/Core/StatTypes.cs` | **~90** | NEW · live | `Habitales.Core`. Enums `TargetStat` (8 stats + derived `SoilComposite`), `Comparator`, `OutcomeKind`; structs `StatChange` (stat+delta, applied directly clamped 0–100, **not** ÷6), `StatCondition` (stat+comparator+threshold+outcome+transformTarget+`onSatisfied` — **death conditions**), `StatGate` (stat+comparator+threshold — **promotion gate**, added 2026-06-24), `DailyStatDeltas` (8 `[Range(-10,10)]` substat sliders for Plant daily effects, added 2026-06-24). |
| **IEntityEventSink** | `Scripts/Core/IEntityEventSink.cs` | **47** | NEW · STUB | `Habitales.Core`. `Raise(EntityEvent)` + `EntitySpawned`/`EntityDied`; `EntityEvent` struct; `NullEntityEventSink.Instance` no-op default carried in `TickContext`. Vocabulary deferred — do not expand without a user decision. |
| **TileEntitySO** | `_GAME/Entities/TileEntitySO.cs` | **~75** | NEW · live · `[CreateAssetMenu]` · **Artifice-authored** | `Habitales.Entities`. Identity + data: `entityId` (save key), `displayName`, `category`, `tileSprite` (`[PreviewSprite]`), `vfxKey`, `billboardScale`, `nextStage`, `promoteAfterDays`, `requirePromoteCondition`+`promoteWhen` (now `StatGate`, `[EnableIf]` on the bool), `transitionEffects`, `plantDailyDeltas` (`DailyStatDeltas`, Plant-only) / `dailyEffects` (non-Plant only), `deathConditions`, optional `behaviour`. Fields grouped via `[BoxGroup]`. Updated 2026-06-24. ⚠️ **No `OnValidate`** (see §N). |
| **GenericTileEntity** | `_GAME/Entities/GenericTileEntity.cs` | **~165** | NEW · live | Extends `TileEntity`. `OnDailyUpdate`: apply `dailyEffects` + `plantDailyDeltas` (sliders, via `ApplyDailyDeltas`) → check `deathConditions` → timed transition (`nextStage`, gated by `Satisfied(StatGate)`) → optional hook. `Satisfied` has a shared core with `StatCondition`/`StatGate` overloads. Per-instance state (`daysExisting`, `hookState` dict incl. `inspected`/burn state) lives **here**, never on the SO. Updated 2026-06-24. |
| **EntityRegistry** | `Scripts/Entities/EntityRegistry.cs` | **74** | NEW · live · `[CreateAssetMenu]` | The **sole** `entityId → TileEntitySO` registry (entities + regions + persistence). `Build()`/`Get()` (loud-fail on miss) / `ValidateAll()` (loud-fail on null/blank/duplicate). Validated at boot by GameBootstrap + TileManager. |
| **EntityCategory** | `Scripts/Entities/EntityCategory.cs` | **12** | NEW · SUPPORT | enum `Plant/Building/Hazard/Debris` (CoverCrop removed). Replaces `is VillageEntity` checks. |
| **EntityBehaviourHook** | `Scripts/Entities/EntityBehaviourHook.cs` | **24** | NEW · live | Abstract SO escape hatch: `OnDailyUpdate(tile,entity,in ctx)` + `ReplacesGenericLifecycle` (false = supplements). Holds no per-instance state. |
| **FireBehaviourHook** | `Scripts/Entities/Behaviours/FireBehaviourHook.cs` | **66** | NEW · live · `[CreateAssetMenu]` | `ReplacesGenericLifecycle = true`. Damage + normalized spread ramp (`MIN_SPREAD_DAYS=3`, weather via `ctx.FireSpreadMultiplier`/`FireBonusDamage`), `ctx.Tiles.SpawnById(neighbor,"fire")`, respects `Firebreak` overlay. Burn state in `hookState`. No singleton grabs. |
| **VillageBehaviourHook** | `Scripts/Entities/Behaviours/VillageBehaviourHook.cs` | **65** | NEW · live · `[CreateAssetMenu]` | Supplements generic lifecycle. `KAINGIN_DAILY_CHANCE=0.015`, `FIRE_RADIUS=4`, spawns 4–7 fires sparing `Building` category. ⚠️ Grabs `EventManager.Instance.FireEventByID("first_kaingin")` directly (S1 TODO at line 24). |
| **TickContext** | `Scripts/Entities/TickContext.cs` | **26** | NEW · live | `Habitales.Entities`. `readonly struct` (`Tiles`, `FireBonusDamage`, `FireSpreadMultiplier`, `Events`) assembled once/day by RunManager, passed `in` to `UpdateAllEntities`. Entities never grab singletons mid-tick (S1). |
| **ActionSO** | `Scripts/Actions/ActionSO.cs` | **42** | NEW · SCAFFOLD · `[CreateAssetMenu]` | `Habitales.Actions`. Author-facing action data (arch §3.4). **Not yet referenced by live code** — Phase 5 supersedes `ActionIconConfig` + hardcoded subclass fields. |
| **RunEndCoordinator** | `Scripts/Meta/RunEndCoordinator.cs` | **80** | NEW · ACTIVE | See §J — the one fully load-bearing meta piece. |
| **Namespace anchors** | `Scripts/{Tiles,Regions,Meta,Dialogue,UI,Data,Editor,Utility}/_NamespaceAnchor.cs` | 4 ea | SCAFFOLD | Empty `namespace Habitales.X {}` reservations (S5). Delete each once its folder gets real content. (`Meta` already has `RunEndCoordinator` — its anchor can go.) |

---

## O. Open questions

1. **60-vs-100 game-over threshold** — RunManager caps the prototype at `prototypeRunDays` (60); ResourceManager fires `OnGameOver` at 100. **Needs a canonical decision** (the 100-day path is effectively unreachable in a prototype run).
2. **`ActionUI` (legacy, 907) vs `ActionBarUI` (active, 337)** — confirm the team has committed to ActionBarUI so the old file can be deleted rather than carried.
3. **`ForcedEntityPlacement.entityType`** — still a legacy display-name string; arch §3.6 wants the struct itself to carry a `TileEntitySO`/`entityId` (the spawn mechanism is already `SpawnById`).
4. **Resolved (locked) decisions from prior sweeps:** entity model → `TileEntitySO`+`entityId` (cube system removed, port DONE); Region is canonical (class rename done, folder `Zone/` rename pending); `EntityRegistry` sole registry; meta layer in `Habitales.Meta`.

---

*Resweep 2026-06-06 (#2) by an agent swarm reading `_GAME/` + `_UTILITIES/` directly (big files double-read & cross-checked). No game code was modified. LOC exact. Pair with HABITALES_ARCHITECTURE.md (the contract) — this is the map.*
