using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class ProgressionPersistence
{
    private static string SavePath => Path.Combine(Application.persistentDataPath, "progression.json");

    [Serializable]
    private class SaveData
    {
        public int level;
        public int totalXp;
        public int currentLevelXp;
        public List<string> unlockedPlantIds;
        public List<string> plantedEverIds;
        public List<int> unlockedPlantSeeds;
        public List<string> unlockedPlantNames;
        public int lastSeenLevel;
        public int lastSeenTotalXp;
        public int lastSeenUnlockCount;
    }

    public static void Save(PlayerProgressionSO so)
    {
        if (so == null) return;
        var data = new SaveData
        {
            level               = so.level,
            totalXp             = so.totalXp,
            currentLevelXp      = so.currentLevelXp,
            unlockedPlantIds    = new List<string>(so.unlockedPlantIds),
            plantedEverIds      = new List<string>(so.plantedEverIds),
            unlockedPlantSeeds  = new List<int>(so.unlockedPlantSeeds),
            unlockedPlantNames  = new List<string>(so.unlockedPlantNames),
            lastSeenLevel       = so.lastSeenLevel,
            lastSeenTotalXp     = so.lastSeenTotalXp,
            lastSeenUnlockCount = so.lastSeenUnlockCount
        };
        File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
        Debug.Log($"[Progression] Saved → {SavePath}");
    }

    public static void Load(PlayerProgressionSO so)
    {
        if (so == null || !File.Exists(SavePath)) return;
        try
        {
            var json = File.ReadAllText(SavePath);
            var data = JsonUtility.FromJson<SaveData>(json);
            if (data == null) return;
            so.level               = data.level;
            so.totalXp             = data.totalXp;
            so.currentLevelXp      = data.currentLevelXp;
            so.unlockedPlantIds    = data.unlockedPlantIds   ?? new List<string>();
            so.plantedEverIds      = data.plantedEverIds     ?? new List<string>();
            so.unlockedPlantSeeds  = data.unlockedPlantSeeds ?? new List<int>();
            so.unlockedPlantNames  = data.unlockedPlantNames ?? new List<string>();
            so.lastSeenLevel       = data.lastSeenLevel == 0 ? 1 : data.lastSeenLevel;
            so.lastSeenTotalXp     = data.lastSeenTotalXp;
            so.lastSeenUnlockCount = data.lastSeenUnlockCount;

            // NOTE: cube-era procedural-plant re-hydration (GeneratedPlantRegistry) and the
            // unlockPool filter were removed with the cube system. The seed/name lists are
            // still loaded for forward compat but drive nothing in Alpha.

            Debug.Log($"[Progression] Loaded — Level {so.level}, {so.totalXp} XP, {so.unlockedPlantIds.Count} unlocks");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Progression] Load failed, using defaults: {e.Message}");
        }
    }

    public static void Reset(PlayerProgressionSO so)
    {
        if (File.Exists(SavePath)) File.Delete(SavePath);
        if (so != null)
        {
            so.level               = 1;
            so.totalXp             = 0;
            so.currentLevelXp      = 0;
            so.unlockedPlantIds.Clear();
            so.plantedEverIds.Clear();
            so.unlockedPlantSeeds.Clear();
            so.unlockedPlantNames.Clear();
            so.lastSeenLevel       = 1;
            so.lastSeenTotalXp     = 0;
            so.lastSeenUnlockCount = 0;
        }
        Debug.Log("[Progression] Reset.");
    }
}
