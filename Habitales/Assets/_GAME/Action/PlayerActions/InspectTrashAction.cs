using System.Collections.Generic;
using UnityEngine;
using Habitales.Entities;

public class InspectTrashAction : PlayerAction
{
    public override string ActionName              => "Inspect Trash";
    public override string Description            => "Examine waste on a tile to identify its type.";
    public override ActionCategory Category       => ActionCategory.Examine;
    public override SelectionMode selectionMode   => SelectionMode.FloodFill;
    public override int MinPeoplePerTile           => 1;
    public override int BaseDays                   => 1;
    public override int MinDays                    => 1;
    public override float FatigueMultiplierPerTile => 1.0f;

    // Per-instance "inspected" flag now lives in GenericTileEntity.hookState (arch §5.3),
    // replacing the legacy isInspected field on the hand-coded trash entities.
    private const string INSPECTED_KEY = "inspected";

    private static bool IsTrash(Tile t)
        => t.entity != null && (t.entity.entityId == EntityIds.TrashBio || t.entity.entityId == EntityIds.TrashNonBio);

    private static bool IsInspected(Tile t)
        => t.entity is GenericTileEntity ge && ge.hookState.ContainsKey(INSPECTED_KEY);

    public override bool CanExecute(List<Tile> tiles)
    {
        return tiles.Exists(t => IsTrash(t) && !IsInspected(t));
    }

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        if (!IsTrash(tile)) return;

        if (tile.entity is GenericTileEntity ge) ge.hookState[INSPECTED_KEY] = 1f;
    }
}