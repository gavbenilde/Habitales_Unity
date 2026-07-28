using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Habitales.Entities;

/// <summary>
/// Manages region health, generation, and metadata.
/// Replaces HardCode zone spawning with a flexible, profile-driven system.
/// Subscribe to OnRegionGenerated to react to new regions (Azi callouts, camera pan, UI, etc.)
/// </summary>
[DefaultExecutionOrder(-150)] // manager — after core services, before the orchestrator (arch §4 init order)
public class RegionManager : MonoBehaviour
{
    public static RegionManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private TileManager tileManager;
    [SerializeField] private ResourceManager resourceManager;

    [Header("Zone 1 (Initial Zone)")]
    [Tooltip("Profile used exclusively for the first zone. If null, falls back to the random pool.")]
    [UnityEngine.Serialization.FormerlySerializedAs("zone1Profile")]
    [SerializeField] private RegionProfile zone1Profile;
    [Tooltip("Per-tile stagger (seconds) for the Zone-1 loading-screen reveal animation. Larger = more watchable/slower.")]
    [SerializeField] private float zone1RevealStagger = 0.08f;
    [Tooltip("Per-tile stagger (seconds) for every SUBSEQUENT region's unlock reveal (not Zone 1). Larger = slower.")]
    [SerializeField] private float regionRevealStagger = 0.02f;
    [Tooltip("Duration of each tile's zero→authored-scale pop during a region reveal. Purely presentational.")]
    [SerializeField] private float tileRevealPopDuration = 0.27f;
    [Tooltip("Ease-out-back overshoot amount — how far past the tile's final scale it bounces before " +
             "settling. Keep low for a single clean overshoot-and-settle with no wobble. Mirrors " +
             "TileManager's entitySpawnPopOvershoot so tiles and their entity children read alike.")]
    [SerializeField] private float tileRevealPopOvershoot = 1f;

    [Header("Zone Profiles")]
    [Tooltip("Profiles used in order as regions unlock. Index 0 = Region 2, Index 1 = Region 3, etc.")]
    [SerializeField] private List<RegionProfile> defaultProfiles = new List<RegionProfile>();
    [Tooltip("Used when defaultProfiles runs out. Should be a mid-to-late difficulty profile.")]
    [SerializeField] private RegionProfile fallbackProfile;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;

    [Header("Region Progress History")]
    [Tooltip("Max daily health samples kept per region in the FIFO progress-history queue. Default " +
             "360 covers a full calendar year — comfortably longer than the 100-day run — so the " +
             "history never truncates mid-run under normal play.")]
    [SerializeField] private int regionHistoryCap = 360;

    // Starts at 2 — Zone 1 is always spawned by GameManager.SpawnInitialZone()
    private int nextRegionID = 2;
    public int NextRegionID => nextRegionID;


    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
    };

    /// <summary>
    /// Fired after every successful region generation.
    /// Subscribers: DialogueManager (Azi), camera controller, UI notifications.
    /// </summary>
    public event System.Action<RegionGenerationResult> OnRegionGenerated;

    /// <summary>Fires when the Zone-1 (initial region) reveal animation finishes. Onboarding phase 1 advances on this.</summary>
    public event System.Action OnInitialRegionRevealed;

    // =====================================================================
    // LIFECYCLE
    // =====================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (tileManager == null)
            tileManager = TileManager.Instance;
        if (tileManager == null)
            Debug.LogError("RegionManager requires TileManager in scene!");

        if (resourceManager == null)
            resourceManager = ResourceManager.Instance;
    }

    // Per-region SHORT rolling window (last TrendWindowDays + 1 daily average-health samples)
    // used ONLY to derive the trend arrow's smoothed per-day change — NOT a run-progress record.
    // It holds just enough days for the arrow and rolls old samples off; the future per-region
    // progress history is a separate concern. Averaging the daily deltas over the window
    // telescopes to (newest − oldest) / span, so the queue is all we need. Captured once per
    // resolved day; UI reads GetRegionHealthTrend (Law 1).
    private readonly Dictionary<int, Queue<float>> _regionTrendWindow = new Dictionary<int, Queue<float>>();
    private readonly Dictionary<int, float>        _regionHealthTrend = new Dictionary<int, float>();

    // ---------------------------------------------------------------------
    // PROGRESS RECORD (added 2026-07-07) — distinct from the trend-only short window above.
    // Per-region FIFO history of daily average health, capped at regionHistoryCap samples.
    // This is a simple run-progress record (how has this region moved over the whole run),
    // NOT the trend arrow's smoothing signal — do not reuse or merge with _regionTrendWindow.
    // Pushed once per resolved day in the same HandleDayResolved handler. No UI/sparkline yet;
    // read via GetRegionHealthHistory / GetRegionProgress (Law 1).
    // ---------------------------------------------------------------------
    private readonly Dictionary<int, Queue<float>> _regionHealthHistory = new Dictionary<int, Queue<float>>();

    private void Start()
    {
        // RunManager (-100) initialises after RegionManager (-150), so subscribe in Start
        // (not Awake/OnEnable) when its Instance is guaranteed set.
        if (RunManager.Instance != null)
            RunManager.Instance.OnDayResolved += HandleDayResolved;
    }

    private void OnDestroy()
    {
        if (RunManager.Instance != null)
            RunManager.Instance.OnDayResolved -= HandleDayResolved;
    }

    /// <summary>
    /// Snapshots each region's average health once per resolved day into a rolling window
    /// (RunManager.TrendWindowDays) and records the smoothed per-day change across it.
    /// Single pass over all tiles (no per-region rescans, no GetRegionHealth debug spam)
    /// — mirrors GetRegionHealth's mean-of-CalculateHealth.
    /// </summary>
    private void HandleDayResolved(int day)
    {
        if (tileManager == null) return;

        var sum   = new Dictionary<int, float>();
        var count = new Dictionary<int, int>();

        foreach (Tile t in tileManager.GetAllTiles())
        {
            if (t == null) continue;
            int id = t.regionID;
            sum.TryGetValue(id, out float s);
            count.TryGetValue(id, out int c);
            sum[id]   = s + t.CalculateHealth();
            count[id] = c + 1;
        }

        foreach (var kv in sum)
        {
            int   id      = kv.Key;
            float current = kv.Value / count[id];

            if (!_regionTrendWindow.TryGetValue(id, out Queue<float> window))
                _regionTrendWindow[id] = window = new Queue<float>();

            window.Enqueue(current);
            // Window + 1 samples span exactly TrendWindowDays daily deltas.
            while (window.Count > RunManager.TrendWindowDays + 1)
                window.Dequeue();

            // Average daily change over the window == (newest − oldest) / span.
            int span = window.Count - 1;
            _regionHealthTrend[id] = span > 0 ? (current - window.Peek()) / span : 0f;

            // Progress record (2026-07-07) — separate FIFO queue, capped at regionHistoryCap.
            // Same "current" sample as above, just recorded into the long-lived history instead
            // of the short trend window.
            if (!_regionHealthHistory.TryGetValue(id, out Queue<float> history))
                _regionHealthHistory[id] = history = new Queue<float>();

            history.Enqueue(current);
            while (history.Count > regionHistoryCap)
                history.Dequeue();
        }
    }

    // =====================================================================
    // PUBLIC API
    // =====================================================================

    /// <summary>
    /// Generates a new region adjacent to the triggering region.
    /// Pass overrideProfile from an Event to force specific stats, entities, or themes.
    /// Returns a RegionGenerationResult for subscribers to read from.
    /// </summary>
    public RegionGenerationResult GenerateNewRegion(int triggeringRegionID, RegionProfile overrideProfile = null)
    {
        if (tileManager == null)
        {
            Debug.LogError("RegionManager: TileManager missing!");
            return null;
        }

        RegionProfile profile = overrideProfile ?? GetProfileForNextRegion();
        if (profile == null)
        {
            Debug.LogError("RegionManager: No RegionProfile available! Assign defaultProfiles or a fallbackProfile.");
            return null;
        }

        int regionID = nextRegionID++;

        if (showDebugInfo)
            Debug.Log($"RegionManager: Generating Region {regionID} (triggered by Region {triggeringRegionID})...");

        // Step 1: Seed tile
        Vector2Int? seed = FindSeedTile();
        if (seed == null)
        {
            Debug.LogError($"RegionManager: No valid seed tile found adjacent to Region {triggeringRegionID}.");
            nextRegionID--; // Roll back — generation failed
            return null;
        }

        // Step 2: Flood-fill shape
        int targetSize = Random.Range(profile.sizeRange.x, profile.sizeRange.y + 1);
        List<Vector2Int> positions = FloodFillShape(seed.Value, targetSize, profile.flowFalloff, profile.enclosureBonus);

        if (positions.Count == 0)
        {
            Debug.LogError("RegionManager: Flood fill produced no positions!");
            nextRegionID--;
            return null;
        }

        // Step 3: Spawn tiles with profile-scaled stats
        List<Tile> regionTiles = SpawnRegionTiles(positions, regionID, profile);

        // Step 4: Resolve theme
        RegionTheme dominantTheme = profile.forceTheme ? profile.forcedTheme : RollTheme(profile);

        // Step 5: Issue assignment
        int issuesAssigned = AssignIssues(regionTiles, dominantTheme, profile);

        // Step 6: Spawn table (trees, trash, villages, factories — all profile-authored)
        PlaceSpawnTableEntities(regionTiles, profile);

        // Step 7: Forced entity overrides (event-driven regions)
        if (profile.forceSpecificEntities)
            ApplyForcedEntities(seed.Value, profile);

        AnimateRegionReveal(regionTiles, regionRevealStagger);

        // Step 8: Worker reward
        if (resourceManager != null && profile.workerReward > 0)
            resourceManager.IncreaseTotalPeople(profile.workerReward);

        // Build result
        float avgHealth = regionTiles.Average(t => t.CalculateHealth());
        float contamCoverage = (float)regionTiles.Count(t => t.stats.contamination > 60f) / regionTiles.Count;
        Vector2Int center = CalculateCenter(positions);

        RegionGenerationResult result = new RegionGenerationResult
        {
            regionID              = regionID,
            tileCount             = regionTiles.Count,
            dominantTheme         = dominantTheme,
            averageStartingHealth = avgHealth,
            issuesAssigned        = issuesAssigned,
            contaminationCoverage = contamCoverage,
            approximateCenter     = center,
            wasTriggeredByEvent   = overrideProfile != null,
            sourceEventID         = null
        };

        PopulateNotableFindings(result, dominantTheme);

        if (showDebugInfo)
            Debug.Log($"Region {regionID} generated: {regionTiles.Count} tiles | Theme: {dominantTheme} | Issues: {issuesAssigned} | Avg Health: {avgHealth:F1}");

        OnRegionGenerated?.Invoke(result);
        return result;
    }

    /// <summary>
    /// Calculates average health of all tiles in a region. Returns 0–100.
    /// </summary>
    public float GetRegionHealth(int regionID)
    {
        List<Tile> tiles = tileManager.GetTilesInRegion(regionID);
        if (tiles == null || tiles.Count == 0)
        {
            if (showDebugInfo) Debug.LogWarning($"RegionManager: No tiles found in region {regionID}!");
            return 0f;
        }

        float total = 0f;
        foreach (Tile tile in tiles)
            total += tile.CalculateHealth();

        float avg = total / tiles.Count;
        if (showDebugInfo) Debug.Log($"Region {regionID} Health: {avg:F1} ({tiles.Count} tiles)");
        return avg;
    }

    /// <summary>
    /// Aggregated (mean) TileStats across every tile in a region — a single pass over
    /// GetTilesInRegion, mirroring GetRegionHealth's structure/logging. Averages all eight
    /// fields (the 6 soil substats + vegetationCover + contamination) so a CalculateHealth-style
    /// readout works on the returned instance (soilComposite / CalculateHealth are derived).
    /// Returns a fully ZEROED TileStats for an empty or unknown region (added 2026-07-22 for the
    /// Selected Info Panel's Region mode).
    /// </summary>
    public TileStats GetRegionStats(int regionID)
    {
        TileStats agg = new TileStats
        {
            nutrientBalance    = 0f,
            soilOrganicMatter  = 0f,
            soilStructure      = 0f,
            biologicalActivity = 0f,
            waterDynamics      = 0f,
            erosionResistance  = 0f,
            vegetationCover    = 0f,
            contamination      = 0f
        };

        List<Tile> tiles = tileManager.GetTilesInRegion(regionID);
        if (tiles == null || tiles.Count == 0)
        {
            if (showDebugInfo) Debug.LogWarning($"RegionManager: No tiles found in region {regionID} for stats!");
            return agg;
        }

        foreach (Tile tile in tiles)
        {
            TileStats s = tile.stats;
            agg.nutrientBalance    += s.nutrientBalance;
            agg.soilOrganicMatter  += s.soilOrganicMatter;
            agg.soilStructure      += s.soilStructure;
            agg.biologicalActivity += s.biologicalActivity;
            agg.waterDynamics      += s.waterDynamics;
            agg.erosionResistance  += s.erosionResistance;
            agg.vegetationCover    += s.vegetationCover;
            agg.contamination      += s.contamination;
        }

        float n = tiles.Count;
        agg.nutrientBalance    /= n;
        agg.soilOrganicMatter  /= n;
        agg.soilStructure      /= n;
        agg.biologicalActivity /= n;
        agg.waterDynamics      /= n;
        agg.erosionResistance  /= n;
        agg.vegetationCover    /= n;
        agg.contamination      /= n;

        if (showDebugInfo) Debug.Log($"Region {regionID} Stats aggregated over {tiles.Count} tiles.");
        return agg;
    }

    /// <summary>
    /// Smoothed per-day change in this region's average health, in health-points — the
    /// average daily change over the last RunManager.TrendWindowDays days (fewer early on).
    /// 0 until two days have resolved or if the region is unknown. Read-only (Law 1) — drives
    /// the region trend arrow. A short recent-trend signal, NOT a run-progress history.
    /// </summary>
    public float GetRegionHealthTrend(int regionID)
        => _regionHealthTrend.TryGetValue(regionID, out float d) ? d : 0f;

    // --- Progress record accessors (2026-07-07) — read the long-lived FIFO history, NOT the
    // short trend window above. No UI/sparkline consumes these yet. ---

    /// <summary>
    /// Read-only (Law 1) copy of this region's daily-health progress history — oldest first,
    /// newest last — capped at regionHistoryCap samples (default 360). Empty (never null) for
    /// an unknown region. Distinct from GetRegionHealthTrend's short smoothing window.
    /// </summary>
    public IReadOnlyList<float> GetRegionHealthHistory(int regionID)
        => _regionHealthHistory.TryGetValue(regionID, out Queue<float> history)
            ? history.ToArray()
            : System.Array.Empty<float>();

    /// <summary>
    /// Net change across this region's whole recorded progress history: newest sample minus
    /// oldest sample. 0 when fewer than two samples exist yet (or the region is unknown).
    /// </summary>
    public float GetRegionProgress(int regionID)
    {
        if (!_regionHealthHistory.TryGetValue(regionID, out Queue<float> history) || history.Count < 2)
            return 0f;

        float oldest = history.Peek();
        float newest = history.Last();
        return newest - oldest;
    }

    // =====================================================================
    // STEP 1 — SEED TILE
    // =====================================================================

    private Vector2Int? FindSeedTile()
    {
        List<Tile> allTiles = tileManager.GetAllTiles();
        if (allTiles == null || allTiles.Count == 0) return null;

        List<Vector2Int> candidates = new List<Vector2Int>();

        foreach (Tile tile in allTiles)
        {
            foreach (Vector2Int dir in Directions)
            {
                Vector2Int neighbor = tile.gridPosition + dir;
                if (tileManager.GetTile(neighbor.x, neighbor.y) == null)
                    candidates.Add(neighbor);
            }
        }

        if (candidates.Count == 0) return null;
        return candidates[Random.Range(0, candidates.Count)];
    }

    // =====================================================================
    // STEP 2 — FLOOD FILL
    // =====================================================================

    private List<Vector2Int> FloodFillShape(Vector2Int seed, int targetSize, float flowFalloff, float enclosureBonus)
    {


        var chosen = new HashSet<Vector2Int>();
        // Priority: higher score = picked first. Score = enclosedNeighbors * 10 - dist * flowFalloff
        var candidates = new SortedDictionary<float, List<Vector2Int>>(Comparer<float>.Create((a, b) => b.CompareTo(a)));
        chosen.Add(seed);
        AddCandidates(seed, chosen, candidates, seed, flowFalloff);

        while (chosen.Count < targetSize && candidates.Count > 0)
        {
            // Pull highest-priority candidate
            var topKey = candidates.Keys.First();
            var topList = candidates[topKey];
            int pickIdx = Random.Range(0, topList.Count); // small jag: random among same-score
            Vector2Int selected = topList[pickIdx];
            topList.RemoveAt(pickIdx);
            if (topList.Count == 0) candidates.Remove(topKey);

            chosen.Add(selected);
            AddCandidates(selected, chosen, candidates, seed, flowFalloff);
        }

        // Phase 2 — Jag pass: ~15% chance to extend 1 tile into open frontier for organic edges
        if (chosen.Count >= targetSize)
        {
            var border = chosen
                .SelectMany(p => Directions.Select(d => p + d))
                .Where(p => !chosen.Contains(p) && tileManager.GetTile(p.x, p.y) == null)
                .Distinct().ToList();
            int jagCount = Mathf.RoundToInt(targetSize * 0.15f);
            Shuffle(border);
            for (int i = 0; i < jagCount && i < border.Count; i++)
                chosen.Add(border[i]);
        }

        FillEnclosedHoles(chosen, targetSize);

        return new List<Vector2Int>(chosen);
    }

    private void AddCandidates(Vector2Int pos, HashSet<Vector2Int> chosen,
        SortedDictionary<float, List<Vector2Int>> candidates, Vector2Int seed, float flowFalloff)
    {
        foreach (var dir in Directions)
        {
            Vector2Int nb = pos + dir;
            if (chosen.Contains(nb)) continue;
            if (tileManager.GetTile(nb.x, nb.y) != null) continue; // occupied by another region

            int enclosed = CountChosenNeighbors(nb, chosen);
            float dist = Vector2Int.Distance(nb, seed);
            float score = enclosed * 10f - dist * flowFalloff;

            // Avoid duplicate insertion
            bool alreadyQueued = candidates.Values.Any(list => list.Contains(nb));
            if (alreadyQueued) continue;

            if (!candidates.ContainsKey(score)) candidates[score] = new List<Vector2Int>();
            candidates[score].Add(nb);
        }
    }

    private int CountChosenNeighbors(Vector2Int pos, HashSet<Vector2Int> chosen)
    {
        int count = 0;
        foreach (Vector2Int dir in Directions)
            if (chosen.Contains(pos + dir)) count++;
        return count;
    }


    // =====================================================================
    // STEP 3 — SPAWN TILES WITH SCALED STATS
    // =====================================================================

    private List<Tile> SpawnRegionTiles(List<Vector2Int> positions, int regionID, RegionProfile profile)
    {
        List<Tile> spawned = new List<Tile>();

        foreach (Vector2Int pos in positions)
        {
            TileStats stats = new TileStats
            {
                nutrientBalance    = Random.Range(profile.nutrientBalanceRange.x,    profile.nutrientBalanceRange.y),
                soilOrganicMatter  = Random.Range(profile.soilOrganicMatterRange.x,  profile.soilOrganicMatterRange.y),
                soilStructure      = Random.Range(profile.soilStructureRange.x,      profile.soilStructureRange.y),
                biologicalActivity = Random.Range(profile.biologicalActivityRange.x, profile.biologicalActivityRange.y),
                waterDynamics      = Random.Range(profile.waterDynamicsRange.x,      profile.waterDynamicsRange.y),
                erosionResistance  = Random.Range(profile.erosionResistanceRange.x,  profile.erosionResistanceRange.y),
                vegetationCover    = Random.Range(profile.vegetationCoverRange.x,    profile.vegetationCoverRange.y),
                contamination      = Random.Range(profile.contaminationRange.x,      profile.contaminationRange.y)
            };

            Tile tile = tileManager.SpawnTile(pos.x, pos.y, stats, regionID);
            if (tile != null)
            {
                tile.issues = new List<TileIssue>();
                tile.tv     = new List<TileOverlayType>();
                tileManager.UpdateTileVisual(tile);
                spawned.Add(tile);
            }
        }

        return spawned;
    }

    /// <summary>
    /// Captures every tile's AUTHORED scale, hides the tiles at zero, then kicks off the shared
    /// pop-in reveal coroutine. Used by both GenerateNewRegion and GenerateInitialRegion (Zone 1)
    /// so every region — including the very first — reveals through the same animated path instead
    /// of popping in instantly.
    ///
    /// The authored scale is READ OFF the freshly instantiated tile rather than hardcoded, because
    /// tweening toward a literal Vector3.one is what broke this before: the tile prefab is authored
    /// at 0.55, so a 0→1 tween left every tile (and therefore every entity child, which inherits the
    /// tile root's scale) ~1.8× oversized. The old code dodged that by running the tween BACKWARDS —
    /// snap to 1, shrink to 0.55 — which is why the reveal looked inverted. Growing 0→captured keeps
    /// the correct final size and follows the prefab if a designer ever rescales it.
    /// </summary>
    private void AnimateRegionReveal(List<Tile> regionTiles, float stagger, System.Action onComplete = null)
    {
        var authoredScales = new Dictionary<Tile, Vector3>(regionTiles.Count);

        foreach (Tile tile in regionTiles)
        {
            GameObject tileGO = tileManager.GetTileGameObject(tile);
            if (tileGO == null) continue;

            Vector3 authored = tileGO.transform.localScale;

            // Defensive (Law 3 loud-warn): a tile already sitting at zero means something zeroed it
            // ahead of us (a double reveal, or a cancelled tween left mid-flight). Capturing that
            // would tween 0→0 and leave the tile permanently invisible, so fall back to unit scale.
            if (authored.sqrMagnitude < 0.0001f)
            {
                Debug.LogWarning($"RegionManager: tile {tile.gridPosition} was already at zero scale when the " +
                                 "reveal started — falling back to unit scale. Was AnimateRegionReveal called twice?");
                authored = Vector3.one;
            }

            authoredScales[tile] = authored;
            tileGO.transform.localScale = Vector3.zero; // invisible until its turn in the stagger
        }

        StartCoroutine(AnimateTiles(regionTiles, authoredScales, stagger, onComplete));
    }

    // Run animation separately — staggered pop from zero up to each tile's captured authored scale.
    private System.Collections.IEnumerator AnimateTiles(List<Tile> tiles, Dictionary<Tile, Vector3> authoredScales,
        float delay, System.Action onComplete = null)
    {
        foreach (Tile tile in tiles)
        {
            GameObject t = tileManager.GetTileGameObject(tile);
            if (t == null) continue;
            if (!authoredScales.TryGetValue(tile, out Vector3 target)) continue;

            // Cancel-safe, mirroring EntitySpawnTween.PopIn: never let two tweens fight over
            // localScale. Re-zero right before the pop so a tile stays hidden through its stagger
            // delay no matter what touched it in between.
            LeanTween.cancel(t);
            t.transform.localScale = Vector3.zero;

            LeanTween.scale(t, target, tileRevealPopDuration)
                .setEase(LeanTweenType.easeOutBack)
                .setOvershoot(tileRevealPopOvershoot);

            // Yield to wait for the specified delay before continuing to the next tile
            yield return new WaitForSeconds(delay);
        }

        // Let the final tile's pop finish before signalling completion.
        yield return new WaitForSeconds(tileRevealPopDuration);
        onComplete?.Invoke();
    }

    // =====================================================================
    // STEP 4 — ISSUE ASSIGNMENT
    // =====================================================================

    private int AssignIssues(List<Tile> tiles, RegionTheme dominantTheme, RegionProfile profile)
    {
        float density = Random.Range(profile.issueDensityRange.x, profile.issueDensityRange.y);
        int issueCount = Mathf.RoundToInt(tiles.Count * density);

        List<Tile> shuffled = new List<Tile>(tiles);
        Shuffle(shuffled);

        int assigned = 0;
        for (int i = 0; i < issueCount && i < shuffled.Count; i++)
        {
            Tile tile = shuffled[i];

            RegionTheme themeToApply = (Random.value <= profile.offThemeIssueProbability)
                ? RollOffTheme(dominantTheme, profile)
                : dominantTheme;

            IssueType issueType = ThemeToIssueType(themeToApply);
            TileIssue issue = TileIssueLibrary.Get(issueType);

            if (issue != null)
            {
                tileManager.ApplyIssue(tile, issue);
                assigned++;
            }
        }

        return assigned;
    }

    // =====================================================================
    // STEP 5 — SPAWN TABLE
    // =====================================================================

    /// <summary>
    /// Scatters the profile's spawn table across the region (replaces the old fixed
    /// Buildings + Organic Entity passes, 2026-07-17). Entries run in list order, so
    /// earlier rows get first pick of empty tiles. Per entry: roll `frequency` against
    /// each empty tile until `maxCount` is hit, then top up to `minCount` on random
    /// remaining empty tiles (the guarantee wins over a max authored below it).
    /// </summary>
    private void PlaceSpawnTableEntities(List<Tile> tiles, RegionProfile profile)
    {
        if (profile.spawnTable == null || profile.spawnTable.Count == 0) return;

        foreach (RegionSpawnEntry entry in profile.spawnTable)
        {
            if (entry == null || entry.entity == null)
            {
                Debug.LogWarning($"RegionManager: spawn table entry in profile '{profile.name}' has no TileEntitySO assigned — skipping.", profile);
                continue;
            }

            int cap = Mathf.Max(entry.maxCount, entry.minCount);
            if (cap == 0) continue;

            List<Tile> empties = new List<Tile>();
            foreach (Tile t in tiles)
                if (t.entity == null) empties.Add(t);
            Shuffle(empties);

            int placed = 0;
            foreach (Tile tile in empties)
            {
                if (placed >= cap) break;
                if (Random.value < entry.frequency)
                {
                    tileManager.SpawnFromDef(tile, entry.entity);
                    placed++;
                }
            }

            // Top up to the guaranteed minimum on tiles the rolls skipped.
            if (placed < entry.minCount)
            {
                foreach (Tile tile in empties)
                {
                    if (placed >= entry.minCount) break;
                    if (tile.entity != null) continue;
                    tileManager.SpawnFromDef(tile, entry.entity);
                    placed++;
                }
            }

            if (showDebugInfo)
                Debug.Log($"RegionManager: spawn table placed {placed}× '{entry.entity.displayName}' (min {entry.minCount}, cap {cap}).");
        }
    }

    // =====================================================================
    // STEP 6 — FORCED ENTITIES (event-driven override)
    // =====================================================================

    private void ApplyForcedEntities(Vector2Int seedPosition, RegionProfile profile)
    {
        foreach (ForcedEntityPlacement placement in profile.forcedEntities)
        {
            Vector2Int targetPos = seedPosition + placement.offset;
            Tile tile = tileManager.GetTile(targetPos.x, targetPos.y);
            if (tile == null)
            {
                Debug.LogWarning($"RegionManager: ForcedEntityPlacement at {targetPos} has no tile — skipping.");
                continue;
            }

            // Code-owned entity id plumbing (2026-07-07): designers author the exact displayName
            // they see on the TileEntitySO; code derives the machine id at resolve time via
            // TileEntitySO.GenerateId — never a hand-authored id string. Loud-warn and skip on a
            // blank name or an unresolvable one (Law 3); SpawnById/EntityRegistry.Get already
            // loud-fails a registry miss, so a blank check here just short-circuits the common typo.
            if (string.IsNullOrWhiteSpace(placement.displayName))
            {
                Debug.LogWarning($"RegionManager: ForcedEntityPlacement at {targetPos} has no displayName assigned — skipping.");
                continue;
            }

            string entityId = TileEntitySO.GenerateId(placement.displayName);
            tileManager.SpawnById(tile, entityId);
        }
    }

    // =====================================================================
    // THEME ROLLING
    // =====================================================================

    private RegionTheme RollTheme(RegionProfile profile)
    {
        int total = profile.loggedTreesWeight
                  + profile.nutrientDepletionWeight
                  + profile.heavyMetalContaminationWeight
                  + profile.activeErosionWeight
                  + profile.drainageCollapseWeight
                  + profile.soilCompactionWeight
                  + profile.chemicalBurnoutWeight;

        if (total <= 0) return RegionTheme.LoggedTrees;

        int roll = Random.Range(0, total);
        int cumulative = 0;

        if ((cumulative += profile.loggedTreesWeight)             > roll) return RegionTheme.LoggedTrees;
        if ((cumulative += profile.nutrientDepletionWeight)       > roll) return RegionTheme.NutrientDepletion;
        if ((cumulative += profile.heavyMetalContaminationWeight) > roll) return RegionTheme.HeavyMetalContamination;
        if ((cumulative += profile.activeErosionWeight)           > roll) return RegionTheme.ActiveErosion;
        if ((cumulative += profile.drainageCollapseWeight)        > roll) return RegionTheme.DrainageCollapse;
        if ((cumulative += profile.soilCompactionWeight)          > roll) return RegionTheme.SoilCompaction;

        return RegionTheme.ChemicalBurnout;
    }

    private RegionTheme RollOffTheme(RegionTheme exclude, RegionProfile profile)
    {
        var options = new List<(RegionTheme theme, int weight)>
        {
            (RegionTheme.LoggedTrees,             profile.loggedTreesWeight),
            (RegionTheme.NutrientDepletion,       profile.nutrientDepletionWeight),
            (RegionTheme.HeavyMetalContamination, profile.heavyMetalContaminationWeight),
            (RegionTheme.ActiveErosion,           profile.activeErosionWeight),
            (RegionTheme.DrainageCollapse,        profile.drainageCollapseWeight),
            (RegionTheme.SoilCompaction,          profile.soilCompactionWeight),
            (RegionTheme.ChemicalBurnout,         profile.chemicalBurnoutWeight),
        };

        options.RemoveAll(o => o.theme == exclude || o.weight <= 0);
        if (options.Count == 0) return exclude;

        int total = options.Sum(o => o.weight);
        int roll = Random.Range(0, total);
        int cumulative = 0;

        foreach (var (theme, weight) in options)
        {
            cumulative += weight;
            if (cumulative > roll) return theme;
        }

        return options[options.Count - 1].theme;
    }

    private IssueType ThemeToIssueType(RegionTheme theme)
    {
        switch (theme)
        {
            case RegionTheme.LoggedTrees:             return IssueType.LoggedTrees;
            case RegionTheme.NutrientDepletion:       return IssueType.NutrientDepletion;
            case RegionTheme.HeavyMetalContamination: return IssueType.HeavyMetalContamination;
            case RegionTheme.ActiveErosion:           return IssueType.ActiveErosion;
            case RegionTheme.DrainageCollapse:        return IssueType.DrainageCollapse;
            case RegionTheme.SoilCompaction:          return IssueType.SoilCompaction;
            case RegionTheme.ChemicalBurnout:         return IssueType.ChemicalBurnout;
            default:                                  return IssueType.LoggedTrees;
        }
    }

    // =====================================================================
    // HELPERS
    // =====================================================================



    /// <summary>
    /// Applies issue assignment and building placement to an already-spawned set of tiles.
    /// Used by GameManager.SpawnInitialZone() so Zone 1 goes through the same pipeline as all other regions.
    /// </summary>
    public void InitializeRegion(List<Tile> tiles, RegionProfile profile)
    {
        if (tiles == null || tiles.Count == 0 || profile == null) return;

        RegionTheme theme = profile.forceTheme ? profile.forcedTheme : RollTheme(profile);
        AssignIssues(tiles, theme, profile);

        PlaceSpawnTableEntities(tiles, profile);
        if (showDebugInfo)
            Debug.Log($"RegionManager.InitializeRegion: theme={theme}, issues assigned, spawn table applied.");
    }

    /// <summary>
    /// After flood-fill, finds empty-cell pockets that are fully enclosed by the
    /// new region (chosen) + any already-existing tiles. Adds enclosed cells to
    /// chosen so the spawned region has no interior voids.
    ///
    /// "Enclosed" = the pocket's BFS cannot reach a cell farther than
    /// (targetSize + 10) from the zone centroid without passing through a tile.
    /// 4-directional only.
    /// </summary>
    private void FillEnclosedHoles(HashSet<Vector2Int> chosen, int targetSize)
    {
        // --- Phase A: Collect frontier ---
        // All empty cells directly adjacent to any chosen tile.
        // These are the only possible entry-points for enclosed pockets.
        var frontier = new HashSet<Vector2Int>();
        foreach (Vector2Int pos in chosen)
        {
            foreach (Vector2Int dir in Directions)
            {
                Vector2Int nb = pos + dir;
                if (!chosen.Contains(nb) && tileManager.GetTile(nb.x, nb.y) == null)
                    frontier.Add(nb);
            }
        }

        if (frontier.Count == 0) return;

        // Compute region centroid for the exterior threshold check.
        float cx = 0f, cy = 0f;
        foreach (Vector2Int pos in chosen) { cx += pos.x; cy += pos.y; }
        cx /= chosen.Count;
        cy /= chosen.Count;

        // Any empty cell farther than this from the centroid is definitionally
        // outside the region's influence — reaching it means the component is open.
        float exteriorThreshold = targetSize + 10f;

        // --- Phase B: Connected-component BFS on empty space ---
        var visited = new HashSet<Vector2Int>();

        foreach (Vector2Int startCell in frontier)
        {
            if (visited.Contains(startCell)) continue;

            var component = new List<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            bool isExterior = false;

            visited.Add(startCell);
            queue.Enqueue(startCell);

            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();

                // Exterior check: is this cell far enough to be "open world"?
                float dx = cell.x - cx;
                float dy = cell.y - cy;
                if (dx * dx + dy * dy > exteriorThreshold * exteriorThreshold)
                {
                    isExterior = true;
                    // Early exit — no need to map the full exterior component.
                    // Remaining queued cells are already in `visited` so they
                    // won't seed duplicate components.
                    break;
                }

                component.Add(cell);

                foreach (Vector2Int dir in Directions)
                {
                    Vector2Int nb = cell + dir;
                    if (visited.Contains(nb)) continue;          // already seen
                    if (chosen.Contains(nb)) continue;           // wall: new region tile
                    if (tileManager.GetTile(nb.x, nb.y) != null) continue; // wall: existing region tile
                    visited.Add(nb);
                    queue.Enqueue(nb);
                }
            }

            // --- Phase C: Fill holes ---
            // Exterior components are open space — leave them alone.
            // Enclosed components are holes — absorb into this region.
            if (!isExterior)
            {
                foreach (Vector2Int cell in component)
                    chosen.Add(cell);
            }
        }
    }

    private RegionProfile lastUsedProfile = null;

    private RegionProfile GetProfileForNextRegion()
    {
        // Ordered-early / random-after split: honor the defaultProfiles tooltip ("Index 0 =
        // Region 2, Index 1 = Region 3, etc.") for the first defaultProfiles.Count regions, so an
        // authored zone (e.g. the onboarding factory zone) always lands on a fixed ordinal instead
        // of waiting on a random roll. Called BEFORE nextRegionID++, so nextRegionID IS the id of
        // the region currently being generated.
        int orderedIndex = nextRegionID - 2;
        if (defaultProfiles != null && orderedIndex >= 0 && orderedIndex < defaultProfiles.Count && defaultProfiles[orderedIndex] != null)
        {
            RegionProfile ordered = defaultProfiles[orderedIndex];
            lastUsedProfile = ordered;

            if (showDebugInfo)
                Debug.Log($"RegionManager: ordered profile '{ordered.name}' for Region {nextRegionID} (index {orderedIndex}).");

            return ordered;
        }

        // Past the authored sequence (orderedIndex >= defaultProfiles.Count) — fall back to the
        // existing random-pool selection below, unchanged.
        // Build the candidate pool — fallback is always included as a safety net
        List<RegionProfile> pool = new List<RegionProfile>();

        if (defaultProfiles != null)
            foreach (RegionProfile p in defaultProfiles)
                if (p != null) pool.Add(p);

        if (fallbackProfile != null && !pool.Contains(fallbackProfile))
            pool.Add(fallbackProfile);

        if (pool.Count == 0)
        {
            Debug.LogError("RegionManager: No profiles available! Assign defaultProfiles or fallbackProfile.");
            return null;
        }

        // With more than one option, remove the last used to prevent back-to-back repeats
        if (pool.Count > 1 && lastUsedProfile != null)
            pool.Remove(lastUsedProfile);

        RegionProfile selected = pool[Random.Range(0, pool.Count)];
        lastUsedProfile = selected;

        if (showDebugInfo)
            Debug.Log($"RegionManager: Selected profile '{selected.name}' for Region {nextRegionID}.");

        return selected;
    }

    /// <summary>
    /// Generates Region 1 using the same flood-fill + full pipeline as GenerateNewRegion,
    /// but accepts an explicit seed position instead of searching for one (no tiles
    /// exist yet). Called exclusively by GameManager.SpawnInitialZone.
    /// </summary>
    public RegionGenerationResult GenerateInitialRegion(Vector2Int seed, RegionProfile overrideProfile = null)
    {
        if (tileManager == null) { Debug.LogError("RegionManager: TileManager missing!"); return null; }

        // Zone 1 authority lives here: event override > the inspector zone1Profile > random pool.
        RegionProfile profile = overrideProfile ?? zone1Profile ?? GetProfileForNextRegion();
        if (profile == null) { Debug.LogError("RegionManager: No Region 1 profile assigned!"); return null; }

        const int regionID = 1;

        // Step 2 — Organic flood-fill shape (identical to GenerateNewRegion)
        int targetSize = Random.Range(profile.sizeRange.x, profile.sizeRange.y + 1);
        List<Vector2Int> positions = FloodFillShape(seed, targetSize, profile.flowFalloff, profile.enclosureBonus);
        if (positions.Count == 0)
        {
            Debug.LogError("RegionManager: GenerateInitialRegion flood fill produced no positions!");
            return null;
        }

        // Steps 3–8 are identical to GenerateNewRegion
        List<Tile> regionTiles = SpawnRegionTiles(positions, regionID, profile);

        RegionTheme dominantTheme = profile.forceTheme ? profile.forcedTheme : RollTheme(profile);
        int issuesAssigned = AssignIssues(regionTiles, dominantTheme, profile);

        PlaceSpawnTableEntities(regionTiles, profile);

        if (profile.forceSpecificEntities)
            ApplyForcedEntities(seed, profile);

        // Zone-1 reveal — entities are placed first (mirrors GenerateNewRegion's ordering), THEN
        // the tiles animate in. Onboarding's phase-1 advance is gated on OnInitialRegionRevealed.
        AnimateRegionReveal(regionTiles, zone1RevealStagger, () => OnInitialRegionRevealed?.Invoke());

        if (resourceManager != null && profile.workerReward > 0)
            resourceManager.IncreaseTotalPeople(profile.workerReward);

        float avgHealth = regionTiles.Average(t => t.CalculateHealth());
        float contamCoverage = (float)regionTiles.Count(t => t.stats.contamination >= 60f) / regionTiles.Count;

        RegionGenerationResult result = new RegionGenerationResult
        {
            regionID              = regionID,
            tileCount             = regionTiles.Count,
            dominantTheme         = dominantTheme,
            averageStartingHealth = avgHealth,
            issuesAssigned        = issuesAssigned,
            contaminationCoverage = contamCoverage,
            approximateCenter     = CalculateCenter(positions),
            wasTriggeredByEvent   = false,
            sourceEventID         = null
        };
        PopulateNotableFindings(result, dominantTheme);

        if (showDebugInfo)
            Debug.Log($"Region 1 (initial) generated — {regionTiles.Count} tiles | Theme: {dominantTheme} | Issues: {issuesAssigned} | Avg Health: {avgHealth:F1}");

        OnRegionGenerated?.Invoke(result);
        return result;
    }

    private Vector2Int CalculateCenter(List<Vector2Int> positions)
    {
        int sumX = 0, sumY = 0;
        foreach (var pos in positions) { sumX += pos.x; sumY += pos.y; }
        return new Vector2Int(sumX / positions.Count, sumY / positions.Count);
    }

    private void PopulateNotableFindings(RegionGenerationResult result, RegionTheme theme)
    {
        result.notableFindings.Add($"Dominant issue: {theme}.");

        if (result.contaminationCoverage > 0.3f)
            result.notableFindings.Add("High contamination coverage detected across region.");
    }

    public float GetTotalAverageHealth()
    {
        List<Tile> allTiles = tileManager.GetAllTiles();
        if (allTiles == null || allTiles.Count == 0) return 0f;

        float total = 0f;
        foreach (Tile tile in allTiles)
            total += tile.CalculateHealth();

        return total / allTiles.Count;
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
