using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime-only registry of procedurally-generated PlantingProfileSOs.
/// Plain static C#, no MonoBehaviour or ScriptableObject.
/// or ScriptableObject. Profiles persist across sessions via their SEED:
/// ProgressionPersistence.Load() regenerates the same profile from the same seed.
/// Starter profiles (Cordia, Banaba, Vetiver, Ipil-ipil) live on disk as authored
/// assets and do NOT pass through this registry.
/// </summary>
public static class GeneratedPlantRegistry
{
    private static readonly Dictionary<string, PlantingProfileSO> _cache = new();

    // Themed Philippine-plant name pool. Banaba excluded — already a starter SO.
    private static readonly string[] _namePool =
    {
        "Narra", "Molave", "Balete", "Kamagong", "Antipolo", "Agoho", "Pili", "Katmon",
        "Bitaog", "Dao", "Almaciga", "Waling-waling", "Tayabak", "Ylang-ylang",
        "Kapa-kapa", "Doña Aurora", "Pitcher Plant", "Corpse Flower", "Salingbobog",
        "Balayong", "Abaca", "Anahaw", "Nipa", "Bignay", "Red Lauan", "Yakal",
        "Apitong", "Tindalo", "Guijo"
    };

    public static PlantingProfileSO RegisterFromSeed(int seed, string name)
    {
        var profile = PlantingProfileGenerator.Generate(seed, name);
        _cache[profile.profileID] = profile;
        return profile;
    }

    public static bool TryGet(string profileID, out PlantingProfileSO profile)
        => _cache.TryGetValue(profileID, out profile);

    public static IEnumerable<PlantingProfileSO> All => _cache.Values;

    /// <summary>
    /// Returns the first themed name not present in <paramref name="alreadyUsed"/>,
    /// or null if all 29 names are exhausted. Caller is expected to fall back to a
    /// generated string (e.g. $"Plant {seed}").
    /// </summary>
    public static string PickUnusedName(IEnumerable<string> alreadyUsed)
    {
        var used = alreadyUsed != null ? new HashSet<string>(alreadyUsed) : new HashSet<string>();
        foreach (var n in _namePool)
            if (!used.Contains(n)) return n;
        return null;
    }

    public static void Clear() => _cache.Clear();
}
