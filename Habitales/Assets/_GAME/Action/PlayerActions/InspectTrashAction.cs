using System.Collections.Generic;
using UnityEngine;

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

    public override bool CanExecute(List<Tile> tiles)
    {
        return tiles.Exists(t =>
            (t.entity is TrashBioEntity bio    && !bio.isInspected)    ||
            (t.entity is TrashNonBioEntity nonBio && !nonBio.isInspected)
        );
    }

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        if (tile.entity is TrashBioEntity bio)
        {
            bio.isInspected = true;

            if (visualConfig != null &&
                visualConfig.bioVariants != null &&
                visualConfig.bioVariants.Count > 0)
            {
                Sprite pick = visualConfig.bioVariants[Random.Range(0, visualConfig.bioVariants.Count)];
                EntityVisualizer vis = tileManager.GetEntityVisualizer(tile);
                if (vis != null) vis.SetSprite(pick);
            }

            // OverflowTipSpawner.Instance.SpawnAtCursor("Bio waste — will decompose on its own");
        }
        else if (tile.entity is TrashNonBioEntity nonBio)
        {
            nonBio.isInspected = true;

            if (visualConfig != null &&
                visualConfig.nonBioVariants != null &&
                visualConfig.nonBioVariants.Count > 0)
            {
                Sprite pick = visualConfig.nonBioVariants[Random.Range(0, visualConfig.nonBioVariants.Count)];
                EntityVisualizer vis = tileManager.GetEntityVisualizer(tile);
                if (vis != null) vis.SetSprite(pick);
            }

            // OverflowTipSpawner.Instance.SpawnAtCursor("Non-bio waste — must be manually cleared");
        }
    }
}