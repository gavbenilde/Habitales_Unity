# Habitales — Master Architecture Document

**Purpose:** This is the single source of truth for the Habitales architecture renovation. It is written to be read top-to-bottom by an Opus orchestrator, which will split it into work-orders for Sonnet subagents. Humans skim it; agents parse it; it lives in the repo.

**Scope of this document:** The **core spine** (RunManager, Tile, TileManager, ResourceManager, PlayerAction), the **entity system renovation** (new `TileEntitySO` + entity-ID system, with the existing hand-coded entities ported onto it), the **event layer as a stub** (insertion points marked, vocabulary deferred to its own thread), and the **Entity Art Tool** authoring surface (feel specified, exact visual layout deferred).

**Framing — Renovate, don't replace.** The existing systems work and have proven feel. They are the behavioral reference, not throwaway code. The first build step is to move all current scripts into `ARCHIVE/`. Subagents diff against `ARCHIVE/` to preserve behavior; this document specifies the *deltas* — what changes, and why.

**Assumed dependencies:** Unity (C#, URP-compatible), LeanTween, JSON persistence, **ArtificeToolkit** (free; the assumed attribute-based custom-inspector layer for all authoring surfaces).

---

## 0. How to use this document (orchestrator note)

1. Read Sections 1–2 fully before splitting work. The Laws and the Heartbeat are global invariants every subagent must hold.
2. Section 7 (Build Order) gives the dependency graph and what is safely parallel. Split along those lines.
3. Every subagent work-order must carry Section 1 (the Laws) verbatim in its context. The Laws are what keep parallel work coherent.
4. Where this document says **STUB**, the subagent creates the seam and the signature but not the full implementation. Do not let a subagent "helpfully" fill in a stub that is deliberately deferred.
5. Where this document says **PORT**, the subagent reproduces behavior from `ARCHIVE/`, verified by reading the archived source, not by inventing from the prose description.

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
- **S5 — Namespace and folder mirror each other.** `namespace Habitales.Core` lives in `Scripts/Core/`, etc. The Project window becomes free documentation.

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
  3. RunManager.RunCascade()                // neighbour diffusion, snapshot-then-apply
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

**Delta:** `ActionManager.ExecuteAction()` entry is now gated by `RunManager.IsSimulationPaused` (closes the documented interrupt-before-action gap).

---

## 4. (reserved — folder & namespace map)

```
Scripts/
├── Core/        RunManager, GameBootstrap, global types, GameLog
├── Tiles/       Tile, TileStats, TileManager, TileVisualizer
├── Entities/    TileEntity base, runtime entity, TileEntitySO, behaviour hooks
├── Actions/     PlayerAction base, GenericSpawnAction, subclasses, ActionSO
├── Zones/       ZoneManager, ZoneProfile, generation helpers
├── Dialogue/    (already namespaced)
├── UI/          all MonoBehaviour UI
├── Data/        pure ScriptableObject definitions
├── Editor/      custom inspectors, art tools (editor-only)
└── Utility/     camera, VFX, helpers
ARCHIVE/         the entire current codebase, untouched, behavioral reference
```

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
- **CoverCrop's `entityType = "..." + variant`** string concat is exactly the anti-pattern being removed — variant becomes a runtime field + the SO carries the variant-agnostic identity, or three SOs share a behaviour hook. Orchestrator: pick one and state it in the work-order.
- **Fire's `RemoveEntity` mid-tick** while iterating all entities — preserve `ARCHIVE`'s safe-iteration behavior (the day-batch already snapshots the tile list); verify no collection-modified-during-enumeration regression.

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
  0a. Move current codebase → ARCHIVE/.  Establish Scripts/ folders + namespaces (S5).
  0b. GameBootstrap + GameLog + the three Laws encoded as a CONTRIBUTING note in-repo.

Phase 1 — Data POCOs & spine skeletons (parallel after 0)
  1a. Tile + TileStats          (port, CANON — §3.1)        ── independent
  1b. ActionSO + StatChange + StatCondition data types       ── independent
  1c. TileEntitySO + EntityCategory + behaviour-hook base    ── independent

Phase 2 — Managers (depends on Phase 1 data types)
  2a. TileManager (port + twin-dict + RefreshAllVisuals)     depends 1a, 1c
  2b. ResourceManager (port + AdvanceOneDay + accessors)     depends on nothing in 1, parallel with 2a
  2c. Event layer STUB (IEntityEventSink + HOOK seams)       depends on nothing — can start early

Phase 3 — The heartbeat (depends on 2a, 2b)
  3a. RunManager day-advance sequence (§2.1), pause gating, threshold eval
      ↳ wires the HOOK seams from 2c at steps 4–7

Phase 4 — Entity port (depends on 2a, 1c, 2c)
  4a. Runtime generic TileEntity + TickContext assembly
  4b. Pure-data ports: Tree, Sapling, Seedling, Stump, DeadTree, Trash×2, Factory   ── parallel
  4c. Behaviour-hook ports: Village, Fire, CoverCrop×3                              ── parallel after 4a
      ↳ each verified against ARCHIVE/TileEntity.cs, not invented

Phase 5 — Actions (depends on 3a, 4)
  5a. PlayerAction base + GenericSpawnAction + ActionManager pause-gate
  5b. Concrete action subclasses (small)                     ── parallel

Phase 6 — Authoring surface (depends on 1c; can overlap Phase 4)
  6a. TileEntitySO custom inspector contract (§7) — ArtificeToolkit, layout STUB
  6b. OnValidate validation rules
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
