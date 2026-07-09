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
    [CreateAssetMenu(menuName = "Habitales/Entities/Behaviours/Village")]
    public class VillageBehaviourHook : EntityBehaviourHook
    {
        private const float KAINGIN_DAILY_CHANCE = 0.015f;
        private const int   FIRE_RADIUS          = 4;

        public override void OnDailyUpdate(Tile tile, TileEntity entity, in TickContext ctx)
        {
            if (Random.value >= KAINGIN_DAILY_CHANCE) return;

            // Fire the "kaingin just started" interruption through the meaning-event sink — no
            // singleton grab (S1). The real sink (TriggerManagerEntitySink) routes it to TriggerManager.
            ctx.Events.Raise(new EntityEvent
            {
                kind         = EntityEvent.ScriptedEvent,
                entityId     = entity?.entityId,
                gridPosition = tile.gridPosition,
                cause        = "first_kaingin",
            });

            SpawnKainginFire(tile, in ctx);
        }

        private void SpawnKainginFire(Tile tile, in TickContext ctx)
        {
            int fireCount = Random.Range(4, 8);
            List<Tile> targets = TilesInRadius(tile, in ctx, FIRE_RADIUS);

            for (int i = 0; i < fireCount && targets.Count > 0; i++)
            {
                int idx = Random.Range(0, targets.Count);
                Tile target = targets[idx];

                // Don't torch buildings (village/factory). Anything else (bare tile, trees) can ignite.
                bool isBuilding = target.entity != null && target.entity.def != null &&
                                  target.entity.def.category == EntityCategory.Building;
                if (!isBuilding && !target.tv.Contains(TileOverlayType.Firebreak))
                    ctx.Tiles.SpawnById(target, EntityIds.Fire);

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
