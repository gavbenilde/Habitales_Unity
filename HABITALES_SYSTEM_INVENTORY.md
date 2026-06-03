# Habitales — System Inventory (companion to HABITALES_ARCHITECTURE.md)

**Purpose:** The architecture doc says *what should be*. This says *what exists, where, and whether it's live* — the map a subagent reads before porting anything, so it never reproduces dormant behavior or treats the running build as legacy.

**How to read a row:** `Source` = where the authoritative code lives. `Status` is from `_GAME/Prototype/CLAUDE.md` (the design-override spec):
- **ACTIVE** — wired and shipping in the current prototype build.
- **DORMANT** — code remains on disk, do **not** delete, but is unwired/unused for the prototype. *Do not port as if live.*
- **NEW** — prototype-only system; exists only under `Prototype/Scripts/`, no `_GAME/` counterpart.
- **SUPPORT** — small types/helpers a system depends on.

**The three-source model (see HABITALES_ARCHITECTURE.md → Sources of Truth):**
1. `_GAME/Prototype/CLAUDE.md` — authoritative **design/behavior spec** (May 11 2026), layered on an April 5 "reverse brief." Read first for ACTIVE/DORMANT status + overrides.
2. `_GAME/**` (live code) — authoritative **implementation** of the spine + most systems, prototype-active via `isPrototypeRun` branches. **Not legacy.**
3. `_GAME/Prototype/Scripts/**` — the **NEW** prototype-only code (~965 LOC): planting cubes, progression, level-up, Azi/Boogle UI.

All paths below are relative to `Habitales/Assets/`. LOC is approximate (current sweep).

---

## A. Core / Run

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **RunManager** | `_GAME/_API/RunManager.cs` | 771 | ACTIVE | Run-level state, day-advance (`HandleTimeAdvanced`), **`CascadeTileUpdates()`** (global, 3-iter, lerp 0.15), peak-thriving snapshot, game-over (60-day cutoff in prototype). `GameManager → RunManager` rename already done (arch Phase 0 partial). |
| **GameBootstrap** | — | — | PLANNED | Does **not** exist yet. Arch §4 introduces it. Today there is no central singleton-init order guard. |

**RunManager holes (CLAUDE.md §9):** `AbortCurrentAction()` stubbed (partial tile progress on interrupt not applied); `IsEventPaused` guards `HandleActionCompleted` completion **only**, not `ActionManager.ExecuteAction` entry (the documented interrupt-before-action gap arch §3.4 closes).

---

## B. Tiles

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **Tile / TileStats** | `_GAME/Tile/_API/Tile.cs` | 48 | ACTIVE · CANON | POCO. 8 stats, `soilComposite` = mean of first 6, `CalculateHealth()` capped 33 @ contam>60. `isAnalyzed/issuesRevealed` default-true (reveal gate disabled). |
| **TileManager** | `_GAME/Tile/_API/TileManager.cs` | 586 | ACTIVE · CANON | Twin dicts (data/visual). Spawn, query, `ModifyTileStats`, entity mgmt. Arch adds `RefreshAllVisuals()` + `SpawnEntity(TileEntitySO)`. |
| **TileSelector** | `_GAME/Tile/_API/TileSelector.cs` | 727 | ACTIVE | Flood-fill seed selection + docked-card brush flow (single-seed; multi-seed deferred). Large — split if renovated. |
| **TileVisualizer** | `_GAME/Tile/_API/TileVisualizer.cs` | 162 | ACTIVE | Per-tile visual + substat colored-bar readout (bars only, no numbers). |
| **EntityVisualizer** | `_GAME/Tile/_API/EntityVisualizer.cs` | 166 | ACTIVE | Billboarding (`LateUpdate`) — cosmetic, RunManager does not touch (arch S3). |
| **Region rendering** | `RegionOutlineRenderer.cs` (145), `RegionBoundaryMeshBuilder.cs` (93), `RegionHealthUI.cs` (73) | — | ACTIVE | Per-zone outline + health UI. Note: `RegionHealthUI` (per-zone) vs `TileStats` UI (per-tile) flagged for post-prototype rename (CLAUDE.md §6.1). |
| **Tile support types** | `TileIssue.cs` (14), `IssueType.cs` (9), `TileIssueLibrary.cs` (75), `TileOverlayType.cs` (5), `TileVisualState.cs` (9) | — | SUPPORT | Issue/overlay enums + library. |

---

## C. Entities ⚠️ (see open tension)

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **TileEntity (hand-coded pipeline)** | `_GAME/Tile/_API/TileEntity.cs` | 459 | **MIXED** | One C# class per kind, `entityType` string identity. **Tree/Sapling/Seedling/Stump/DeadTree/CoverCrop×3/Trash×2 = DORMANT** (CLAUDE.md §7). **Fire + Village partly ACTIVE** (kaingin fires must exist for Fire Suppression). **Factory inert stub.** |
| **PlantedCubeEntity** | `_GAME/Prototype/Scripts/PlantedCubeEntity.cs` | 118 | **ARCHIVE-THEN-REMOVE** | Disposable prototype entity. Not ported. Frozen in `ARCHIVE/` at Phase 0, deleted from live tree after Phase 5. |

> ✅ **RESOLVED (locked).** Renovation targets the **hand-coded tree pipeline**, all ported onto `TileEntitySO` + `entityId`: Tree, Sapling, Seedling, Stump, DeadTree (shared), CoverCrop×3 (behaviour hook), Village (hook), Fire (hook), Trash×2, Factory. All become **ACTIVE** under the new SO model. `PlantedCubeEntity` is archive-then-remove.

---

## D. Resource / Time

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **ResourceManager** | `_GAME/Results/Resources/ResourceManager.cs` | 234 | ACTIVE · Law-1 exemplar | Clock (`AdvanceTime`, `AdvanceTimeStepped`), worker roster/states, fatigue, recovery, research points. Arch adds `AdvanceOneDay()`. `OnGameOver` repurposed → fires at `totalDays >= 60`. |
| **Worker / Factory** | `Worker.cs` (49), `WorkerFactory.cs` (69), `WorkerPortraitPool.cs` (32), `ResourceDisplay.cs` (69) | — | ACTIVE | Filipino-name roster + portraits. **Traits = display only** (mechanical traits disabled, CLAUDE.md §7). |
| **DayNightCycleHandler** | `_UTILITIES/Graphics/Shaders/Scripts/DayNightCycleHandler.cs` | — | ACTIVE · critical | `AdvanceTimeStepped`'s `WaitUntil(IsIdle)` gate depends on it. Lives outside `_GAME/`. |

---

## E. Weather

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **WeatherManager** | `_GAME/Weather/WeatherManager.cs` | 97 | ACTIVE | Dry/wet weighted rolls. **Prototype locks to Sunny** (`RollWeather` short-circuit when `isPrototypeRun`). Exposes `GetFireBonusDamage()`/`GetFireSpreadMultiplier()`/`GetWorkSpeedMultiplier()` as **methods** — arch §5.2 converts to Law-1 getters for `TickContext`. |

---

## F. Zones

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **ZoneManager** | `_GAME/Zone/ZoneManager.cs` | 892 | ACTIVE | Generation pipeline (flood-fill blob → fill holes → profile stats → theme → issues → buildings → organic entities → forced overrides → worker reward → `OnZoneGenerated`). First zone deterministic; unlock @ ~60% (prototype). **Largest single file — split if renovated.** |
| **Zone data** | `ZoneProfile.cs` (94), `ZoneTheme.cs` (9), `ZoneGenerationResult.cs` (27), `ForcedEntityPlacement.cs` (10) | — | ACTIVE | 6 prototype substat-themed profiles (`SO/Zones/Prototype/`). Arch §3.6: `ForcedEntityPlacement` migrates string → `TileEntitySO` (shares the entityId registry). |

---

## G. Actions

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **PlayerAction (base)** | `_GAME/Action/_API/PlayerAction.cs` | 63 | ACTIVE | Abstract base. Efficiency `ceil(BaseDays/sqrt(ppl/MinPpl))` clamp MinDays. Arch §3.4 → ActionSO + GenericSpawnAction split. |
| **ActionManager** | `_GAME/Action/_API/ActionManager.cs` | 180 | ACTIVE | Coroutine, tile-distribution-across-days. Arch adds `IsSimulationPaused` entry-gate. |
| **Action UI** | `ActionUI.cs` (926), `ActionBarUI.cs` (374), `DPadLayoutGroup.cs` (338), `CategoryButton.cs` (53), `ActionCategory.cs` (52), `ActionIconConfig.cs` (88), `ActionPanelState.cs` (11), `TrashVisualConfig.cs` (8) | — | ACTIVE | Docked-card brush flow. `ActionUI` is the **single biggest UI file (926)** — split if touched. `ActionIconConfig` replaced by `ActionSO` (arch §3.4). |
| **Active actions** | `FireSuppressionAction.cs` (33) · `Prototype/PlantingAction.cs` (106) · `Prototype/RemoveWitheredAction.cs` (27) | — | ACTIVE | Only 6 actions live: 4 Plant variants (PlantingAction), Fire Suppression, Remove Withered. |
| **Dormant actions** | `AnalyzeSoilSampleAction` (43), `EcologicalSurveyAction` (25), `InspectTrashAction` (57), `ClearTrashAction` (26), `StumpDeadTreeRemovalAction` (27), `CreateFirebreakAction` (39), `CoverCroppingAction` (63), `PlantTreesAction` (29, old), `ApplyFertilizerAction` (35) | — | DORMANT | All in `_GAME/Action/PlayerActions/`. Code stays; unused in prototype (CLAUDE.md §7). Do not port as live. |

---

## H. Events

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **EventManager** | `_GAME/Events/_API/EventManager.cs` | 257 | ACTIVE | Live because kaingin fire events fire from `VillageEntity` — player needs the interruption. `linkedThread` on `GameEventSO` kept **null** (thread authoring out of scope). |
| **Event support** | `EventPopupUI.cs` (95), `EventCameraHandler.cs` (177), `EventContext.cs` (122), `GameEventSO.cs` (37), `GameEventRegistry.cs` (7) | — | ACTIVE | `EventContext` = static token bus (unchanged). Arch §6 turns these into the meaning-event seam layer (STUB). |

---

## I. Dialogue / Chat

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **DialogueManager** | `_GAME/Dialogue/_API/DialogueManager.cs` | 526 | ACTIVE (simplified) | **40%/day** flavor message (replaces 13% gate). Azi Tier 1/2 hardcoded `string.Format`; Tier 3 → run-end speech bubble. |
| **Chat App UI** | `ChatAppUI.cs` (252), `ChatBubbleUI.cs` (54), `TabRowUI.cs` (53), `StickerButtonUI.cs` (30) | — | ACTIVE | Tab filtering: only workers/threads with ≥1 message. |
| **Dialogue support** | `DialogueRegistry.cs` (57), `DialogueRuntimeTypes.cs` (105), `DialogueTypes.cs` (43), `DialogueTabSO.cs` (15), `DialogueThreadSO.cs` (24) | — | SUPPORT | `DialogueThreadSO` **authoring path dormant** (Azi hardcoded). |
| **Dormant dialogue** | `StickerSO.cs` (16), `StickerLibrary.cs` (55), `WorkerMessageTemplateSO.cs` (11), `WorkerPortraitPool.cs`* | — | DORMANT | Sticker system disabled; 13%-gate template randomizer replaced; birthday system disabled (CLAUDE.md §7). |

---

## J. Results / Progression / Level-Up (mostly NEW)

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **EndGame (existing)** | `EndGameScreenUI.cs` (243), `EndGameData.cs` (44), `ZonePillUI.cs` (31) | — | ACTIVE | Reused by run-end snapshot (zone pills, Employee of the Year). |
| **RunSnapshot** | `_GAME/Prototype/Scripts/RunSnapshot.cs` | 37 | NEW · ACTIVE | Peak-thriving count + `Texture2D` screenshot at peak + carryover fields. |
| **Level-Up** | `LevelUpScreenUI.cs` (209), `PlayerProgressionSO.cs` (73), `ProgressionPersistence.cs` (106), `PendingLevelUp.cs` (4) | — | NEW · ACTIVE | XP bar, unlock card, fade-to-menu. `ProgressionPersistence` = the "PersistenceService" concept (JSON @ persistentDataPath; SO is schema only). |
| **Azi / Boogle UI** | `AziSpeechBubbleUI.cs` (78), `BooglePanelUI.cs` (36) | — | NEW · ACTIVE | Azi run-end bubble; Boogle = full-screen plant-info panel (post-prototype migrates into Tablet shell). |

> `RunEndSnapshotUI` / `PersistenceService` from CLAUDE.md do **not** exist as standalone files — realized as `EndGameScreenUI` reuse and `ProgressionPersistence` respectively.

---

## K. Planting Profiles — ARCHIVE-THEN-REMOVE (cube system, disposable)

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **PlantingProfileSO** | `_GAME/Prototype/Scripts/PlantingProfileSO.cs` | 46 | ARCHIVE-THEN-REMOVE | Cube-system schema. Replaced by `TileEntitySO`. |
| **PlantingProfileGenerator** | `_GAME/Prototype/Scripts/PlantingProfileGenerator.cs` | 50 | ARCHIVE-THEN-REMOVE | Procedural profile generator. Not ported. |
| **GeneratedPlantRegistry** | `_GAME/Prototype/Scripts/GeneratedPlantRegistry.cs` | 52 | ARCHIVE-THEN-REMOVE | **NOT** the planned `EntityRegistry` — retires with the cube system. |
| **HWBColor** | `_GAME/Prototype/Scripts/HWBColor.cs` | 23 | ARCHIVE-THEN-REMOVE | HWB→RGB for cube tint. Tree pipeline uses sprite/billboard visuals instead. |

---

## L. UI / Misc / Utilities

| System | Source | LOC | Status | Role & notes |
|---|---|---|---|---|
| **Inspect / Examine** | `InspectPanelUI.cs` (252), `ExamineResultPopupUI.cs` (204), `InspectModeManager.cs` (92) | — | DORMANT-ish | Examine/inspect loop gated off (`isAnalyzed` default-true; trash/issues not player-facing). Verify per-screen before assuming dead. |
| **HUD / Menu** | `HealthBarUI.cs` (97), `MainMenu.cs` (85), `UnlockNextZoneButtonUI.cs` (71), `ObjectiveBannerUI.cs` (53), `OverflowTip.cs` (47), `OverflowTipSpawner.cs` (43), `HoverToolttip.cs` (40) | — | ACTIVE | General HUD/menu support. (`HoverToolttip` — note the typo'd filename.) |
| **EntityArtTool** | `_GAME/Utilities/EntityArtTool.cs` | 169 | ACTIVE (editor) | Existing entity authoring helper — the seed for arch §7's authoring surface. |
| **VFXManager** | `_UTILITIES/Graphics/VFX/Scripts/VFXManager.cs` | — | SHELL | Level-up VFX may route through it; otherwise no callers (CLAUDE.md §9). |

---

## M. Out-of-scope / third-party (do not touch)

- `Assets/1_Archive/` — pre-existing old code (`HardCode.cs`, `playerController.cs`). Unrelated to the planned renovation `ARCHIVE/`.
- `Assets/Plugins/FMOD/**` — third-party audio middleware. Do not modify.

---

## N. Known stubs / hard TODOs (CLAUDE.md §9)

| Location | Status |
|---|---|
| `RunManager.AbortCurrentAction()` | Stubbed — partial tile progress on interrupt not applied. |
| `IsEventPaused` → `ActionManager.ExecuteAction` entry | Partial — guards completion only, not entry (arch §3.4 closes). |
| `FactoryEntity.OnDailyUpdate()` | Stubbed (inert). |
| `VFXManager` | Shell. |
| `BooglePanelUI` | Standalone canvas; designed to re-parent into a future Tablet UI shell. |

---

## O. Open questions — RESOLVED (locked decisions)

1. **Entity model** → **hand-coded tree pipeline** (renovated onto `TileEntitySO` + `entityId`). `PlantedCubeEntity` system is archive-then-remove. See §C.
2. **The "April 5 reverse brief"** → it is `ARCHIVE_Habitales_Design_Direction_Synthesis.md` (repo root). Effectively a system inventory; CLAUDE.md defers to it for unmentioned systems. This inventory doc supersedes it for the renovation.
3. **Zone vs Region** → **Region** is canonical. Classes rename in Phase 2.5 (`ZoneManager → RegionManager`, etc.); namespace `Habitales.Regions`.
4. **`GeneratedPlantRegistry` vs `EntityRegistry`** → **not consolidated.** `GeneratedPlantRegistry` retires with the cube system. `EntityRegistry` (`entityId → TileEntitySO`) is the sole registry for entities, forced-entities, and persistence.
5. **GameBootstrap** → created in **Phase 0c** (`Habitales.Core`, `[DefaultExecutionOrder(-1000)]`); validates singleton init order + `EntityRegistry.ValidateAll()` + single `ProgressionPersistence.Load()`.

**Alpha framing:** this renovation targets a stable **Alpha** (playable by anyone), full game vision — the full tree pipeline + the full action roster reactivated. The meta layer (XP/level-up, scoring, Azi bubble) is kept and re-housed into `Habitales.Meta`, decoupled from the removed cubes; **XP unlocks nothing functional in Alpha (cosmetic/feel only).**

---

*Generated by reading `_GAME/` and `_GAME/Prototype/` directly. No game code was modified. LOC approximate. Pair with HABITALES_ARCHITECTURE.md (the contract) — this is the map.*
