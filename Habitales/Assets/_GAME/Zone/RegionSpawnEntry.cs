using UnityEngine;
using Habitales.Entities;

/// <summary>
/// One row of a RegionProfile's spawn table: which entity to scatter across the region
/// at generation time, how often, and how many. Replaces the old fixed-field
/// "Organic Entity Spawning" + "Buildings" passes (2026-07-17) — villages, factories,
/// trees, and trash are all authored the same way now.
/// </summary>
[System.Serializable]
public class RegionSpawnEntry
{
    [Tooltip("The entity to spawn. Direct asset reference — drag the TileEntitySO in.")]
    public TileEntitySO entity;

    [Tooltip("Per-empty-tile chance to spawn this entity, rolled tile by tile until Max Count is hit.")]
    [Range(0f, 1f)] public float frequency = 0.1f;

    [Tooltip("Guaranteed placements per region — if the frequency rolls land fewer than this, the shortfall is placed on random remaining empty tiles.")]
    [Min(0)] public int minCount = 0;

    [Tooltip("Hard cap per region. If authored below Min Count, the guarantee wins.")]
    [Min(0)] public int maxCount = 5;
}
