using System.Collections.Generic;
using UnityEngine;

public class EcologicalSurveyAction : PlayerAction
{
    public override string ActionName              => "Ecological Survey";
    public override string Description            => "Reveal hidden issues across a zone of tiles.";
    public override ActionCategory Category       => ActionCategory.Examine;
    public override SelectionMode selectionMode   => SelectionMode.FloodFill;
    public override int MinPeoplePerTile           => 1;
    public override int BaseDays                   => 1;
    public override int MinDays                    => 1;
    public override float FatigueMultiplierPerTile => 0.5f;

    public override bool CanExecute(List<Tile> tiles)
    {
        return tiles.Exists(t => !t.issuesRevealed);
    }

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        tile.issuesRevealed = true;
        // No OverflowTip by design — IssueMarker disappearing is the confirmation.
        // UpdateOverlays() is called by ActionManager automatically after this.
    }
}