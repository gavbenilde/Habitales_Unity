using System.Collections.Generic;
using UnityEngine;
using Habitales.Core;

// FactoryBehaviorHook — bespoke Factory behaviour (arch §5.3). SUPPLEMENTS the generic lifecycle
// (factory.asset has no dailyEffects/promotion, so the data pipeline is a no-op and this hook runs
// after it). Each day it rolls a small chance to litter a cluster of trash on EMPTY tiles around
// the factory, and announces the first cluster as an interruption.
//
// Trash lifetime is authored on the trash SOs, not here: trash_bio* promote after 7 days with no
// nextStage (GenericTileEntity step 3 → RemoveEntity, i.e. it decays away), trash_nonbio* after 100.
//
// Author one asset (Habitales/Entities/Behaviours/Factory) and assign it to the `factory`
// TileEntitySO's `behaviour` field.
namespace Habitales.Entities
{
    [CreateAssetMenu(menuName = "Habitales/Entities/Behaviours/Factory")]
    public class FactoryBehaviorHook : EntityBehaviourHook
    {
        private static readonly string[] TRASH_IDS =
        {
            "trash_bio_1",
            "trash_bio_2",
            "trash_bio_3",
            "trash_nonbio_1",
            "trash_nonbio_2",
            "trash_nonbio_3",
        };
        
        private const float TRASH_DAILY_CHANCE = 0.215f;
        private const int   TRASH_RADIUS          = 4;

        // hookState key for the once-per-factory "first trash" interruption (per-instance scratch
        // on the runtime entity — never on this shared SO, arch §5.3/§5.4).
        private const string ANNOUNCED_KEY = "trash_announced";

        public override void OnDailyUpdate(Tile tile, TileEntity entity, in TickContext ctx)
        {
            if (Random.value >= TRASH_DAILY_CHANCE) return;

            // Spawn FIRST, announce second: a roll that lands on nothing (every tile in radius
            // already occupied) is not a story beat, and "first_trash" must mean the first trash
            // this factory actually produced.
            if (SpawnTrash(tile, in ctx) == 0) return;

            var runtime = entity as GenericTileEntity;
            if (runtime != null)
            {
                if (runtime.hookState.ContainsKey(ANNOUNCED_KEY)) return;   // already told that story
                runtime.hookState[ANNOUNCED_KEY] = 1f;
            }

            // Fire the "first trash appeared" interruption through the meaning-event sink — no
            // singleton grab (S1). The real sink (EventManagerEntitySink) routes it to EventManager.
            ctx.Events.Raise(new EntityEvent
            {
                kind         = EntityEvent.ScriptedEvent,
                entityId     = entity?.entityId,
                gridPosition = tile.gridPosition,
                cause        = "first_trash",
            });
        }

        /// <summary>Scatters this roll's trash on EMPTY tiles only; returns how many landed.</summary>
        private int SpawnTrash(Tile tile, in TickContext ctx)
        {
            int trashCount = Random.Range(4, 8);
            List<Tile> targets = TilesInRadius(tile, in ctx, TRASH_RADIUS);
            int placed = 0;

            for (int i = 0; i < trashCount && targets.Count > 0; i++)
            {
                int idx = Random.Range(0, targets.Count);
                Tile target = targets[idx];

                // Never squat a living tile — trees, crops, buildings, fire and older trash all
                // hold their tile. (SpawnById would otherwise silently REPLACE the occupant:
                // TileManager.SpawnFromDef pre-clears whatever is there.)
                if (target.entity == null)
                {
                    string trashId = TRASH_IDS[Random.Range(0, TRASH_IDS.Length)];
                    ctx.Tiles.SpawnById(target, trashId);
                    placed++;
                }

                targets.RemoveAt(idx);
            }

            return placed;
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
