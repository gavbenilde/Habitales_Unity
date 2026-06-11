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

    // Cube-era seed/name lists for procedurally-unlocked plants. Still serialized for
    // save-file forward compat, but inert in Alpha — the generator/registry that consumed
    // them was removed with the cube system. Drive nothing today.
    public List<int> unlockedPlantSeeds = new List<int>();
    public List<string> unlockedPlantNames = new List<string>();

    // Persists across sessions — which plants the player has ever planted.
    public List<string> plantedEverIds = new List<string>();

    // Resets each session — which plants were planted this run.
    [System.NonSerialized] public List<string> plantedThisSessionIds = new List<string>();

    // NOTE: the cube-era `unlockPool` (List<PlantingProfileSO>) was removed with the cube
    // system. In Alpha, XP unlocks nothing functional — see RunEndCoordinator.

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
