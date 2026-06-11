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

    [SerializeField] private TrashVisualConfig visualConfig;

    // Per-instance "inspected" flag now lives in GenericTileEntity.hookState (arch §5.3),
    // replacing the legacy isInspected field on the hand-coded trash entities.
    private const string INSPECTED_KEY = "inspected";

    private static bool IsTrash(Tile t)
        => t.entity != null && (t.entity.entityId == "trash_bio" || t.entity.entityId == "trash_nonbio");

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

        bool isBio = tile.entity.entityId == "trash_bio";
        List<Sprite> variants = isBio ? visualConfig?.bioVariants : visualConfig?.nonBioVariants;
        if (visualConfig != null && variants != null && variants.Count > 0)
        {
            Sprite pick = variants[Random.Range(0, variants.Count)];
            EntityVisualizer vis = tileManager.GetEntityVisualizer(tile);
            if (vis != null) vis.SetSprite(pick);
        }
    }
}