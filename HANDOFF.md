# Handoff Brief — Entity System Cutover (Phase 4b/4c/4d)

**Branch:** `fix/tile-flood-fill-selection-flow`
**State:** All code compiles clean (`dotnet build Assembly-CSharp.csproj` → **0 errors**, 47 pre-existing warnings).
**Phase 4 is COMPLETE** (4b→4c→4d) and the entity system is now **100% generic/data-driven** — all legacy
entity classes (incl. CoverCrop), the `entityType` string, and `SpawnEntity<T>`/`TransformEntity<T>` are gone.
Entities confirmed spawning/visible in-Editor. **Phase 5** has the selected actions registered. Remaining:
play-test + the pending in-Editor scene-wiring chores.

---

## ✅ RESOLVED — "no entities appear" (was: registry not wired)

The earlier blocker (empty tiles, no GameObjects) was the new `SpawnById` path bailing because the
`EntityRegistry` wasn't created/assigned. **User wired the registry + SOs; entities now spawn and render.**
Kept here for reference — if it recurs, check the Console for the Law-3 logs below. The cutover routes
*every* spawn through `TileManager.SpawnById(tile, entityId)`, which resolves the id via a serialized
`EntityRegistry`. That path **early-returns and spawns nothing** when the registry is missing or the id
isn't found:

```
SpawnById → if (entityRegistry == null) { LogError; return; }      // nothing spawns
          → def = entityRegistry.Get(id);  // null on a miss → LogError, return
          → SpawnFromDef(...)              // only here is the entity + GameObject created
```

Because no `EntityRegistry` asset has been created/assigned yet, **0 entities spawn → no `Entity_*`
GameObjects → empty tiles.** This matches the symptom exactly.

**First thing to do: open the Console.** The Law-3 logging will say precisely which case it is:
- `"TileManager: entityRegistry is not assigned …"` (Awake) → registry not wired.
- `"TileManager.SpawnById('<id>'): entityRegistry not assigned."` → registry not wired.
- `"EntityRegistry: no entity with id '<id>'. …"` → registry wired but missing/typo'd id.

### Fix = the in-Editor wiring checklist (the gate to runtime)

| # | Action |
|---|--------|
| 1 | **Create the `EntityRegistry` asset** (`Create ▸ Habitales ▸ Entities ▸ Entity Registry`). Drag **all** `TileEntitySO`s into its `entities` list. |
| 2 | **Assign it** to `TileManager`'s new **`entityRegistry`** field (Header: "Entity System"). |
| 3 | **Fix two entityIds:** `tree_mature.asset` currently has id `trash_nonbio` (copy-paste error) → set to `tree_mature`. `village.asset` has a **blank** id → set to `village`. (Both fail `ValidateAll` until fixed.) |
| 4 | **Create `fire.asset`** (id `fire`) — there is no fire SO yet. The fire spread/suppression code spawns `"fire"`. |
| 5 | **Create the two behaviour-hook assets:** `Create ▸ Habitales ▸ Entities ▸ Behaviours ▸ Fire` and `… ▸ Village`. Assign them to the `behaviour` field of `fire.asset` and `village.asset` respectively. |
| 6 | **Categories:** `village` & `factory` MUST be **Building** (the kaingin "don't torch buildings" check uses `def.category == Building`). Trash → Debris. Covercrops → Plant. |
| 7 | **`tileSprite`** on every SO — the visualizer's data path uses it; blank = invisible GameObject (spawns but you see nothing). |
| 8 | **`vfxKey`** — blank = no VFX (guarded, no error). Set it where you want VFX (especially fire). |
| 9 | Fill `dailyEffects` / `deathConditions` / `nextStage` chains per the canonical entity table (your design doc). Empty SOs spawn but are inert. |

The spawn-site code maps **legacy `RegionProfile` display strings** ("Seedling", "Mature Tree", …) to the
new ids, so you do **not** need to re-key existing zone profile assets.

After wiring: **play-test.** Verify (a) entities show sprites, (b) trees grow seedling→sapling→mature
and die into deadtree, (c) fire spreads and Fire Suppression removes it, (d) villages start kaingin fires.

---

## What was done this session

### 1. Cube retirement (code) — DONE, compiles
- Stripped cube/Boogle refs from `ActionManager`, `ActionBarUI`, `ActionUI`, `PlayerProgressionSO`,
  `ProgressionPersistence`, `LevelUpScreenUI`.
- **Deleted 8 files:** `PlantedCubeEntity`, `PlantingProfileSO`, `PlantingProfileGenerator`, `HWBColor`,
  `GeneratedPlantRegistry`, `PlantingAction`, `RemoveWitheredAction`, **`BooglePanelUI`** (it referenced
  the deleted `PlantingProfileSO`).
- `ActionManager.RegisterActions()` now registers **`FireSuppressionAction` + `PlantTreesAction`** only.
- **Scene cleanup still pending (yours):** remove the `BooglePanel` GameObject/prefab, cube material,
  and any leftover cube GameObjects; the dropped `ActionManager` serialized fields self-clear on import.

### 2. Phase 4b safe slice — DONE, compiles
- Removed `CoverCrop` from `EntityCategory` (was last value; existing indices unaffected).
- **`TickContext` threaded** through the heartbeat: `RunManager.HandleTimeAdvanced` builds one context
  per day from `WeatherManager` (`GetFireBonusDamage`/`GetFireSpreadMultiplier`, `NullEntityEventSink`)
  and passes it to `TileManager.UpdateAllEntities(in ctx)`. `TileEntity`'s base `(Tile, in ctx)` overload
  bridges to the legacy `(Tile, TileManager)` tick so any remaining hand-coded entity still ticks.
- **`EntityVisualizer`** prefers `def.tileSprite`; falls back to the legacy string-switch.

### 3. Phase 4b/4c entity cutover — DONE, compiles (runtime-blocked, see above)
- **Behaviour hooks** (new, `Assets/_GAME/Scripts/Entities/Behaviours/`):
  - `FireBehaviourHook` — ports `FireEntity` (damage, ramped spread, burn-out). `ReplacesGenericLifecycle
    = true`. Reads weather from `ctx`, not the singleton. Burn state in `GenericTileEntity.hookState`.
  - `VillageBehaviourHook` — ports the 1.5%/day kaingin cluster; skips `Building`-category tiles.
- **`TileManager`:** added serialized `entityRegistry` + boot `ValidateAll()`; new `SpawnById(tile,id)` /
  `SpawnFromDef(tile,def)`; migrated the visual-name + VFX-key sites to `entityId`/`def.vfxKey`.
- **All live spawn sites flipped to `SpawnById`:** `PlantTreesAction`, RegionManager forced-placement,
  organic placement, and building placement (village/factory).
- **`entityType → entityId` (live sites):** `FireSuppressionAction` (new `AnyTileHasEntityId` helper on
  `PlayerAction`), `PlantTrees` factory check, TileManager name/VFX.

### 4. Dormant-action migration + Phase 4d — DONE, compiles
- **Migrated the remaining legacy consumers** off `is XEntity` to `entityId`/`category` (kept dormant,
  ready for Phase 5): `ClearTrashAction`, `StumpDeadTreeRemovalAction`, `CreateFirebreakAction`,
  `InspectTrashAction` (its `isInspected` flag now lives in `GenericTileEntity.hookState`), and
  `ExamineResultPopupUI`'s trash count.
- **Deleted 10 legacy `TileEntity` subclasses:** Fire, Village, Tree, Sapling, Seedling, DeadTree, Stump,
  Factory, TrashBio, TrashNonBio. Nothing references them anymore.

### 5. Phase 5 — action registration — DONE (selected set), compiles
Registered in `ActionManager.RegisterActions`: `FireSuppression`, `PlantTrees`, **`ApplyFertilizer`**,
**`ClearTrash`**, **`StumpDeadTreeRemoval`**, **`InspectTrash`**. Still dormant (commented):
`CreateFirebreak`, `AnalyzeSoilSample`, `EcologicalSurvey`.

### 6. Full CoverCrop cut — DONE, compiles → entity system is now 100% generic
- Deleted `CoverCroppingAction` + the 3 CoverCrop entity classes.
- Removed the now-dead infra they were keeping alive: `TileManager.SpawnEntity<T>` / `TransformEntity<T>`
  / `UpdateEntitiesInRegion` (no callers), the legacy `(Tile, TileManager)` tick overload + the base bridge,
  the `entityType` field (mirror), and `EntityVisualizer`'s per-type Sprite fields + string-switch fallback.
- `TileEntity` is now a thin base: `health`, `entityId`, `def`, and a single abstract
  `OnDailyUpdate(Tile, in TickContext)`. `EntityVisualizer` is def-only.

---

## What's next (in order)

1. **Play-test the newly live actions.** Trash/Stump/Dead actions need their entities present to be useful —
   set the relevant spawn chances in `RegionProfile` assets (`bioTrashSpawnChance`, `deadTreeSpawnChance`,
   `stumpSpawnChance`) and/or `forcedEntities`.
2. **Pending scene-wiring chores** (in-Editor, from earlier handoffs): wire `RunEndCoordinator`; cube scene
   cleanup (remove `BooglePanel` GameObject/prefab + cube material).

## Known caveats / TODOs
- **`InspectTrashAction.visualConfig` is always null.** It's a `[SerializeField]` on a plain C# class created
  via `new` — Unity can't populate it. Inspect marks tiles inspected (`hookState["inspected"]`) but won't
  swap trash sprite variants. To restore that, source the `TrashVisualConfig` another way (e.g. a registry/
  singleton lookup, or move the config onto a MonoBehaviour/SO the action can reach).
- **Village kaingin** still calls `EventManager.Instance.FireEventByID("first_kaingin")` directly
  (pre-existing S1). TODO: route via `ctx.Events` once an `EventManager` adapter sink exists.
- **Still pending from earlier handoff:** scene-wire `RunEndCoordinator` (add component, assign
  `PlayerProgressionSO`, wire `RunManager.runEndCoordinator`).

## Key files
- `Tile/_API/TileManager.cs` — registry + `SpawnById`/`SpawnFromDef`, visual/VFX migration
- `Tile/_API/TileEntity.cs` — thin base (`health`/`entityId`/`def` + abstract `OnDailyUpdate(in ctx)`)
- `Scripts/Entities/GenericTileEntity.cs` — the generic runtime entity (lifecycle eval)
- `Scripts/Entities/EntityRegistry.cs` — the `entityId → SO` lookup (asset now wired)
- `Scripts/Entities/Behaviours/FireBehaviourHook.cs`, `VillageBehaviourHook.cs` — NEW (need hook assets)
- `Tile/_API/EntityVisualizer.cs` — def-driven visuals + legacy fallback
- `Zone/RegionManager.cs` — forced/organic/building spawn → `SpawnById`
- `_API/RunManager.cs` — heartbeat builds + threads `TickContext`
- `Action/PlayerActions/{FireSuppression,PlantTrees}Action.cs`, `Action/_API/PlayerAction.cs`

## Compile-verify without Unity (handy)
```
cd Habitales && dotnet build Assembly-CSharp.csproj -nologo -clp:ErrorsOnly
```
Note: `Assembly-CSharp.csproj` is Unity-generated and was hand-edited this session (cube files removed,
2 hook files added). **Unity regenerates it on import — those edits are throwaway.** New `.cs` files get
their `.meta` (and GUIDs) generated by Unity on next open; assign the hook/registry assets after that.
