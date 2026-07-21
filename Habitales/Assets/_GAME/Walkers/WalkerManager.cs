using System.Collections.Generic;
using UnityEngine;

// Central coordinator for the Walker system. Owns the claim map (tile occupancy — not on
// Tile.cs), spawns WorkerWalkers 1:1 with ResourceManager.AllWorkers, runs the animal
// deficit/surplus population rule, and fans out the two singleton events every walker needs
// to react to in lockstep: ActionManager's per-day work batches and RunManager's game-over.
// Walkers never touch each other's claims directly — every claim mutation goes through this
// manager's public API.
// Execution order -120 slots between RegionManager (-150) and RunManager (-100) so this
// manager's Start() subscribes to RegionManager.OnRegionGenerated BEFORE RunManager's
// Start() generates the initial region and fires that event. At default order (0) the
// subscription lands after the one-shot event has already fired and no workers ever spawn.
// Safe because all Awake()s complete before any Start(), so every .Instance guard below is set.
[DefaultExecutionOrder(-120)]
[DisallowMultipleComponent]
public class WalkerManager : MonoBehaviour
{
    /// <summary>Singleton — exactly one WalkerManager per scene.</summary>
    public static WalkerManager Instance { get; private set; }

    [Header("Species Profiles")]
    [Tooltip("The Worker species profile. WorkerWalkers spawn 1:1 with ResourceManager.AllWorkers at run start.")]
    [SerializeField] private WalkerProfileSO workerProfile;
    [Tooltip("Animal species profiles (Carabao, Warty Pig, ...). Each independently targets " +
             "ThrivingTileCount / tilesPerAnimal — populations are not split across species.")]
    [SerializeField] private List<WalkerProfileSO> animalProfiles = new List<WalkerProfileSO>();

    [Header("Manager Knobs")]
    [Tooltip("Thriving-tile-count ÷ this = each animal species' population target. " +
             "Re-evaluated only on RunManager.OnTileTierChanged, plus once at init.")]
    [SerializeField] private int tilesPerAnimal = 10;
    [Tooltip("Constant world Y every walker sits at — tune to sit level with TileEntities.")]
    [SerializeField] private float walkerY = 0f;
    [Tooltip("Duration of the card-flip spawn / card-fold despawn tween, in seconds.")]
    [SerializeField] private float lifecycleTweenDuration = 0.4f;

    /// <summary>Every Walker reads this for its constant ground height.</summary>
    public float WalkerY => walkerY;

    // ── The claim map (lives here — Tile.cs is untouched) ──────────────────────────────────
    // One walker per tile is a roaming policy (Walker.PickRoamDestination checks IsClaimEmpty),
    // not an enforced invariant here — a worker's Working state claims freely into shared
    // tiles via ClaimTile.
    private readonly Dictionary<Tile, List<Walker>> claims = new Dictionary<Tile, List<Walker>>();

    private readonly List<WorkerWalker> workerWalkers = new List<WorkerWalker>();
    // Rosters are per-profile so the deficit rule can grow/shrink each species independently
    // without one species' count leaking into another's target math.
    private readonly Dictionary<WalkerProfileSO, List<AnimalWalker>> animalRosters = new Dictionary<WalkerProfileSO, List<AnimalWalker>>();

    private bool hasSpawnedInitialWorkers;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // Core singletons are read via .Instance rather than mirrored into a SerializeField —
        // this catches a scene missing one of them immediately with a clear error.
        if (TileManager.Instance == null) { Fail("TileManager"); return; }
        if (ResourceManager.Instance == null) { Fail("ResourceManager"); return; }
        if (ActionManager.Instance == null) { Fail("ActionManager"); return; }
        if (RunManager.Instance == null) { Fail("RunManager"); return; }
        if (RegionManager.Instance == null) { Fail("RegionManager (needed to know when tiles first exist)"); return; }
        if (workerProfile == null)
        {
            Debug.LogError("WalkerManager: Worker WalkerProfileSO is not assigned — wire it in the Inspector.", this);
            enabled = false;
            return;
        }

        ActionManager.Instance.OnActionDayStarted += HandleActionDayStarted;
        RunManager.Instance.OnGameOverTriggered   += HandleGameOverTriggered;
        RunManager.Instance.OnTileTierChanged     += HandleTileTierChanged;

        // Tiles don't exist until the first region generates — RegionManager.OnRegionGenerated
        // signals "tiles now exist", so hook init off it instead of guessing MonoBehaviour
        // Start() ordering against RegionManager.
        RegionManager.Instance.OnRegionGenerated += HandleFirstRegionGenerated;
    }

    void OnDestroy()
    {
        if (ActionManager.Instance != null) ActionManager.Instance.OnActionDayStarted -= HandleActionDayStarted;
        if (RunManager.Instance != null)
        {
            RunManager.Instance.OnGameOverTriggered -= HandleGameOverTriggered;
            RunManager.Instance.OnTileTierChanged   -= HandleTileTierChanged;
        }
        if (RegionManager.Instance != null) RegionManager.Instance.OnRegionGenerated -= HandleFirstRegionGenerated;
    }

    private void Fail(string missing)
    {
        Debug.LogError($"WalkerManager requires a {missing} in the scene!", this);
        enabled = false;
    }

    // ── Run-start wiring ─────────────────────────────────────────────────────────────────────

    private void HandleFirstRegionGenerated(RegionGenerationResult _)
    {
        if (hasSpawnedInitialWorkers) return; // later region unlocks don't re-trigger this
        hasSpawnedInitialWorkers = true;

        SpawnInitialWorkers();
        EvaluateAnimalPopulation(); // once at init

        RegionManager.Instance.OnRegionGenerated -= HandleFirstRegionGenerated;
    }

    private void SpawnInitialWorkers()
    {
        List<Tile> allTiles = TileManager.Instance.GetAllTiles();
        if (allTiles.Count == 0)
        {
            Debug.LogError("WalkerManager: no tiles exist after the first region generated — cannot place workers.", this);
            return;
        }

        foreach (Worker w in ResourceManager.Instance.AllWorkers)
            SpawnWorkerWalker(w, allTiles[Random.Range(0, allTiles.Count)]);
    }

    private void SpawnWorkerWalker(Worker worker, Tile startTile)
    {
        if (workerProfile.prefab == null)
        {
            Debug.LogError("WalkerManager: Worker WalkerProfileSO has no prefab assigned.", this);
            return;
        }

        GameObject go = Instantiate(workerProfile.prefab, transform);
        WorkerWalker ww = go.GetComponent<WorkerWalker>();
        if (ww == null)
        {
            Debug.LogError($"WalkerManager: Worker prefab '{workerProfile.prefab.name}' has no WorkerWalker component.", go);
            Destroy(go);
            return;
        }

        ww.Bind(worker);
        ww.Initialize(workerProfile, startTile);
        workerWalkers.Add(ww);
    }

    // ── Fan-out to walkers ──────────────────────────────────────────────────────────────────

    private void HandleActionDayStarted(IReadOnlyList<Tile> batch)
    {
        for (int i = 0; i < workerWalkers.Count; i++)
            workerWalkers[i]?.NotifyActionDayStarted(batch);
    }

    private void HandleGameOverTriggered()
    {
        for (int i = 0; i < workerWalkers.Count; i++)
            workerWalkers[i]?.NotifyGameOver();

        // Nothing new starts after game-over — that includes the animal population rule, so
        // stop reacting to further tier crossings during any post-game-over playout.
        RunManager.Instance.OnTileTierChanged -= HandleTileTierChanged;
    }

    // ── Claim map API (the only place tile occupancy is tracked) ───────────────────────────────

    public void ClaimTile(Walker walker, Tile tile)
    {
        if (walker == null || tile == null) return;
        if (!claims.TryGetValue(tile, out List<Walker> list))
        {
            list = new List<Walker>();
            claims[tile] = list;
        }
        if (!list.Contains(walker)) list.Add(walker);
    }

    public void ReleaseClaim(Walker walker, Tile tile)
    {
        if (walker == null || tile == null) return;
        if (!claims.TryGetValue(tile, out List<Walker> list)) return;
        list.Remove(walker);
        if (list.Count == 0) claims.Remove(tile);
    }

    /// <summary>True if no walker currently claims this tile. Roaming's empty-tile policy reads
    /// this; a worker entering the Working state does not.</summary>
    public bool IsClaimEmpty(Tile tile) => !claims.TryGetValue(tile, out List<Walker> list) || list.Count == 0;

    /// <summary>Tiles claimed by exactly one WorkerWalker — the candidate set for animal-follow targeting.</summary>
    public IEnumerable<Tile> TilesWithExactlyOneWorkerClaim()
    {
        foreach (KeyValuePair<Tile, List<Walker>> kvp in claims)
            if (kvp.Value.Count == 1 && kvp.Value[0] is WorkerWalker)
                yield return kvp.Key;
    }

    // ── Animal population — deficit rule + spawn/despawn lifecycle ─────────────────────────────

    private void HandleTileTierChanged(Tile tile, Tier oldTier, Tier newTier) => EvaluateAnimalPopulation();

    /// <summary>
    /// Count of tiles RunManager currently considers Thriving. Reads Tile.lastTier rather than
    /// calling into RunManager directly — that would require adding a new public getter to a
    /// manager this system must not modify, and Tile.lastTier already holds the same per-tile
    /// tier RunManager maintains as canon.
    /// </summary>
    private int CountThrivingTiles()
    {
        int count = 0;
        foreach (Tile t in TileManager.Instance.GetAllTiles())
            if (t.lastTier == Tier.Thriving) count++;
        return count;
    }

    private void EvaluateAnimalPopulation()
    {
        if (animalProfiles == null || animalProfiles.Count == 0) return;
        int thriving = CountThrivingTiles();

        foreach (WalkerProfileSO profile in animalProfiles)
        {
            if (profile == null) continue;
            if (!animalRosters.TryGetValue(profile, out List<AnimalWalker> roster))
            {
                roster = new List<AnimalWalker>();
                animalRosters[profile] = roster;
            }

            // Target = thriving-tile-count ÷ tilesPerAnimal, with a fractional-remainder chance
            // roll — e.g. 25 thriving / 10 = target 2 with a 50% chance of 3.
            float rawTarget = (float)thriving / tilesPerAnimal;
            int floorTarget = Mathf.FloorToInt(rawTarget);
            float remainder = rawTarget - floorTarget;
            int target = floorTarget + (Random.value < remainder ? 1 : 0);

            if (roster.Count < target) SpawnAnimal(profile, roster);
            else if (roster.Count > target) DespawnAnimal(roster);
        }
    }

    private void SpawnAnimal(WalkerProfileSO profile, List<AnimalWalker> roster)
    {
        if (profile.prefab == null)
        {
            Debug.LogError($"WalkerManager: WalkerProfileSO '{profile.name}' has no prefab assigned.", this);
            return;
        }

        List<Tile> thrivingTiles = new List<Tile>();
        foreach (Tile t in TileManager.Instance.GetAllTiles())
            if (t.lastTier == Tier.Thriving) thrivingTiles.Add(t);
        if (thrivingTiles.Count == 0) return; // nowhere to place one yet — try again on the next crossing

        Tile spawnTile = thrivingTiles[Random.Range(0, thrivingTiles.Count)];

        GameObject go = Instantiate(profile.prefab, transform);
        AnimalWalker aw = go.GetComponent<AnimalWalker>();
        if (aw == null)
        {
            Debug.LogError($"WalkerManager: Animal prefab '{profile.prefab.name}' has no AnimalWalker component.", go);
            Destroy(go);
            return;
        }

        aw.Initialize(profile, spawnTile);
        roster.Add(aw);

        // Card-flip spawn: x-rotation -180 -> 0 (reverse of the despawn fold below).
        LeanTween.cancel(go);
        Vector3 e = go.transform.eulerAngles;
        go.transform.eulerAngles = new Vector3(-180f, e.y, e.z);
        LeanTween.rotateX(go, 0f, lifecycleTweenDuration).setEase(LeanTweenType.easeOutQuad);
    }

    private void DespawnAnimal(List<AnimalWalker> roster)
    {
        if (roster.Count == 0) return;
        AnimalWalker victim = roster[Random.Range(0, roster.Count)];

        // The animal leaves the data at the start of the despawn tween — removed from the
        // roster and its claim immediately. It stays visible for the rest of the tween but is
        // already gone to every system, so it can't block its own tile or suppress its own
        // replacement.
        roster.Remove(victim);
        ReleaseClaim(victim, victim.CurrentTile);
        victim.enabled = false; // freeze roaming logic while the despawn tween plays

        GameObject go = victim.gameObject;

        // Card-fold despawn: x-rotation 0 -> -180, then Destroy.
        LeanTween.cancel(go);
        LeanTween.rotateX(go, -180f, lifecycleTweenDuration)
            .setEase(LeanTweenType.easeInQuad)
            .setOnComplete(() => Destroy(go));
    }
}
