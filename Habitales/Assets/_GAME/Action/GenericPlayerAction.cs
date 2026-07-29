using System.Collections.Generic;
using UnityEngine;
using Habitales.Actions;

// GenericPlayerAction — the data-driven runtime half of an action (arch §3.4). Wraps an
// ActionSO and turns it into a live PlayerAction: all metadata reads from the SO, and
// ExecuteOnTile simply runs the SO's authored effects in order. This is the action-side
// twin of GenericTileEntity/TileEntitySO — authored actions need NO bespoke C# subclass.
//
// The cost math (GetMaxTiles / CalculateDays / fatigue) lives unchanged in the PlayerAction
// base; it just reads the values surfaced below.
public class GenericPlayerAction : PlayerAction
{
    private readonly ActionSO _def;

    public GenericPlayerAction(ActionSO def)
    {
        _def = def;
    }

    public override string ActionId                => _def.actionId;
    public override string ActionName              => _def.displayName;
    public override string Description             => _def.description;
    public override SelectionMode selectionMode    => _def.selectionMode;
    public override ActionCategory Category        => MapGroup(_def.group);
    public override ExamineActionType ExamineReport => _def.examineReport;

    // Crew of 0 would divide-by-zero in the cost math; clamp to a sane floor.
    public override int MinPeoplePerTile           => Mathf.Max(1, _def.minPeoplePerTile);
    public override int BaseDays                   => Mathf.Max(1, _def.baseDays);
    public override int MinDays                    => Mathf.Max(1, _def.minDays);
    public override float FatigueMultiplierPerTile => _def.fatigueMultiplierPerTile;

    public override Sprite Icon => _def.icon;

    public override string Lore        => _def.lore;
    public override string InfoTooltip => _def.infoTooltip;
    public override IReadOnlyList<Sprite> SupplementaryImages => _def.supplementaryImages;

    public override void ExecuteOnTile(Tile tile, TileManager tileManager)
    {
        if (tile == null || _def.effects == null) return;
        foreach (ActionEffect effect in _def.effects)
            effect.Apply(tile, tileManager);
    }

    /// <summary>
    /// Selection filter, asked per candidate tile by TileSelector. Delegates to the SO's effects:
    /// a tile is targetable if ANY effect that expresses an opinion accepts it, and — the common
    /// case — every tile is targetable when no effect filters at all, so examining is untouched.
    /// This is how "Remove Trash only picks trash tiles" is authored: the RemoveEntities hook
    /// already lists exactly what the action removes, so targeting follows that list with no extra
    /// authoring (2026-07-29).
    ///
    /// Asks EVERY effect, not just CustomBehavior ones (2026-07-29): a PlaceEntity effect refuses
    /// occupied tiles, so a plant action can no longer be spent on a tile holding trash or a tree
    /// and quietly place nothing.
    /// </summary>
    public override bool CanTargetTile(Tile tile)
    {
        if (_def.effects == null) return true;

        bool anyFilter = false;
        foreach (ActionEffect effect in _def.effects)
        {
            bool? verdict = effect.CanTargetTile(tile);
            if (verdict == null) continue;   // effect doesn't filter — ignore it

            anyFilter = true;
            if (verdict.Value) return true;
        }

        return !anyFilter;
    }

    // The author-facing 3-value ActionGroup maps onto the broader global ActionCategory.
    // Emergency is intentionally unreachable here — it is dormant and not authorable.
    private static ActionCategory MapGroup(ActionGroup group)
    {
        switch (group)
        {
            case ActionGroup.Examine:   return ActionCategory.Examine;
            case ActionGroup.Intervene: return ActionCategory.Intervene;
            case ActionGroup.Cleanup:   return ActionCategory.Cleanup;
            default:                    return ActionCategory.Intervene;
        }
    }
}
