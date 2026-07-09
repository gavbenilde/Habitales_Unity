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
        tileManager.RevealTileIssues(tile); // Law 1: reveal flag written by the owner, not the action
        // No OverflowTip by design — IssueMarker disappearing is the confirmation.
        // UpdateOverlays() is called by ActionManager automatically after this.
    }
}