using System.Collections.Generic;
using UnityEngine;
using Habitales.Core;

// VillageBehaviourHook — bespoke Village behaviour ported from the legacy VillageEntity
// (arch §5.3 / 4c). SUPPLEMENTS the generic lifecycle (village.asset has no dailyEffects, so the
// data pipeline is a no-op and this hook runs after it). Each day it rolls a small chance to start
// a kaingin (slash-and-burn) fire cluster — the "fire just started" interruption the prototype
// relies on.
//
// Author one asset (Habitales/Entities/Behaviours/Village) and assign it to the `village`
// TileEntitySO's `behaviour` field.
namespace Habitales.Entities
{
    [CreateAssetMenu(menuName = "Habitales/Entities/Behaviours/Factory")]
    public class FactoryBehaviorHook : EntityBehaviourHook
    {
        private const float TRASH_DAILY_CHANCE = 0.015f;
        private const int   TRASH_RADIUS          = 4;

        public override void OnDailyUpdate(Tile tile, TileEntity entity, in TickContext ctx)
        {
            if (Random.value >= TRASH_DAILY_CHANCE) return;

            // Fire the "kaingin just started" interruption through the meaning-event sink — no
            // singleton grab (S1). The real sink (EventManagerEntitySink) routes it to EventManager.
            ctx.Events.Raise(new EntityEvent
            {
                kind         = EntityEvent.ScriptedEvent,
                entityId     = entity?.entityId,
                gridPosition = tile.gridPosition,
                cause        = "first_trash",
            });

            SpawnTrash(tile, in ctx);
        }

        private void SpawnTrash(Tile tile, in TickContext ctx)
        {
            int trashCount = Random.Range(4, 8);
            List<Tile> targets = TilesInRadius(tile, in ctx, TRASH_RADIUS);

            for (int i = 0; i < trashCount && targets.Count > 0; i++)
            {
                int idx = Random.Range(0, targets.Count);
                Tile target = targets[idx];
                
                bool isOccupied = target.entity != null && target.entity.def != null;
                
                if (!isOccupied)
                    ctx.Tiles.SpawnById(target, "trash_bio");

                targets.RemoveAt(idx);
            }
        }

        private List<Tile> TilesInRadius(Tile center, in TickContext ctx, int radius)
        {
            var result = new List<Tile>();
            for (int x = -radius; x <= radius; x++)
            for (int y = -radius; y <= radius; y++)
            {
                if (x == 0 && y == 0) continue;
                Tile t = ctx.Tiles.GetTile(center.gridPosition.x + x, center.gridPosition.y + y);
                if (t != null) result.Add(t);
            }
            return result;
        }
    }
}
