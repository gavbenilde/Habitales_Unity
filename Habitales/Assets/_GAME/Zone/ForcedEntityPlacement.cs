using UnityEngine;

[System.Serializable]
public class ForcedEntityPlacement
{
    [Tooltip("Offset from the zone's seed tile in grid units.")]
    public Vector2Int offset;

    [Tooltip("The entity type string to spawn. Must match TileEntity.entityType exactly.")]
    public string entityType;
}