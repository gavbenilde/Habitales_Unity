using UnityEngine;

[System.Serializable]
public class ForcedEntityPlacement
{
    [Tooltip("Offset from the zone's seed tile in grid units.")]
    public Vector2Int offset;

    [Tooltip("Legacy display name of the entity to spawn (e.g. \"Seedling\", \"Mature Tree\"). " +
             "RegionManager maps it to an entityId for the registry-driven spawn.")]
    public string entityType;
}