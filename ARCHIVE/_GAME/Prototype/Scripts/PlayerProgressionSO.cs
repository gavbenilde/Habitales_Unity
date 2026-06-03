using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PlayerProgression", menuName = "Habitales/Player Progression")]
public class PlayerProgressionSO : ScriptableObject
{
    public int level   = 1;
    public int totalXp = 0;
    // Per-level XP: resets to 0 on each level-up. levelThresholds[level] is the cost
    // to advance FROM the current level to the next. totalXp is kept for end-of-run
    // and historical displays.
    public int currentLevelXp = 0;
    public List<string> unlockedPlantIds = new List<string>();

    // Seeds and names for procedurally-unlocked plants — same index across both lists
    // describes one plant. Persisted to JSON; GeneratedPlantRegistry re-hydrates SOs
    // on load by calling PlantingProfileGenerator.Generate(seed, name).
    public List<int> unlockedPlantSeeds = new List<int>();
    public List<string> unlockedPlantNames = new List<string>();

    // Persists across sessions — which plants the player has ever planted.
    public List<string> plantedEverIds = new List<string>();

    // DORMANT — replaced by GeneratedPlantRegistry. Kept per CLAUDE.md §7
    // ("dormant in code, do not delete"). No live code path reads this anymore.
    public List<PlantingProfileSO> unlockPool = new List<PlantingProfileSO>();

    // Resets each session — which plants were planted this run.
    [System.NonSerialized] public List<string> plantedThisSessionIds = new List<string>();

    // Snapshot of what the player last saw on the main menu. Persisted so the
    // level-up overlay only fires on the first main-menu visit of a new session;
    // runs played within the same session accumulate against this snapshot until
    // the player relaunches the game.
    public int lastSeenLevel       = 1;
    public int lastSeenTotalXp     = 0;
    public int lastSeenUnlockCount = 0;

    // Per-level XP cost (index i = cost to advance FROM level i to level i+1).
    // Index 0 is unused sentinel. Asset is authored with this interpretation:
    // L1→L2 = 100, L2→L3 = 135, etc.
    public int[] levelThresholds = { 0, 100, 135, 185, 245, 330 };

    public int XpIntoCurrentLevel => currentLevelXp;

    public int XpNeededForNextLevel
    {
        get
        {
            int idx = Mathf.Clamp(level, 1, levelThresholds.Length - 1);
            return levelThresholds[idx];
        }
    }

    // Returns the number of level-ups that occurred (0 if none).
    public int AddXp(int amount)
    {
        if (amount <= 0) return 0;
        totalXp        += amount;
        currentLevelXp += amount;
        int levelUps = 0;
        while (level < levelThresholds.Length - 1 && currentLevelXp >= levelThresholds[level])
        {
            currentLevelXp -= levelThresholds[level];
            level++;
            levelUps++;
        }
        // Cap overflow at the max-level threshold so the bar never reads weirdly.
        if (level >= levelThresholds.Length - 1)
            currentLevelXp = Mathf.Min(currentLevelXp, levelThresholds[levelThresholds.Length - 1]);
        return levelUps;
    }
}
