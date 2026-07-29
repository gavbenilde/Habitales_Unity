using UnityEngine;

// ActionEffectHook — the escape hatch for bespoke action effects, mirroring
// EntityBehaviourHook (arch §5.3). The boring common case is data (PlaceEntity);
// the unique case is a tiny subclass here that a half-programmer writes confidently.
//
// The hook is a ScriptableObject (shared, immutable) — it holds NO per-instance state.
// It is the action-side twin of Habitales.Entities.EntityBehaviourHook: where that one
// runs per-DAY on a living entity, this one runs once per TILE when the action resolves.
namespace Habitales.Actions
{
    public abstract class ActionEffectHook : ScriptableObject
    {
        // Bespoke per-tile work, invoked by GenericPlayerAction.ExecuteOnTile for each
        // selected tile. Put stat changes, conditional spawns, overlay ops, etc. here.
        // `tileManager` is the live manager so hooks can spawn/remove entities.
        public abstract void Apply(Tile tile, TileManager tileManager);

        // Optional SELECTION filter, asked once per candidate tile while the player is still
        // picking targets (TileSelector → PlayerAction.CanTargetTile → GenericPlayerAction):
        //   null  — this effect has no opinion. THE DEFAULT: effects don't restrict targeting.
        //   true  — this effect can act on the tile.
        //   false — it cannot.
        // GenericPlayerAction ORs the non-null verdicts: a tile is targetable if ANY filtering
        // effect accepts it, and every tile is targetable when no effect filters at all — so
        // adding this changed nothing for existing hooks.
        public virtual bool? CanTargetTile(Tile tile) => null;
    }
}
