using System.Collections.Generic;
using Habitales.Entities;
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
    [CreateAssetMenu(menuName = "Habitales/Actions/RemoveEntitiesEffect")]
    public class RemoveEntities : ActionEffectHook
    {
        [SerializeReference] List<TileEntitySO> entitiesToRemove;
        
        // Bespoke per-tile work, invoked by GenericPlayerAction.ExecuteOnTile for each
        // selected tile. Put stat changes, conditional spawns, overlay ops, etc. here.
        // `tileManager` is the live manager so hooks can spawn/remove entities.
        public override void Apply(Tile tile, TileManager tileManager)
        {
            if (tile == null || tileManager == null) return;

            if (entitiesToRemove == null || entitiesToRemove.Count == 0) return;
            foreach (var e in entitiesToRemove)
            {
                if (e.EntityId == tile.entity.entityId)
                {
                    tileManager.RemoveEntity(tile, false, "Removed...");
                    break;
                }
            }
        }
    }
}
