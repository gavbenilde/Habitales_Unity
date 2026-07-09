using UnityEngine;

[System.Serializable]
public class ForcedEntityPlacement
{
    [Tooltip("Offset from the zone's seed tile in grid units.")]
    public Vector2Int offset;

    [Tooltip("The exact displayName of the TileEntitySO to spawn here (e.g. \"Narra Tree Mature\"). " +
             "Code derives the machine id from this at resolve time (design decision 2026-07-07) — " +
             "never author a raw id string.")]
    public string displayName;
}
