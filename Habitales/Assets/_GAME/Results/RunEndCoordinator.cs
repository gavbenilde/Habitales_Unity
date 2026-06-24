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

        public struct RunEndSummary
        {
            public int    xpEarned;
            public int    xpBefore;
            public int    levelUps;
            public string aziLine;
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
        public RunEndSummary ProcessRunEnd(int thriving, int degraded, int critical, int peakThriving)
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

            Debug.Log($"[RunEnd] XP +{xpEarned} (thriving {thriving}×{xpPerThrivingTile}, degraded {degraded}×{xpPerDegradedTile}, critical {critical}×{xpPerCriticalTile}) | level-ups: {levelUps}");

            return new RunEndSummary
            {
                xpEarned = xpEarned,
                xpBefore = xpBefore,
                levelUps = levelUps,
                aziLine  = GenerateAziLine(peakThriving),
            };
        }

        // Debug (F10): reset progression to defaults.
        public void ResetProgression() => ProgressionPersistence.Reset(progression);

        private static string GenerateAziLine(int peak)
        {
            if (peak == 0)  return "Tough run. We didn't quite get there. Next time?";
            if (peak < 5)   return "We made a small dent. Felt like the start of something.";
            if (peak < 12)  return "Solid run, Cap. The land remembers what we did.";
            return "Look at what we built. I'm proud of us.";
        }
    }
}
