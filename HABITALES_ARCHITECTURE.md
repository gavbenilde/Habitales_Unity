# Habitales — Master Architecture Document

**Purpose:** This is the single source of truth for the Habitales architecture renovation. It is written to be read top-to-bottom by an Opus orchestrator, which will split it into work-orders for Sonnet subagents. Humans skim it; agents parse it; it lives in the repo.

**Scope of this document:** The **core spine** (RunManager, Tile, TileManager, ResourceManager, PlayerAction), the **entity system renovation** (new `TileEntitySO` + entity-ID system, with the existing hand-coded entities ported onto it), **Zones** (ZoneManager/ZoneProfile renovated to honor the Laws + the entityId migration), **persistence** (save-safe on `entityId`, in-scope), the **event layer as a stub** (insertion points marked, vocabulary deferred to its own thread), and the **Entity Art Tool** authoring surface (feel specified, exact visual layout deferred). Weather and Dialogue are touched only where the spine intersects them (Law-1 getters; event seams).

**Framing — Renovate, don't replace.** The existing systems work and have proven feel. They are the behavioral reference, not throwaway code. The first build step is to move all current scripts into `ARCHIVE/`. Subagents diff against `ARCHIVE/` to preserve behavior; this document specifies the *deltas* — what changes, and why.

**Assumed dependencies:** Unity (C#, URP-compatible), LeanTween, JSON persistence, **ArtificeToolkit** (free; the assumed attribute-based custom-inspector layer for all authoring surfaces).

**PRESERVATION MANDATE — the current game is the "correct game."** The live game code is technically functional and represents the team's proven mechanics. It is **not** to be eradicated, gutted, or "cleaned up" in service of matching this document. Where the document and the game disagree, the game's *behavior* wins and the document is corrected — never the reverse. "Renovate" means re-housing proven behavior into the new structure, not deleting working functionality to satisfy a spec. A subagent that finds a discrepancy stops and flags it; it does not trash game code to resolve it.

**SOURCES OF TRUTH — read in this order, they play different roles (do not conflate them):**

1. **`_GAME/Prototype/CLAUDE.md` — the authoritative DESIGN/BEHAVIOR spec.** This is the team's *current consensus*, dated May 11 2026, layered on top of an earlier "April 5 reverse brief." It is **not code** — it is the override map that tells you, per system, what is **ACTIVE / DORMANT / OUT-OF-SCOPE** for the current build, and what the live behavior *should* be. **Read this first** to know which code is even live before porting anything. Its rule: *"if a system is not mentioned here, the reverse brief still applies; if it is here, the prototype overrides apply."*
2. **`_GAME/` live code — the authoritative IMPLEMENTATION of the spine and most systems.** RunManager, TileManager, ResourceManager, Zones, Weather, Events, Dialogue, Actions all live here and are **prototype-active**, with prototype-only behavior gated by `isPrototypeRun` branches (not duplicated elsewhere). This is **not legacy** — it is the running game. It is the behavioral reference to port, *as constrained/overridden by CLAUDE.md*.
3. **`_GAME/Prototype/Scripts/` — NEW prototype-only code** (~965 LOC). Two fates:
   - **ARCHIVE-THEN-REMOVE** (the cube-planting slice): `PlantedCubeEntity`, `PlantingProfileSO`, `PlantingProfileGenerator`, `HWBColor`, `GeneratedPlantRegistry`. Not ported — replaced by the tree pipeline + `EntityRegistry`.
   - **KEEP & RE-HOUSE** (the meta layer, decoupled from cubes): `PlayerProgressionSO`, `RunSnapshot`, `ProgressionPersistence`, `LevelUpScreenUI`, Azi/Boogle UI. Ported into `Habitales.Meta`. **In Alpha, XP unlocks nothing functional — cosmetic/feel only** (full roster available from start; level-up screen stays as a reward moment).
4. **`ARCHIVE/`** — the frozen snapshot taken at renovation start (Phase 0), used purely as a stable diff target.

**Porting rule:** to renovate a system, (a) check `CLAUDE.md` for its ACTIVE/DORMANT status and overrides, then (b) port the live `_GAME/` implementation (or `Prototype/Scripts/` for the new-only systems) honoring those overrides. **Do not port a DORMANT system as if it were live, and do not assume `_GAME/` is stale — it is the running build.** The System Inventory (companion doc) enumerates, per system, where the authoritative code lives and its ACTIVE/DORMANT status.

> ✅ **Entity model — LOCKED.** The renovation targets the **hand-coded tree pipeline** (Tree/Sapling/Seedling/Stump/DeadTree, CoverCrop, Village, Fire, Trash, Factory), renovated onto `TileEntitySO` + `entityId` per §5. The prototype `PlantedCubeEntity` system was a disposable prototyping detour: `PlantedCubeEntity`, `PlantingProfileSO`, `PlantingProfileGenerator`, `HWBColor`, `GeneratedPlantRegistry`, `PlantingAction`, and `RemoveWitheredAction` are **archive-then-remove** — frozen in `ARCHIVE/` at Phase 0, deleted from the live tree after Phase 5 verification. They are **not** ported. The §5 port map below is the target.
>
> ✅ **Alpha goal — LOCKED.** This renovation brings the game to a stable **Alpha** (playable by anyone), full game vision: the complete tree pipeline + the full action roster (the actions the prototype shelved are reactivated). The prototype's DORMANT list was prototype-only; Alpha restores the real game.

---

## 0. How to use this document (orchestrator note)

1. Read Sections 1–2 fully before splitting work. The Laws and the Heartbeat are global invariants every subagent must hold.
2. Section 7 (Build Order) gives the dependency graph and what is safely parallel. Split along those lines.
3. Every subagent work-order must carry Section 1 (the Laws) verbatim in its context. The Laws are what keep parallel work coherent.
4. Where this document says **STUB**, the subagent creates the seam and the signature but not the full implementation. Do not let a subagent "helpfully" fill in a stub that is deliberately deferred.
5. Where this document says **PORT**, the subagent reproduces behavior from `ARCHIVE/`, verified by reading the archived source, not by inventing from the prose description.

---

## 0.5 Implementation Status — reconciled 2026-06-06 (agent-swarm resweep)

This section maps the contract below onto **what is actually built**, verified by an agent-swarm re-read of every `_GAME/**` file (the five largest double-read & cross-checked). It is the bridge between this document (the target) and `HABITALES_SYSTEM_INVENTORY.md` (the map). **Legend:** ✅ DONE · 🟡 PARTIAL · ⬜ NOT STARTED · ❌ DIVERGES (built differently than specced — code wins, see note).

| Spec § | Item | Status | Note (source: live code) |
|---|---|---|---|
| §2.1 | Day-advance heartbeat owned by RunManager (`HandleTimeAdvanced`) | ✅ | Per-day loop: entity-tick → cascade → thresholds → collapse → unlock-flag → peak → cutoff → visuals → `OnDayResolved`. |
| §2.1 | Step **order** matches spec exactly | ❌ | Code runs **entities BEFORE cascade** (spec lists cascade as step 3 after entities — functionally fine, but the documented "cascade then entities" ordering is not literal). |
| §2.1 | `EventManager.DrainQueue` as ordered heartbeat step 5 | 🟡 | Runs via EventManager's own `OnTimeAdvanced` subscription, **not** inside the heartbeat loop. "Strict step-5 ordering awaits the control-inversion" (code comment). |
| §2.1 | `IsSimulationPaused` gates **entry** of a new day | 🟡 | Pause exists as `RunManager.IsEventPaused`, but it gates **action entry** (via ActionManager, see §3.4), not day-loop entry. Name differs. |
| §2.1b | Cascade port (global, 3-iter, lerp 0.15, contamination-overlay sync @ >60) | ✅ | `RunManager.CascadeTileUpdates()`; iter/lerp are serialized defaults (3 / 0.15). |
| §2.1b | `EvaluateThresholds` (tier-crossing detection, `lastTier` on runtime tile, fires `OnTileTierChanged`) | ✅ | Tile carries `lastTier`+`tierSeeded`; first eval seeds without firing. |
| §2.3 | RunManager Law-1 getter surface (`CurrentDay`/`CurrentYear`/`IsSimulationPaused`/`WorldHealth`) | ❌ | **None of those four exist on RunManager.** Clock getters live on ResourceManager; avg-health on RegionManager. RunManager exposes `IsEventPaused`, `ZoneUnlockThreshold`, `PeakThrivingCount`, etc. (See §2.3 inline note.) |
| §3.1 | Tile POCO + `TileStats` + runtime `lastTier` | ✅ | Preserved; capped-33 health rule intact. |
| §3.2 | TileManager twin-dict, `RefreshAllVisuals()`, `SpawnById`/`SpawnFromDef`/`ReplaceWithSO`/`RemoveEntity`, `FindObjectOfType` removed | ✅ | `SpawnEntity<T>`/`TransformEntity<T>`/`UpdateEntitiesInRegion` deleted. Third `activeVFX` dict added. |
| §3.3 | ResourceManager `AdvanceOneDay()` primitive + `AdvanceTimeStepped` wrapper + Law-1 getters | ✅ | All getters present (computed on demand). |
| §3.3 | ResourceManager meaning-events (`OnWorkerRecovered`/`OnWorkerBirthday`) | 🟡 | **`OnWorkerRecovered(Worker)` added & fired** per-worker in `CheckWorkerRecovery` (count-based `OnPeopleRecovered` kept for UI). `OnWorkerBirthday(Worker)` declared as a seam but **not raised** (birthday system dormant, CLAUDE.md §7). |
| §3.4 | `ActionManager.ExecuteAction` **entry-gated** by pause | ✅ | Checks `RunManager.Instance.IsEventPaused` at entry (ActionManager.cs:83–87). **The interrupt-before-action gap is CLOSED.** |
| §3.4 | `ActionSO` (data) + `GenericSpawnAction` | ⬜ | `ActionSO` exists as **unwired SCAFFOLD**; `GenericSpawnAction` not built. Actions are still hand-coded subclasses using `ActionIconConfig`. |
| §3.5 | `StatChange` / `StatCondition` / `TargetStat` / `Comparator` / `OutcomeKind` | ✅ | In `Habitales.Core.StatTypes`; `StatChange` applies direct (not ÷6). |
| §3.6 | Region generation pipeline preserved | ✅ | All steps present in `RegionManager`. |
| §3.6 | `forcedEntities` spawn via the registry (`SpawnById`) | 🟡 | **Spawn mechanism** is `SpawnById` for forced/organic/buildings ✅, but `ForcedEntityPlacement.entityType` is **still a legacy display-name string** with a name→id switch (the struct migration to a `TileEntitySO`/`entityId` ref is not done). |
| §3.6 | `aziCalloutKey` → `OnRegionGenerated`/`OnZoneUnlocked` seam (decouple from DialogueManager) | 🟡 | `OnRegionGenerated` event exists and DialogueManager subscribes it (decoupled). `OnZoneUnlocked`/`OnRegionUnlocked` lives on **RunManager**, not RegionManager. |
| §3.6 | RegionManager getters `GetZoneHealth`/`UnlockedRegions`/`ZoneCount` | 🟡 | `GetRegionHealth(int)` + `GetTotalAverageHealth()` present; `UnlockedRegions`/`ZoneCount` absent (unlock state on RunManager). |
| §4 | Class rename Zone→Region | ✅ | `RegionManager`/`RegionProfile`/`RegionTheme`/`RegionGenerationResult` done; `regionID` canonical. |
| §4 | Folder & namespace layout | ❌ (plan revised 2026-06-24) | **Structure directive changed:** kept feature folders, **dissolved** the `Scripts/` tree and all `_API/` subfolders, moved every SO to a top-level `_SO/` (art in `_ART/`). Folder `Zone/` name retained (classes are `Region*`); namespaces no longer mirror folders (S5 revised). GUIDs preserved. |
| §4 | `GameBootstrap` `[DefaultExecutionOrder(-1000)]` presence-validator | 🟡 | Validates 8 singletons (+EntityRegistry) in `Start()`; bootstrap-driven **ordered init** is still Phase 3. |
| §5.1 | `TileEntitySO` identity model (`entityId`, stage chain, dailyEffects, deathConditions, behaviour) | ✅ | Replaces string `entityType`. |
| §5.1 | One generic runtime `TileEntity` (`GenericTileEntity`) | ✅ | The 10 hand-coded kinds are deleted; per-instance state on the runtime entity. |
| §5.2 | `TickContext` assembled once/day, threaded into `UpdateAllEntities(in ctx)` | ✅ | Entities read weather from `ctx`, never a singleton. |
| §5.2 | WeatherManager Law-1 getters for the context | ✅ | `FireBonusDamage`/`FireSpreadMultiplier`/`FatigueK`/`WorkSpeedMultiplier` properties + legacy `GetX()` wrappers. |
| §5.3 | Behaviour-hook ports: **Fire** (replaces), **Village** (supplements) | ✅ | Both built. **S1 closed for Village:** the kaingin event now routes through `ctx.Events.Raise(kind=ScriptedEvent, cause="first_kaingin")` → `EventManagerEntitySink` → `EventManager.FireEventByID`; no singleton grab. |
| §5.3 | Behaviour-hook port: **CoverCrop ×3** | ⬜ | Removed with the cube cut; the 3-SO + shared-hook plan is **not yet rebuilt**. |
| §5.3 | Pure-data ports (Tree/Sapling/Seedling/Stump/DeadTree/Trash×2/Factory) | 🟡 | Code path is generic & done; the `.asset` definitions exist (`tree_*`, `deadtree (shared)`, `stump (shared)`, `trash_*`, `factory`, etc.) but their `dailyEffects`/`deathConditions`/`nextStage` authoring is **Inspector data, not verifiable from code** — playtest to confirm growth/death. |
| §5.4 | `entityId` is the save key (contract) | 🟡 | The **contract/rehydrate path** exists (registry `Get` by id, loud-fail on miss). |
| §5.4 | JSON **entity** save/load implementation | ⬜ | No entity save/load code found. `ProgressionPersistence` is **meta-XP only**, not tiles/entities. (HANDOFF's "Phase 4d done" = the id-as-key contract, not a working entity save system.) |
| §6.1 | `IEntityEventSink` stub + `NullEntityEventSink` default | ✅ | `Raise` + `EntitySpawned`/`EntityDied`; vocabulary deferred. |
| §6.1 | HOOK seams: `OnDayResolved` / `OnTileTierChanged` / `OnRegionUnlocked` | ✅ | All three present on RunManager. |
| §6.1 | HOOK seams: `TileManager.OnEntitySpawned`/`OnEntityDied`, `ResourceManager.OnWorkerRecovered`/`OnWorkerBirthday` | 🟡 | **Added.** `TileManager.OnEntitySpawned`(fired in `SpawnFromDef`)/`OnEntityDied`(fired in `RemoveEntity`, suppressed on the replace pre-clear so promotion≠death). `OnWorkerRecovered` fired; `OnWorkerBirthday` seam-only. Still ⬜: no real sink consumes them yet (entities ride `NullEntityEventSink`). |
| §7 | Entity Art Tool authoring surface (sectioned form, `OnValidate` shouts, ArtificeToolkit) | ⬜ | `TileEntitySO` has **no `OnValidate`**; existing `EntityArtTool.cs` is a dormant runtime sprite-placer, not TileEntitySO-aware. Nothing of §7 built. |
| §8 | `GameLog` static category toggles | ✅ | `Core/Action/Cascade/Dialogue/RegionGen/Entity`. |
| §8 | `DebugOverlay` + gizmos | ⬜ | Not built. |
| §10 | Outstanding **Law violations** to fix | — | ~~`ApplyFertilizerAction` writes `tile.stats` directly (LIVE)~~ — **fixed**: routes through `TileManager.ApplyStatChanges` (new §3.5 owner-side StatChange write path). `CreateFirebreakAction` direct writes still open (dormant). ~~`VillageBehaviourHook` singleton grab (S1)~~ — **fixed** (routes via `ctx.Events`). |

**Phase roll-up (vs §9 build order):** Phase 0 ✅ · Phase 1 ✅ · Phase 2 ✅ (managers, getters, weather, event-stub) · Phase 2.5 🟡 (Region rename + SpawnById done; folder/namespace + `ForcedEntityPlacement` struct + region-unlock getters pending) · Phase 3 ✅ heartbeat/cascade/thresholds/TickContext/entry-gate (🟡 DrainQueue ordering, GameBootstrap ordered-init) · Phase 4 ✅ generic entity + Fire/Village hooks (🟡 pure-data asset authoring; ⬜ CoverCrop rebuild) · Phase 4.5 ⬜ entity persistence · Phase 5 🟡 6 actions registered & live, but still hand-coded (⬜ ActionSO/GenericSpawnAction; legacy `ActionUI` superseded by `ActionBarUI`) · Phase 6 ⬜ authoring surface / OnValidate / DebugOverlay.

---

## 1. Architectural Laws

These are non-negotiable invariants. They are short on purpose. Every subagent holds these in context.

### Law 1 — Getters, not setters. (Read anywhere, write only at home.)

Any system may **read** any other system's state through public read-only properties. No system may **write** another system's state directly.

```csharp
// CANON
public int AvailableWorkers { get; private set; }   // anyone reads; only ResourceManager writes
```

- Expose data as `{ get; private set; }` or computed `=>` properties.
- Mutation happens only through the owning system's explicit methods (`AdvanceTime`, `ModifyTileStats`, `SpawnEntity`).
- This is what makes "readable anywhere" safe. `<System>.Instance.<Property>` is the intended access pattern for UI and for any consumer. It is normal C#; it is not global mutable state, because the setter is private.

**Rationale:** "Readable anywhere" and "writable anywhere" look similar and are opposites. The first is convenience; the second is the coupling that rots a codebase. This Law keeps the first and forbids the second.

### Law 2 — Hooks fire on meaning, not on mutation.

Events represent **meaningful world moments**, never raw field writes.

- A stat changing is **mutation** → no event.
- A tile crossing Critical→Degraded is **meaning** → event.
- A seedling losing health is **mutation** → no event.
- A seedling *dying* is **meaning** → event.

**Rationale:** A cascade touches every tile three times per day-advance. If every stat write fired an event, the narrative systems (story weaver, messaging app) would drown in thousands of meaningless signals per day. Events are for the consumers that care about *what happened in the world*, not *what number changed*. Every event in the registry must be one a narrative or UI system would genuinely subscribe to.

> The full event vocabulary is **deferred to its own thread** (see Section 6). This Law still governs: any hook a subagent adds while building the spine must be meaning-level, and must be registered at the insertion points in Section 6, not scattered.

### Law 3 — The further from code, the louder the failure.

Feedback must scale inversely with the user's distance from the source.

- **Programmers** read stack traces. Internal systems may fail with exceptions.
- **Data authors** (writers/designers filling SO forms) read red text in the Inspector. Misconfiguration surfaces via `OnValidate()` warnings.
- **Artists** (wiring references, placing prefabs) read console errors that highlight the broken object. Every artist-touched reference uses loud-fail with GameObject highlighting.

```csharp
// CANON — artist-touched reference
[SerializeField] private TileManager tileManager;
void Awake() {
    if (tileManager == null) {
        Debug.LogError($"{name}: TileManager reference missing — wire it in the Inspector.", this);
        enabled = false;
        return;
    }
}
```

The `this` second argument makes clicking the console error highlight the exact misconfigured GameObject. This is non-optional on artist-facing references.

**Rationale:** Artists and writers cannot debug what they cannot see. Silent recovery (`FindObjectOfType` fallbacks, try-catch-swallow, "if null use default") hides the misconfiguration until it surfaces as a subtle bug weeks later. We are shipping to a 5-person team, not to end users. Loud is safe.

### Supporting rules (corollaries of the Laws)

- **S1 — Entities receive context; they never grab globals.** No `WeatherManager.Instance` mid-tick. See Section 5.2. (Corollary of decoupling intent.)
- **S2 — One concept, one place.** If "where is X defined?" has more than one answer, that is an architecture bug. Everything about the Narra Tree lives in `NarraTree.asset`. Everything about Apply Fertilizer lives in its SO + one small subclass.
- **S3 — RunManager owns the simulation tick, not the frame tick.** See Section 2. Cosmetic per-frame updates (billboarding, camera, tweens) stay in each component's own `Update()`. RunManager never touches them.
- **S4 — Choose one reference model per manager.** Core singletons (always exactly one) use `.Instance`. Per-instance / UI references use `[SerializeField]` + loud-fail. Never mix the two on the same field.
- **S5 — Code groups by feature; data and art live apart from code.** Each gameplay system is a flat feature folder directly under `_GAME/` (`_GAME/Tile/`, `_GAME/Dialogue/`, …) holding its scripts — no `_API/` or `Scripts/` sub-trees. All ScriptableObject assets live under the top-level `_SO/` (grouped by feature), all art under `_ART/`. The Project window becomes free documentation. *(Revised 2026-06-24 — supersedes the original "namespace mirrors folder" rule; see §4. `Habitales.*` namespaces are retained but no longer need to mirror folder names.)*

---

## 2. The Simulation Heartbeat (RunManager)

**Decision (locked):** RunManager owns the **simulation tick** — the ordered day-advance sequence. It does **not** own the frame tick.

**Why this is the right call for Habitales specifically:** the game has *no idle time*. Time only advances when the player commits an action. There is therefore no per-frame simulation to distribute across `Update()` methods. There is a **day-advance event** that systems react to in a deliberate order. That single fact decides the architecture: one system owns the ordered sequence; everyone else reacts.

### 2.1 The day-advance sequence (owned, explicit, ordered)

When a day passes, RunManager executes this sequence in this exact order. The order is itself an architectural artifact — it is the answer to "why did cascade run before zone-unlock?"

```
For each day in the committed action:
  1. ResourceManager.AdvanceOneDay()        // clock++, weather roll, worker recovery check
  2. TileManager.UpdateAllEntities()        // every entity's OnDailyUpdate (fire, trees, trash...)
  3. RunManager.RunCascade()                // neighbour diffusion, snapshot-then-apply — GLOBAL (all tiles), ONCE per day
  4. RunManager.EvaluateThresholds()        // tile health tier crossings → fire meaning-events (Law 2)
  5. EventManager.DrainQueue()              // hard-interrupt popups, if any qualified
  6. TileManager.RefreshAllVisuals()        // visual sync last, after all data settled
  7. RunManager.FireDayResolved(day)        // the single push everyone subscribes to (STUB hook)
```

**Invariants:**
- **Visuals refresh last (step 6).** Data fully settles before anything redraws. No mid-sequence visual reads.
- **Cascade is snapshot-then-apply** (preserve `ARCHIVE/` behavior): compute all new stats against the *current* snapshot, then write them all at once. No mid-iteration bleed. This is proven behavior — port it exactly.
- **`IsSimulationPaused`** gates the *start* of a new day. When an event popup is active, the next day does not begin. (This fixes the documented gap where the pause guarded completion but not entry.)
- **`AdvanceTimeStepped` remains the canonical real-action path** (port from `ARCHIVE/`): one day at a time, each gated on the day-night cycle reaching idle before the next begins.

### 2.1b Cascade & Thresholds (steps 3–4 detail)

**Cascade (step 3) — PORT, but VERIFY the source.** The method is **`RunManager.CascadeTileUpdates()`** (confirmed against source — name preserved through the `GameManager → RunManager` rename, lives in current `RunManager.cs`): **3 iterations** of 4-neighbour diffusion **across ALL tiles (global, not region-scoped)**, each iteration `LerpStats(current, neighbourAverage, 0.15f)` computed against a **snapshot** then applied all at once (no mid-iteration bleed). It also syncs the `Contaminated` overlay on `tile.tv` when contamination crosses 60 — **port that overlay sync too, it is easy to drop.**

> **Source-comment caveat:** the XML doc-comment above `CascadeTileUpdates()` in the live code says "across a region" — that comment is **stale and wrong**; the code operates on `GetAllTiles()` (global). Trust the code, not the comment. (Fixing that stale comment in the live file is a safe, non-behavioral hygiene edit.)

> **Frequency — intentional delta (NOT a pure port).** In `ARCHIVE`/live, cascade runs **once per committed action** (the code even comments *"Cascade … per-action, not per-day"*). The heartbeat **deliberately moves it to once per day** (step 3 inside the per-day loop). A 5-day action therefore cascades 5× under the new model vs 1× in ARCHIVE. This is a chosen behavior change — the porting subagent must **not** "preserve" the per-action frequency; it ports the *diffusion math* exactly and lets the heartbeat own the *cadence*. State this explicitly in the work-order so the agent does not flag it as a regression.

Still `// VERIFY:` tag the diffusion math in code: if iteration count, lerp factor, or neighbour set differ from this note when read in source, *source wins* and this note gets corrected.

**Thresholds (step 4) — PIN, do not invent.** The tier cutoffs already exist in `ARCHIVE/Tile.cs` and are canon — encode exactly, no new numbers:

```csharp
public enum Tier { Critical, Degraded, Thriving }
// health < 33  → Critical   |   health < 67 → Degraded   |   health >= 67 → Thriving
```

`EvaluateThresholds` is **new orchestration** (not a port): it must detect *crossings*, not current state, so it only fires on change. Store `lastTier` on the **runtime tile** (not the SO). Per day, per tile: compute current `Tier` from `CalculateHealth()`, compare to `lastTier`; if different, fire `OnTileTierChanged(tile, lastTier, current)` (Law 2 meaning-event, §6 HOOK) and update `lastTier`. First evaluation seeds `lastTier` without firing.

### 2.2 What RunManager owns vs. orchestrates

| RunManager OWNS | RunManager does NOT touch |
|---|---|
| The day-advance sequence + order | Billboarding (`EntityVisualizer.LateUpdate`) |
| `IsSimulationPaused` | Camera drag/zoom |
| The `OnDayResolved` push | LeanTween animations |
| Cascade orchestration | Per-component cosmetic `Update()` |
| Threshold evaluation | UI redraw timing (UI pulls via Law 1) |
| Run-start / run-end / game-over | |

### 2.3 RunManager public surface (getters per Law 1)

```csharp
namespace Habitales.Core {
  public class RunManager : MonoBehaviour {
    public static RunManager Instance { get; private set; }

    // --- Read-anywhere state (Law 1) ---
    public int    CurrentDay        { get; private set; }
    public int    CurrentYear       { get; private set; }
    public bool   IsSimulationPaused{ get; private set; }
    public bool   IsActionRunning   { get; private set; }
    public float  WorldHealth       { get; private set; }   // cached avg, recomputed post-cascade

    // --- Meaning-events (Law 2) — signatures only; see Section 6 STUB ---
    public event System.Action<int> OnDayResolved;          // arg: the day that just resolved
    public event System.Action      OnRunStarted;
    public event System.Action<RunEndData> OnRunEnded;

    // --- Orchestration entry points ---
    public void CommitAction(/* action + tiles */);          // begins the stepped day loop
    public void RequestPause();   public void ReleasePause(); // event system gates here
  }
}
```

> 🟡 **STATUS (2026-06-06): the live surface differs from this spec.** RunManager does **not** expose `CurrentDay`, `CurrentYear`, `IsSimulationPaused`, or `WorldHealth` — clock getters live on `ResourceManager` (`TotalDays`/`CurrentYear`), avg-health on `RegionManager` (`GetTotalAverageHealth`). The pause flag is `IsEventPaused` (set by `PauseForEvent()`/`ResumeFromEvent()`, the live names — not `RequestPause`/`ReleasePause`). The day loop is `HandleTimeAdvanced(int days)`, not `CommitAction`. Treat this code block as target intent, not current API.

---

## 3. Core Spine Specs

Each spec encodes the proven signatures from the gold-standard files, the exposed getters (Law 1), and what the system fires/consumes (Law 2, stubbed).

### 3.1 Tile (data POCO — CANON, preserve)

`Tile` is plain C# data in a dictionary, **not** a MonoBehaviour. Its GameObject lives in a *separate* parallel dictionary inside TileManager. This data/visual separation is canon and is exactly the modularity we want — do not collapse it.

Preserve from `ARCHIVE/Tile.cs`:
- `gridPosition`, `stats` (`TileStats`), `entity` (`TileEntity`), `issues`, `tv` (overlay list), `regionID`.
- `TileStats`: 8 floats; `soilComposite` = mean of the first 6; `CalculateHealth()` = `(soilComposite + vegetationCover + (100 - contamination)) / 3`, hard-capped at 33 when `contamination > 60`.
- Read accessors: `CalculateHealth()`, `GetSoilComposite()`, `GetVegetationCover()`, `GetContamination()`.

**Delta:** `isAnalyzed` / `issuesRevealed` stay default-true for the prototype (Examine actions dormant). No change this pass.

### 3.2 TileManager (CANON spine — preserve, minimal delta)

Preserve the proven structure from `ARCHIVE/TileManager.cs`:
- Twin dictionaries: `Dictionary<Vector2Int, Tile> tileCache` (data) and `Dictionary<Tile, GameObject> tileGameObjects` (visual). **This separation is canon.**
- Spawning: `SpawnTile`, `SpawnTileArea`, `SpawnTilesFromPositions`.
- Queries: `GetTile` (×2 overloads), `GetTilesInRegion`, `GetAllTiles`, `GetTilesInRadius`, `GetAdjacentTiles` (4-dir), `GetAdjacentTiles8Dir`, `HasTileAt`.
- Modification: `ApplyIssue`, `ModifyTileStats` (soilDelta distributes ÷6; veg/contam direct).
- Entity management: `SpawnEntity<T>`, `TransformEntity<T>`, `RemoveEntity`, `UpdateEntitiesInRegion`, `UpdateAllEntities`, `GetEntityVisualizer`.
- Visual: `InstantiateTileVisual`, `UpdateTileVisual`, `GridToWorldPosition`; `WorldMin`/`WorldMax` properties.

**Deltas:**
1. **Generic entity spawning changes** to accommodate the new SO system. `SpawnEntity<T>()` (type-param) is supplemented by `SpawnEntity(TileEntitySO def)` (data-driven). See Section 5.
2. **Add `RefreshAllVisuals()`** — bulk visual sync called once at step 6 of the heartbeat (cheaper and more correct than per-mutation `UpdateTileVisual` during cascade).
3. **Remove any `FindObjectOfType` fallbacks** in favor of `.Instance` + loud-fail (Law 3 / S4).

### 3.3 ResourceManager (the accessor-pattern exemplar — Law 1 canon)

This system is the model for Law 1. Its accessor list is the pattern every other manager imitates.

Preserve: clock (`AdvanceTime`, `AdvanceTimeStepped` coroutine), worker roster + states, fatigue formula, recovery check, research points, `WorkerFactory` (Filipino names, portrait pool).

Expose (getters, Law 1): `CurrentYear`, `TotalDays`, `TotalPeople`, `AvailablePeople`, `RecoveringPeopleCount`, `AllWorkers`, `AvailableWorkers`, `FatiguedWorkers`, `ResearchPoints`.

**Deltas:**
1. **Add `AdvanceOneDay()`** as the single-day primitive the heartbeat (step 1) calls — clock++, `RollWeather`, recovery check. `AdvanceTimeStepped` becomes a thin coroutine wrapper that calls it and waits on the cycle.
2. Meaning-events stubbed: `OnDayAdvanced(int)`, `OnWorkerRecovered(Worker)`, `OnWorkerBirthday(Worker)` (Section 6).

### 3.4 PlayerAction (data + tiny-subclass split)

**The right level of data-drivenness for this team:** the boring common case is pure data; the unique case is a tiny subclass.

- **`ActionSO` (data, author-facing):** ID, display name, description, icon, category (Examine/Intervene/Emergency/Cleanup), selection mode (FloodFill/Adjacent/NonAdjacent), `baseDays`, `minDays`, `minPeoplePerTile`, `fatigueMultiplierPerTile`, info-tooltip text, supplementary images, lore, optional `variantGroupName`. Replaces `ActionIconConfig` + the hardcoded per-subclass fields. **Authored entirely in the Inspector.**
- **`PlayerAction` (behavior, small subclass):** abstract base; `ExecuteOnTile(tile, ctx)` is 10–20 lines. References its `ActionSO` for all metadata.
- **`GenericSpawnAction`:** one concrete class that reads a spawn-target `TileEntitySO` from its SO — covers roughly half the actions ("spawn this entity, apply this stat change") with **zero new code per action**.

Preserve from `ARCHIVE`: efficiency formula `days = ceil(BaseDays / sqrt(peoplePerTile / MinPeoplePerTile))` clamped to `MinDays`; the coroutine `FinishAction` tile-distribution-across-days; preflight `CanExecute`.

**Delta:** `ActionManager.ExecuteAction()` entry is now gated by the pause flag (closes the documented interrupt-before-action gap).

> ✅ **STATUS (2026-06-06): DONE.** `ActionManager.ExecuteAction` checks `RunManager.Instance.IsEventPaused` at entry (ActionManager.cs:83–87) and blocks while an event popup is active. The flag is named `IsEventPaused` (not `IsSimulationPaused`); `EventManager.PauseForEvent()` sets it. ⬜ The data-driven half of this section — `ActionSO` + `GenericSpawnAction` — is **not** built: `ActionSO` is unwired scaffold and the six live actions are still hand-coded subclasses on `ActionIconConfig`.

### 3.5 Shared data types — `StatChange` / `StatCondition` (Phase 1b — define BEFORE the entity system)

These are consumed by the entity system (§5.1 daily effects, death, promotion) **and** `GenericSpawnAction` (§3.4). They ship in Phase 1b as independent data types and **must be defined here, not invented per-agent** — two agents inventing two shapes fractures the spine. Two *distinct named types*, never one overloaded struct:

```csharp
namespace Habitales.Core {
  // The 8 TileStats fields + the three derived reads. One shared enum so every
  // consumer addresses stats identically.
  public enum TargetStat {
    NutrientBalance, SoilOrganicMatter, SoilStructure, BiologicalActivity,
    WaterDynamics, ErosionResistance, VegetationCover, Contamination,
    SoilComposite   // read-only derived; valid in conditions, invalid as a StatChange target
  }

  public enum Comparator { LessThan, LessOrEqual, GreaterThan, GreaterOrEqual }

  // Outcome of a satisfied condition. TransformTo carries the target definition.
  public enum OutcomeKind { None, Remove, TransformTo }

  [System.Serializable]
  public struct StatChange {            // daily effects, spawn-action effects
    public TargetStat stat;
    public float      delta;            // applied via TileManager.ModifyTileStats semantics
  }

  [System.Serializable]
  public struct StatCondition {         // death conditions, promotion gates
    public TargetStat stat;
    public Comparator comparator;
    public float      threshold;
    public OutcomeKind outcome;         // None for a pure promotion gate
    public TileEntitySO transformTarget;// used only when outcome == TransformTo
  }
}
```

**Rules for subagents:**
- A `StatChange` targeting `SoilComposite` is invalid (it is derived, not stored) — `OnValidate` must reject it (Law 3).
- A `StatChange` applies **directly to the one named stat**, clamped 0–100 (`tile.stats.<field> = Clamp(field + delta)`). This matches the hand-coded entities' proven behavior (e.g. `TreeEntity` adds per-stat boosts: VC +2.0, BA +1.2, NB +0.3, …) — it does **NOT** route through `ModifyTileStats`'s `soilDelta` ÷6 distribution. *(Corrected against `ARCHIVE/TileEntity.cs`; the earlier ÷-distribution note was wrong — a single `StatChange` targets a single stat, not all six.)*
- `StatCondition.transformTarget` is required iff `outcome == TransformTo`; `OnValidate` flags the mismatch.

---

### 3.6 Zones — `ZoneManager` / `ZoneProfile` (renovate this pass)

Zones are **in-scope this pass** (not left in `ARCHIVE`). They renovate to honor the three Laws and the entityId migration.

Preserve from `ARCHIVE`: the generation flow (seed → flood-fill organic blob → `FillEnclosedHoles` → profile-scaled stats → `RollTheme` → `AssignIssues` → `PlaceBuildings` → `PlaceOrganicEntities` → forced overrides → grant workers → fire result). Preserve `ZoneProfile` as the **author-facing SO** (difficulty-scaling per-stat range fields, theme weights, density ranges, building caps — proven, stay).

**Deltas:**
1. **`forcedEntities` migrates from string entity-types to `TileEntitySO` references.** This is the intersection with §5 — `ForcedEntityPlacement` now carries a `TileEntitySO` (resolved via the same `entityId` registry §5.4 defines), not a string. The Zone agent and the entity agent **share that one registry**; schedule it before both.
2. **`aziCalloutKey` becomes a meaning-event seam** (§6 HOOK): zone-finished-generating fires `OnZoneGenerated(result)` and `OnZoneUnlocked(regionID)` rather than calling `DialogueManager` directly. The dialogue/story-weaver layer subscribes. Removes the direct ZoneManager → DialogueManager coupling.
3. **Expose Law-1 getters:** keep `GetTotalAverageHealth()`; add `GetZoneHealth(regionID)`, `UnlockedRegions` (read-only), `ZoneCount`.
4. **Zone unlock threshold** (avg health ≥ 80) stays canon; the unlock check moves into the heartbeat's threshold step (§2.1b) so it fires in deterministic order rather than ad-hoc.

**Edge case to flag:** generation reads the registry for forced entities — ensure it runs *after* the registry is populated (bootstrap order, §4 GameBootstrap).

---

## 4. Folder & namespace map (revised 2026-06-24)

**Canonical term: "Region" (not "Zone").** The in-game concept is a *Region*. C# classes rename `ZoneManager → RegionManager`, `ZoneProfile → RegionProfile`, `ZoneGenerationResult → RegionGenerationResult`, `ZoneTheme → RegionTheme` (Phase 2.5). `regionID` is already the field name on `Tile` — the canonical identifier. Inspector display text and `zoneXxx` SO fields become `regionXxx`.

> **Structure decision (2026-06-24 — supersedes the original `Scripts/<Domain>` plan).** The team kept the **feature-folder** layout instead of consolidating code under a parallel `Scripts/` tree. Code groups by feature directly under `_GAME/`; the old per-feature `_API/` subfolders and the `Scripts/` renovation tree were **dissolved**. **ScriptableObjects** moved to a top-level `_SO/` and **art** lives in `_ART/` — both siblings of `_GAME/`. `Habitales.*` namespaces are retained where they exist but no longer need to mirror folder names (S5 revised). The reorganization preserved every Unity GUID (each `.meta` moved with its file), so scene/prefab/asset references survived.

```
Assets/
├── _GAME/                     gameplay code — one flat feature folder per system
│   ├── Core/        RunManager, GameBootstrap, GameLog, IEntityEventSink, StatTypes   (ns Habitales.Core)
│   ├── Tile/        Tile, TileStats, TileManager, TileVisualizer, EntityVisualizer
│   ├── Entities/    TileEntitySO, GenericTileEntity, EntityRegistry, hooks (+ Behaviours/)  (ns Habitales.Entities)
│   ├── Action/      PlayerAction base, action subclasses, ActionManager, ActionSO, ActionBarUI (+ PlayerActions/)
│   ├── Zone/        RegionManager, RegionProfile, generation helpers  (folder name kept; classes are Region*)
│   ├── Dialogue/    DialogueManager, ConversationSO model, ChatApp (+ Editor/ for ConversationEditor)  (ns Habitales.Dialogue)
│   ├── Events/      EventManager, GameEventSO, EventContext, popup/camera handlers
│   ├── Weather/     WeatherManager, WeatherVFXController
│   ├── Results/     EndGame, Level-Up, RunEndCoordinator, progression/meta
│   ├── UI/          shared HUD/menu UI (+ Messaging/, Narrative/)  (ns Habitales.UI)
│   ├── Onboarding/  OnboardingDirector + coach-marks + juice (+ CoachMarks/, Juice/)  (ns Habitales.Onboarding)
│   ├── Utilities/   helpers
│   ├── Prototype/   prototype-only meta slice (RunSnapshot, etc.)
│   └── SO/          legacy holder — only orphaned archive assets remain; NOT a home for new SOs
├── _SO/                       ALL ScriptableObject assets, grouped by feature
│   ├── Entities/    entity defs + EntityRegistry + Fire/Village hooks
│   └── Dialogue/ (+ Stickers/) · Events/ · Regions/ (+ Debug/) · Action/ · Progression/
└── _ART/                      all art (characters, environment, tiles, UI, SFX)

ARCHIVE/   (repo root, OUTSIDE Assets/) — frozen pre-renovation scripts, behavioral reference
```

**New-content placement rule (the directive):**
- A new gameplay script → its feature folder `_GAME/<Feature>/` (create a new feature folder for a new system). No `_API/` or `Scripts/` nesting.
- Editor-only scripts (anything using `UnityEditor`) → `_GAME/<Feature>/Editor/`.
- Every new ScriptableObject asset → `_SO/<Feature>/`.
- Art → `_ART/`.
- **Log the new system in `HABITALES_SYSTEM_INVENTORY.md` immediately, with its add-date.**

**GameBootstrap:** one MonoBehaviour with `[DefaultExecutionOrder(-1000)]` that validates/initializes every core singleton in a known sequence. Ends initialization-order races. If a singleton's `Instance` is accessed before its `Awake`, log a loud error (Law 3).

---

## 5. Entity System Renovation

This is the load-bearing pipeline — artists fill it forever, so it must stay friction-free for them and rot-resistant for us. Two changes are designed **now**: a real entity-ID system (replacing string `entityType`), and a context object (replacing mid-tick singleton grabs, S1).

### 5.1 Real entity identity (replaces string `entityType`)

**Problem in `ARCHIVE`:** identity is string-typed and overloaded — `entityType = "CoverCropMature " + variant`, mixed with `is VillageEntity` type checks, and reused as VFX lookup keys. This is the thing most likely to rot as the pool grows.

**New model — `TileEntitySO` as the canonical identity + data:**

```csharp
namespace Habitales.Entities {
  [CreateAssetMenu(menuName = "Habitales/Entities/Entity Definition")]
  public class TileEntitySO : ScriptableObject {
    [Header("Identity")]
    public string entityId;              // stable machine ID, e.g. "narra_mature" — THE identity
    public string displayName;           // "Narra Tree"
    public EntityCategory category;      // Plant / Building / Hazard / Debris / CoverCrop (enum)

    [Header("Visuals")]
    public Sprite tileSprite;            // typed Sprite → inspector preview (Law 3)
    public string vfxKey;                // explicit VFX key, no longer derived from a string concat
    public float billboardScale = 1f;

    [Header("Lifecycle / Stage Chain")]
    public TileEntitySO nextStage;       // null = terminal stage
    public int   promoteAfterDays;       // 0 = no time-based promotion
    public StatCondition promoteWhen;    // optional gated promotion

    [Header("Daily Effects")]
    public List<StatChange> dailyEffects;// the pure-data common case

    [Header("Death")]
    public List<StatCondition> deathConditions; // condition → outcome (remove / transform-to)

    [Header("Custom Behaviour (optional)")]
    public EntityBehaviourHook behaviour;// null for pure-data entities; set for bespoke ones
  }
}
```

- **`entityId` is identity.** Type-checks (`is VillageEntity`) are replaced by category checks (`def.category == EntityCategory.Building`) or id checks where a specific entity matters.
- **One SO per stage**, chained via `nextStage`. `DeadTree` and `Stump` become **shared generic assets** reused across every species. Narra's lifecycle is `narra_seedling → narra_sapling → narra_mature → deadtree(shared)`. This is the composability win (S2).
- **Runtime is one generic `TileEntity`** that reads its `TileEntitySO` and delegates. It is no longer one C# class per entity kind.

### 5.2 The context object (S1 — entities never grab globals)

**Problem in `ARCHIVE`:** entities reach for `WeatherManager.Instance`, `EventManager.Instance`, `VFXManager.Instance` mid-tick. That coupling is what blocks modularity — a `FireEntity` should not know `WeatherManager` exists as a global.

**New model — entities receive a `TickContext`:**

```csharp
namespace Habitales.Entities {
  public readonly struct TickContext {
    public readonly TileManager Tiles;
    public readonly float FireBonusDamage;     // resolved by RunManager from WeatherManager
    public readonly float FireSpreadMultiplier;
    public readonly IEntityEventSink Events;   // meaning-event sink (STUB) — not a global grab
    // ...whatever the day's resolved environment needs
  }

  public abstract class TileEntity {
    public string  entityId;     // mirrors its SO
    public float   health = 100f;
    public abstract void OnDailyUpdate(Tile tile, in TickContext ctx);
  }
}
```

RunManager assembles the `TickContext` once per day (reading WeatherManager via Law 1) and passes it down through `UpdateAllEntities(in ctx)`. Entities read `ctx.FireBonusDamage` instead of `WeatherManager.Instance.GetFireBonusDamage()`. No entity references any singleton.

> **WeatherManager surface — confirm/renovate.** `TickContext` assumes WeatherManager exposes `FireBonusDamage` / `FireSpreadMultiplier` / `WorkSpeedMultiplier` as **Law-1 getters** (`{ get; private set; }` or computed). In `ARCHIVE` these are methods (`GetFireBonusDamage()` etc.). The WeatherManager renovation adds the getters; RunManager reads them once per day to build the context. If a value is only meaningful per-call, RunManager resolves it at context-assembly time. Subagent confirms the surface exists before wiring.

**Generic runtime vs. hook — how `nextStage` / `deathConditions` are evaluated.** The generic runtime `TileEntity` evaluates *pure-data* lifecycle itself: each day it applies `dailyEffects`, checks `deathConditions` (fire outcome on first satisfied), then checks promotion (`promoteAfterDays` reached AND `promoteWhen` satisfied → `TransformEntity` to `nextStage`). A `behaviour` hook, when present, runs **in addition** — the hook handles only the bespoke logic (Fire's spread, CoverCrop's variant table); the generic data-evaluation still runs unless the hook explicitly owns the stage chain. Orchestrator states per-hook in the work-order whether the hook *supplements* or *replaces* the generic evaluation (Fire replaces; Village supplements).

### 5.3 Behaviour hooks (the escape hatch) + the port map

Pure-data entities need no code. Bespoke ones get a tiny `EntityBehaviourHook` (a referenced ScriptableObject subclass with an overridable `OnDailyUpdate(tile, entity, in ctx)`), kept small enough that the half-programmer teammate writes one confidently.

**Port map (the proving work — verify each against `ARCHIVE/TileEntity.cs`):**

| Entity | Ports to | Notes |
|---|---|---|
| Tree (Mature) | **Pure data** | 7 daily stat boosts + death condition `soilComposite < 15 → DeadTree`. Clean. |
| Sapling | **Pure data** | Daily effects + NB consume + `promoteAfterDays` + death + `+10 SOM on death`. The on-death stat bonus is a death-outcome field. |
| Seedling | **Pure data** | Promote-after-days → Sapling, with veg bump on promote. |
| Stump | **Pure data** | +0.05 SOM/day, no removal. Shared generic asset. |
| DeadTree | **Pure data** | +0.3 SOM/day, remove after 30 days. Shared generic asset. |
| TrashBio | **Pure data** | +`isInspected` flag (keep as runtime field); day-7 BA hit then remove. |
| TrashNonBio | **Pure data** | Inert; `isInspected` flag. |
| Factory | **Pure data (stub effects)** | Daily logic still empty in `ARCHIVE`; port as no-op, leave effects for later. |
| **Village** | **Behaviour hook** | 1.5%/day kaingin: fires a meaning-event + spawns 4–7 fires in radius. Needs `ctx.Events` + spawn logic. |
| **Fire** | **Behaviour hook** | Spread ramp normalized by starting vegetation, MIN_SPREAD_DAYS gate, weather mult via `ctx`. Too bespoke for data. |
| **CoverCrop ×3** | **Behaviour hook** | Variant-switch (Legume/Grass/Phyto) with per-variant daily tables + Phyto's 30-day drain sapling stage. Variant logic needs code. |

**Edge cases to flag during the port (per your phase-end-check preference):**
- **`isInspected`** lived on the entity instance in `ARCHIVE`. In the SO model, definition data is shared/immutable, so per-instance runtime flags (inspected, daysExisting, startingVegetation, fireDuration) must live on the **runtime entity**, never on the SO. Subagent must not push mutable per-instance state into the shared asset.
- **CoverCrop's `entityType = "..." + variant`** string concat is the anti-pattern being removed. **LOCKED decision:** three SOs (`cover_crop_legume`, `cover_crop_grass`, `cover_crop_phyto`), each referencing the **same** `CoverCropBehaviourHook`; the variant field lives on the **SO**, not the runtime entity (S2 — one concept, one place; avoids per-instance SO pollution).
- **Fire's `RemoveEntity` mid-tick** while iterating all entities — preserve `ARCHIVE`'s safe-iteration behavior (the day-batch already snapshots the tile list); verify no collection-modified-during-enumeration regression.

### 5.4 Persistence contract (in-scope — `entityId` is the save key)

The `entityType`(string) → `entityId` rename **is a persistence contract**, so it is designed now, not retrofitted. **No existing saves matter** — there is no migration map and no backward compatibility requirement. The contract only has to be correct from this point forward.

> 🟡 **STATUS (2026-06-06): contract in place, save/load NOT implemented.** `entityId` is the stable key and the registry rehydrate path (`EntityRegistry.Get(id)` with loud-fail on miss) exists — so rules 1 & 3 below are honored *by construction*. But there is **no entity/tile JSON save or load anywhere in the codebase.** The only persistence that exists is `ProgressionPersistence` (meta-XP at `persistentDataPath/progression.json`), which does not touch tiles or entities. A prior handoff's "Phase 4d done" refers to the id-as-key *contract*, not a working entity save system. Rule 2 (persist per-instance runtime state) and the actual JSON layer remain ⬜ to build.

**The rules (all subagents touching save/load OR entities honor these identically):**
1. **`entityId` is the save key and is stable-forever / write-once.** Once an entity ships with `entityId = "narra_mature"`, that string is permanent. Renaming it silently orphans saved tiles. `OnValidate` enforces non-empty + project-unique `entityId` (Law 3).
2. **Save = `entityId` + per-instance runtime state.** JSON persists the id string plus the entity's mutable fields: `health`, `daysExisting`, `isInspected`, `startingVegetation`, `fireDuration`, CoverCrop `variant`, etc. — whatever the runtime instance carries.
3. **Load = registry lookup + rehydrate.** On load, the entity is reconstructed by looking up its `TileEntitySO` in an `entityId`-keyed registry, then restoring the saved runtime state onto the fresh runtime `TileEntity`. A missing `entityId` in the registry is a **loud load-time error** (Law 3), never a silent skip.
4. **Shared SOs hold zero per-instance state.** This is the §5.3 edge-case as a persistence rule: because `DeadTree`/`Stump` SOs are shared across every tile that uses them, no mutable field may live on the SO — it would be written once and read by every instance. Per-instance state lives only on the runtime entity and only in the save record.

> Build-order note: the registry that maps `entityId → TileEntitySO` (`EntityRegistry`) is the same registry Regions use to resolve `forcedEntities` (§3.5 / §3.6). One registry, three consumers (entities, regions, persistence) — schedule it once (Phase 1d), before all. **`EntityRegistry` is the SOLE registry going forward** — the prototype's `GeneratedPlantRegistry` retires with the cube system and is NOT consolidated into it.

---

## 6. Event Layer — STUB

> **The full event vocabulary is deferred to its own conversation thread.** This section creates the *seams* so subagents leave hooks ready-to-connect (your stated gamechanger), without committing the vocabulary prematurely. Law 2 governs everything placed here.

### 6.1 What to build now (the stub)

- **`IEntityEventSink`** — the interface entities receive in `TickContext` to report meaning-moments without knowing who listens. Stub with a handful of obvious signatures and a `Raise(EntityEvent e)` catch-all.
- **Named insertion points** in RunManager and the spine where meaning-events will attach. Mark each with a `// HOOK:` comment and an empty `event Action<...>` so consumers can already subscribe:

```
// HOOK: RunManager.OnDayResolved(int day)            — the universal push
// HOOK: RunManager.OnTileTierChanged(Tile, Tier old, Tier new)  — Critical/Degraded/Thriving crossings
// HOOK: RunManager.OnZoneUnlocked(int regionID)
// HOOK: TileManager.OnEntitySpawned(Tile, entityId)
// HOOK: TileManager.OnEntityDied(Tile, entityId, cause)
// HOOK: ResourceManager.OnWorkerRecovered(Worker) / OnWorkerBirthday(Worker)
```

> 🟡 **STATUS (2026-06-06): seams present; no real sink yet.** ✅ Built on RunManager: `OnDayResolved(int)`, `OnTileTierChanged(Tile, Tier, Tier)`, `OnRegionUnlockReady`, `OnRegionUnlocked(int)`. ✅ Now built: `TileManager.OnEntitySpawned(Tile, entityId)` / `OnEntityDied(Tile, entityId, cause)` (death-event suppressed on the `SpawnFromDef` replace pre-clear so in-place promotion is not reported as a death), and `ResourceManager.OnWorkerRecovered(Worker)` (fired per-worker). `OnWorkerBirthday(Worker)` is declared as a seam but not raised (birthday system dormant). The `IEntityEventSink` stub + `NullEntityEventSink` default exist and ride in `TickContext`. **A real sink is now wired:** `EventManagerEntitySink` (built once by RunManager, threaded into every per-day `TickContext`) translates the catch-all `Raise(kind=ScriptedEvent, cause=<id>)` into `EventManager.FireEventByID`. `VillageBehaviourHook` uses it for the kaingin event — **S1 closed for Village** (no more singleton grab). Still open: the sink's `EntitySpawned`/`EntityDied` are no-ops (TileManager fires its own at the mutation site), and nothing yet subscribes to the new TileManager/ResourceManager manager-events — that awaits the deferred vocabulary (§6.2).

### 6.2 The deferred question (do NOT resolve here)

Whether the registry is a **curated single-tier list** (only meaning-events) or a **two-tier list** (meaning-events + a documented fine-grained tier for systems that opt in) is an open design decision flagged for the dedicated thread. The user explicitly wants it to be "even a mix of both," which needs its own design pass. **Subagents must not invent the vocabulary.** They wire the stub seams and stop.

### 6.3 Why this is enough for now

The story weaver and the messaging app are the consumers that justify this layer. They subscribe to the ordered, meaning-level stream so the chat app can "evolve" with how the player plays — without coupling to combat-of-the-day internals. The stub guarantees those subscription points exist and are correctly ordered (events fire at heartbeat steps 4–7, after data settles). Designing *which* signals the weaver consumes is the next thread's job.

---

## 7. The Entity Art Tool — authoring surface

The entity pipeline is the hottest authoring path, so it gets a purpose-built surface. **The feel is specified here; the exact visual layout is a deliberate STUB** — left to whoever implements it, using ArtificeToolkit's attribute-based drawers. This section is the contract that surface must satisfy, not its pixels.

### 7.1 The three authoring audiences (Law 3 applied)

1. **Data authors** filling `TileEntitySO` forms — see structured sections, every field tooltipped, misconfiguration shouts via `OnValidate`.
2. **Artists** placing/previewing entities in-scene — `[ContextMenu]` actions, scene gizmos, batch apply; editor-time only.
3. **Reference-wirers** — loud-fail with `Debug.LogError(msg, this)` highlighting on every artist-touched reference.

### 7.2 The contract (what the surface must deliver)

- **One SO = one thing a human can point at** (S2). Everything about an entity lives in its asset.
- **Sectioned form** via ArtificeToolkit attributes: Identity · Visuals · Lifecycle · Daily Effects · Death · Custom Behaviour. Authors scan, not read.
- **Every field carries a tooltip** explaining what it does and what breaks if it is wrong.
- **`Sprite`-typed visual fields** so the Inspector renders real previews (Law 3 — see the consequence immediately).
- **`OnValidate()` shouts early**: missing sprite, empty daily-effects on a plant, orphan `nextStage`, `entityId` blank or duplicated → red Inspector warnings the moment of misconfiguration, never at runtime.
- **`[CreateAssetMenu]` submenus** organized `Habitales/Entities/...` so authors find templates fast.

### 7.3 The "Create Entity" flow (feel, not layout — STUB)

The intended feel: an author triggers "Create Entity" (an inspector button / editor action), is guided through the sectioned form with live validation, and ends with a ready-to-use asset — **without programmer friction and without touching C#** for the common case. A bespoke entity additionally drops in a behaviour hook (the only step that may need the half-programmer).

**Implementation note for whoever takes this:** the actual window/button layout, batch-placement gizmos, and preview arrangement are yours to design when you tackle it. The constraints above (sectioned, tooltipped, sprite-preview, loud `OnValidate`, ArtificeToolkit, one-concept-one-place) are the fixed contract. Everything else is open.

---

## 8. Diagnostics (build alongside the spine)

- **`GameLog` static class**, tagged categories (`GameLog.Action`, `GameLog.Dialogue`, `GameLog.Cascade`, `GameLog.ZoneGen`), each globally toggleable via a config SO. Replaces the fragmented per-manager `showDebugInfo` flags. Centralized = you mute cascade spam while debugging dialogue, and vice-versa.
- **In-scene `DebugOverlay`** (toggle key): current day/year, weather, available workers, world + per-zone health, active entity counts, last N meaning-events fired. You will use this every playtest.
- **Gizmos** where they make the invisible visible: `ZoneManager.OnDrawGizmos` for region boundaries + seed tiles.

---

## 9. Build Order & Delegation Notes (for the orchestrator)

Dependency-ordered. Items at the same depth with no shared files are **safe to parallelize**.

```
Phase 0 — Foundation (sequential, blocks everything)
  0a. Move current codebase → ARCHIVE/.  Establish feature folders under `_GAME/` + a top-level `_SO/` for assets (S5; revised 2026-06-24 — original plan was a `Scripts/<Domain>` tree with folder-mirrored namespaces, now superseded by §4).
  0b. GameBootstrap + GameLog + the three Laws encoded as a CONTRIBUTING note in-repo.

Phase 1 — Data POCOs & spine skeletons (parallel after 0)
  1a. Tile + TileStats          (port, CANON — §3.1)        ── independent
      ↳ add runtime `lastTier` field (§2.1b) — NOT on the SO
  1b. ActionSO + StatChange + StatCondition data types (§3.5) ── independent
  1c. TileEntitySO + EntityCategory + behaviour-hook base    ── independent
  1d. EntityRegistry (entityId → TileEntitySO lookup)        ── SHARED by §5.4 + §3.6; build once, before Phase 2.5 & 4

Phase 2 — Managers (depends on Phase 1 data types)
  2a. TileManager (port + twin-dict + RefreshAllVisuals)     depends 1a, 1c
  2b. ResourceManager (port + AdvanceOneDay + accessors)     depends on nothing in 1, parallel with 2a
  2c. Event layer STUB (IEntityEventSink + HOOK seams)       depends on nothing — can start early
  2d. WeatherManager (port + Law-1 getters §5.2)             depends on nothing — parallel

Phase 2.5 — Zones (depends on 2a, 1d)
  2e. ZoneManager + ZoneProfile renovation (§3.6)            forcedEntities → TileEntitySO via 1d registry
      ↳ unlock check relocates into heartbeat threshold step (3a)

Phase 3 — The heartbeat (depends on 2a, 2b, 2d)
  3a. RunManager day-advance sequence (§2.1), pause gating, threshold eval (§2.1b)
      ↳ wires the HOOK seams from 2c at steps 4–7; assembles TickContext from 2d
      ↳ RunCascade: VERIFY source method in current RunManager.cs before porting (§2.1b)

Phase 4 — Entity port (depends on 2a, 1c, 1d, 2c)
  4a. Runtime generic TileEntity + TickContext assembly + generic lifecycle eval (§5.2)
  4b. Pure-data ports: Tree, Sapling, Seedling, Stump, DeadTree, Trash×2, Factory   ── parallel
  4c. Behaviour-hook ports: Village, Fire, CoverCrop×3                              ── parallel after 4a
      ↳ each verified against ARCHIVE/TileEntity.cs, not invented
      ↳ orchestrator states per-hook: supplements vs replaces generic eval (§5.2)

Phase 4.5 — Persistence (depends on 4a, 1d)
  4d. JSON save/load on entityId + per-instance runtime state (§5.4)
      ↳ no migration map (no saves matter); loud load-time error on missing entityId

Phase 5 — Actions (depends on 3a, 4)
  5a. PlayerAction base + GenericSpawnAction + ActionManager pause-gate
  5b. Concrete action subclasses (small)                     ── parallel

Phase 6 — Authoring surface (depends on 1c; can overlap Phase 4)
  6a. TileEntitySO custom inspector contract (§7) — ArtificeToolkit, layout STUB
  6b. OnValidate validation rules (incl. §3.5 stat-type + §5.4 entityId uniqueness)
  6c. DebugOverlay
```

**Parallelization guidance for the orchestrator:**
- Hand each Sonnet a **single file or single tight cluster** plus Section 1 (Laws) verbatim plus the relevant `ARCHIVE/` source to diff against.
- A work-order that would touch RunManager + TileManager + an entity in one pass is too wide — split it (S2 / one-concern).
- **STUB means stub.** Village/Fire/CoverCrop subagents implement behaviour; the event-vocabulary and the art-tool layout subagents must stop at the seam.

---

## 10. Phase-end check (run this after each phase)

Per the standing preference, after each phase compare implementation against this document and report:

1. **Deviations** — anything that diverged from the Laws or the spec, and why.
2. **Edge-case risks** — especially: per-instance mutable state leaking into shared SOs (§5.3); collection-modified-during-enumeration in entity ticks; cascade snapshot-then-apply integrity; pause gating both *entry* and *completion*.
3. **Ambiguous behavior** — anything the document under-specified that the subagent had to guess. These become the next clarification round.

---

*This document is the contract. `ARCHIVE/` is the behavioral reference. The Laws are the invariant. Everything marked STUB is deferred on purpose — do not fill it without a decision from the user.*