using UnityEngine;

// RunEndCoordinator — owns the META layer at run-end (arch §Meta), decoupled from the run
// simulation. RunManager calls ProcessRunEnd() when a run ends; this computes XP, applies it to
// the player's progression, persists it, and returns a summary (+ the Azi flavour line) for the
// run-end UI.
//
// In Alpha, XP unlocks NOTHING functional — it is a cosmetic/feel reward. The prototype's
// per-level procedural plant-unlock path (GrantOnePlantUnlock → GeneratedPlantRegistry) is gone
// with the cube system.
namespace Habitales.Meta
{
    public class RunEndCoordinator : MonoBehaviour
    {
        [SerializeField] private PlayerProgressionSO progression;

        [Header("XP weights (per-tile, applied at run-end)")]
        [SerializeField] private float xpPerCriticalTile = 0.1f;
        [SerializeField] private float xpPerDegradedTile = 0.6f;
        [SerializeField] private float xpPerThrivingTile = 2.4f;

        [Header("Season grade thresholds (normalized weighted-health score, 0..1)")]
        [Tooltip("Score at/above this → Exemplary.")]
        [SerializeField] [Range(0f, 1f)] private float exemplaryAt = 0.85f;
        [Tooltip("Score at/above this (and below Exemplary) → Commendable.")]
        [SerializeField] [Range(0f, 1f)] private float commendableAt = 0.65f;
        [Tooltip("Score at/above this (and below Commendable) → Adequate.")]
        [SerializeField] [Range(0f, 1f)] private float adequateAt = 0.4f;
        // Anything below adequateAt buckets to Concerning — SeasonGrade.Collapse is never reached
        // by score alone, only forced by ProcessRunEnd's collapsed flag (arch ENDGAME_BUILD_PLAN §3:
        // Collapse is the ≥90%-critical early exit, a distinct end-reason, not a score bucket).

        public struct RunEndSummary
        {
            public int         xpEarned;
            public int         xpBefore;
            public int         levelUps;
            public string      aziLine;
            public SeasonGrade grade;
        }

        private void Awake()
        {
            if (progression == null)
                Debug.LogError($"{name}: RunEndCoordinator has no PlayerProgressionSO assigned — XP/level-up will not persist. Wire it in the Inspector.", this);
        }

        private void Start()
        {
            // Single load point for progression (was duplicated in RunManager.Start + ActionManager.Awake).
            ProgressionPersistence.Load(progression);
        }

        // Called by RunManager.TriggerGameOver. Pure meta: XP → progression → save → summary.
        // collapsed forces the grade to SeasonGrade.Collapse regardless of the tile mix — RunManager
        // knows why the run ended (Ecosystem Collapse vs Field Season Complete) and passes it through.
        public RunEndSummary ProcessRunEnd(int thriving, int degraded, int critical, int peakThriving, bool collapsed = false)
        {
            float raw = critical * xpPerCriticalTile + degraded * xpPerDegradedTile + thriving * xpPerThrivingTile;
            int   xpEarned = Mathf.RoundToInt(raw);

            int xpBefore = 0;
            int levelUps = 0;
            if (progression != null)
            {
                xpBefore = progression.totalXp;
                levelUps = progression.AddXp(xpEarned);
                // Alpha: level-up is a cosmetic/feel reward — no functional unlock granted here.
                ProgressionPersistence.Save(progression);
            }

            SeasonGrade grade = collapsed ? SeasonGrade.Collapse : ComputeGrade(thriving, degraded, critical);

            Debug.Log($"[RunEnd] XP +{xpEarned} (thriving {thriving}×{xpPerThrivingTile}, degraded {degraded}×{xpPerDegradedTile}, critical {critical}×{xpPerCriticalTile}) | level-ups: {levelUps} | grade: {grade}");

            return new RunEndSummary
            {
                xpEarned = xpEarned,
                xpBefore = xpBefore,
                levelUps = levelUps,
                aziLine  = GenerateAziLine(grade),
                grade    = grade,
            };
        }

        // Debug (F10): reset progression to defaults.
        public void ResetProgression() => ProgressionPersistence.Reset(progression);

        // Same per-tile weights XP uses (arch ENDGAME_BUILD_PLAN §3), normalized to 0..1 by dividing
        // by the score an all-thriving region would produce, then bucketed by the serialized
        // thresholds. Instance method (not static) because the thresholds are serialized data.
        public SeasonGrade ComputeGrade(int thrivingCount, int degradedCount, int criticalCount)
        {
            int totalTiles = thrivingCount + degradedCount + criticalCount;
            if (totalTiles <= 0) return SeasonGrade.Concerning;

            float weightedScore = criticalCount * xpPerCriticalTile + degradedCount * xpPerDegradedTile + thrivingCount * xpPerThrivingTile;
            float normalized    = weightedScore / (totalTiles * xpPerThrivingTile);

            if (normalized >= exemplaryAt)   return SeasonGrade.Exemplary;
            if (normalized >= commendableAt) return SeasonGrade.Commendable;
            if (normalized >= adequateAt)    return SeasonGrade.Adequate;
            return SeasonGrade.Concerning;
        }

        private static string GenerateAziLine(SeasonGrade grade)
        {
            switch (grade)
            {
                case SeasonGrade.Collapse:    return "Tough run. We didn't quite get there. Next time?";
                case SeasonGrade.Concerning:  return "We made a small dent. Felt like the start of something.";
                case SeasonGrade.Adequate:    return "Solid run, Cap. The land remembers what we did.";
                case SeasonGrade.Commendable: return "Look at what we built. I'm proud of us.";
                case SeasonGrade.Exemplary:   return "Cap... this is the best I've ever seen this region look. HQ's going to want to know how we did this.";
                default:                      return "Look at what we built. I'm proud of us.";
            }
        }
    }
}
