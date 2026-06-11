using UnityEngine;

// FireBehaviourHook — bespoke Fire behaviour ported from the legacy FireEntity (arch §5.3 / 4c).
// REPLACES the generic lifecycle: fire has no dailyEffects/death/promote data — it owns its tick.
// Per-instance state (burn duration, vegetation at ignition) lives on the runtime entity's
// hookState, never on this shared SO. Weather is READ from the threaded TickContext (Law 1 / S1),
// not from WeatherManager.Instance.
//
// Author one asset (Habitales/Entities/Behaviours/Fire) and assign it to the `fire` TileEntitySO's
// `behaviour` field.
namespace Habitales.Entities
{
    [CreateAssetMenu(menuName = "Habitales/Entities/Behaviours/Fire")]
    public class FireBehaviourHook : EntityBehaviourHook
    {
        private const float BASE_DAMAGE        = 8f;
        private const int   MIN_SPREAD_DAYS    = 3;
        private const float PEAK_SPREAD_CHANCE = 0.2f;

        // Fire is self-governing — skip the data-driven dailyEffects/death/promotion pipeline.
        public override bool ReplacesGenericLifecycle => true;

        public override void OnDailyUpdate(Tile tile, TileEntity entity, in TickContext ctx)
        {
            var ge = entity as GenericTileEntity;
            if (ge == null) return;
            var state = ge.hookState;

            // Capture vegetation at ignition on the first tick (normalizes the spread ramp).
            if (!state.ContainsKey("startVeg"))
                state["startVeg"] = Mathf.Max(1f, tile.stats.vegetationCover);

            float totalDamage = BASE_DAMAGE + ctx.FireBonusDamage;
            tile.stats.vegetationCover    = Mathf.Clamp(tile.stats.vegetationCover    - totalDamage, 0f, 100f);
            tile.stats.nutrientBalance    = Mathf.Clamp(tile.stats.nutrientBalance    - totalDamage, 0f, 100f);
            tile.stats.biologicalActivity = Mathf.Clamp(tile.stats.biologicalActivity - totalDamage, 0f, 100f);

            float duration = (state.TryGetValue("duration", out var d) ? d : 0f) + 1f;
            state["duration"] = duration;

            TrySpread(tile, in ctx, state["startVeg"], duration);

            if (tile.stats.vegetationCover <= 0f)
                ctx.Tiles.RemoveEntity(tile);
        }

        private void TrySpread(Tile tile, in TickContext ctx, float startVeg, float duration)
        {
            // Too fresh to throw embers.
            if (duration < MIN_SPREAD_DAYS) return;

            // Ramp: 0 at MIN_SPREAD_DAYS, reaching 1.0 at the tile's natural burn-out duration.
            float maxDuration    = startVeg / BASE_DAMAGE;
            float spreadProgress = Mathf.Clamp01((duration - MIN_SPREAD_DAYS) / Mathf.Max(1f, maxDuration - MIN_SPREAD_DAYS));
            float spreadChance   = PEAK_SPREAD_CHANCE * spreadProgress * ctx.FireSpreadMultiplier;

            foreach (Tile neighbor in ctx.Tiles.GetAdjacentTiles(tile))
            {
                if (neighbor.tv.Contains(TileOverlayType.Firebreak)) continue;
                if (neighbor.entity != null) continue;
                if (Random.value < spreadChance)
                    ctx.Tiles.SpawnById(neighbor, "fire");
            }
        }
    }
}
