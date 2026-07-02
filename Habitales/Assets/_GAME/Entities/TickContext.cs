using Habitales.Core;

// TickContext — the per-day environment handed to every entity's OnDailyUpdate (arch §5.2).
// Entities READ what the day resolved (weather-derived values, the tile manager, the event
// sink) instead of grabbing singletons mid-tick (S1). RunManager assembles this ONCE per day
// (reading WeatherManager via Law 1) and passes it down through UpdateAllEntities.
//
// readonly struct + `in` parameter = passed by reference, zero alloc, no accidental mutation.
namespace Habitales.Entities
{
    public readonly struct TickContext
    {
        public readonly TileManager      Tiles;               // tile queries + ModifyTileStats + spawn/transform/remove
        public readonly float            FireBonusDamage;     // resolved by RunManager from WeatherManager (Law 1)
        public readonly float            FireSpreadMultiplier;
        public readonly IEntityEventSink Events;              // meaning-event sink (never a global grab)

        // ── Weather spell stress (resolved by RunManager from WeatherManager + TileManager) ──
        // How deep into an ACTIVE drought/deluge the day is: 0 = not active, 1 = the day the
        // spell crossed the streak threshold, +1 per active day after. At most one is non-zero
        // (drought needs a Sunny day, deluge a Rainy/Stormy one). Plants combine this with their
        // tile's VegetationCover and their species resistance to decide growth stall/regression.
        public readonly int   DroughtStreakDays;
        public readonly int   DelugeStreakDays;
        public readonly float GrowthStallPoint;    // stress ≥ this → plant growth pauses
        public readonly float GrowthRegressPoint;  // stress ≥ this → plant growth rolls backwards

        public TickContext(TileManager tiles, float fireBonusDamage, float fireSpreadMultiplier, IEntityEventSink events,
                           int droughtStreakDays = 0, int delugeStreakDays = 0,
                           float growthStallPoint = float.MaxValue, float growthRegressPoint = float.MaxValue)
        {
            Tiles                = tiles;
            FireBonusDamage      = fireBonusDamage;
            FireSpreadMultiplier = fireSpreadMultiplier;
            Events               = events ?? NullEntityEventSink.Instance;
            DroughtStreakDays    = droughtStreakDays;
            DelugeStreakDays     = delugeStreakDays;
            GrowthStallPoint     = growthStallPoint;
            GrowthRegressPoint   = growthRegressPoint;
        }
    }
}
