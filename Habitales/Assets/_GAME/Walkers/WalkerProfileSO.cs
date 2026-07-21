using UnityEngine;

// Per-species tuning for the Walker system. One asset per species (Worker, Carabao, Warty
// Pig) — species differences live entirely in this data; Walker/WorkerWalker/AnimalWalker
// never subclass per species. Mirrors TileEntitySO/ActionSO's CreateAssetMenu convention.
[CreateAssetMenu(fileName = "WalkerProfile", menuName = "Habitales/Walkers/Walker Profile")]
public class WalkerProfileSO : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Human-facing name, e.g. \"Worker\", \"Carabao\", \"Warty Pig\". Authoring/debug only.")]
    public string displayName;
    [Tooltip("The figure's prefab — a card-style billboard matching the tabletop look. " +
             "Worker prefabs need a WorkerWalker component; animal prefabs need an AnimalWalker component.")]
    public GameObject prefab;

    [Header("Roaming")]
    [Tooltip("World units per second while roaming. Exhausted workers multiply this by fatiguedSpeedMultiplier.")]
    public float moveSpeed = 1.5f;
    [Tooltip("Worker-only: Exhausted (fatigued) walkers move at moveSpeed × this. Ignored by animals.")]
    public float fatiguedSpeedMultiplier = 0.5f;
    [Tooltip("Idle beat range (seconds) between arriving at a destination and picking the next one.")]
    public float idleMin = 1.5f;
    [Tooltip("Idle beat range (seconds) between arriving at a destination and picking the next one.")]
    public float idleMax = 4f;
    [Tooltip("Random offset (x/z, each in [-value, +value]) applied within a tile while roaming. Default 0.6.")]
    public float roamDeviation = 0.6f;
    [Tooltip("Random offset applied when a worker lands from a Working flight. Kept small so workers " +
             "cluster tightly at the work site. Default 0.1.")]
    public float workDeviation = 0.1f;
    [Tooltip("Max grid (Manhattan) distance considered for a roaming destination pick. A large or " +
             "effectively unlimited value is fine — the power-law weighting below already favors nearby tiles.")]
    public int destinationRadius = 9999;
    [Tooltip("Power-law falloff exponent (k) for destination weighting: weight = 1 / max(d,1)^k. " +
             "0 = uniform random pick across all tiles; higher values (2-3 recommended) bias strongly " +
             "toward nearby tiles with occasional long trips.")]
    public float falloffExponent = 2.5f;
    [Tooltip("Per-step random jitter added to each intermediate waypoint while path-following, so the walk " +
             "doesn't look robotic. The final waypoint uses roamDeviation instead, not this.")]
    public float waypointJitter = 0.15f;

    [Header("Animal Follow Behaviour (ignored by workers — leave followChance at 0)")]
    [Tooltip("Chance a roaming pick targets a tile with exactly one WorkerWalker claim instead of an " +
             "empty tile, so the animal walks toward a nearby worker. Workers must leave this at 0.")]
    [Range(0f, 1f)]
    public float followChance = 0f;

    [Header("Working Flight (worker-only)")]
    [Tooltip("Flight duration in seconds — constant regardless of distance travelled, so short and long " +
             "hops take the same amount of time.")]
    public float flightDuration = 0.6f;
    [Tooltip("Max random launch delay (seconds) so a batch of workers scrambles instead of launching in lockstep.")]
    public float flightStaggerMax = 0.3f;
    [Tooltip("Peak vertical offset of the parabolic arc, in world units.")]
    public float arcHeight = 2f;
    [Tooltip("0→1 curve shaping the arc's vertical offset over the flight (evaluated at normalized flight time).")]
    public AnimationCurve arcCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 0f);
    [Tooltip("VFXManager key for the looping 'working' smoke, played on landing and stopped on re-scatter/" +
             "return-to-roaming (worker-only). Empty = no VFX.")]
    public string workingVfxKey = "Smoke";

    private void OnValidate()
    {
        if (idleMax < idleMin)
            Debug.LogWarning($"WalkerProfileSO '{name}': idleMax ({idleMax}) is less than idleMin ({idleMin}) — " +
                              "Random.Range(idleMin, idleMax) will misbehave.", this);
        if (prefab == null)
            Debug.LogWarning($"WalkerProfileSO '{name}': prefab is not assigned — WalkerManager cannot spawn this species.", this);
        if (destinationRadius <= 0)
            Debug.LogWarning($"WalkerProfileSO '{name}': destinationRadius ({destinationRadius}) should be positive.", this);
        if (flightDuration <= 0f)
            Debug.LogWarning($"WalkerProfileSO '{name}': flightDuration ({flightDuration}) should be positive — " +
                              "a Working walker would never finish its flight.", this);
    }
}
