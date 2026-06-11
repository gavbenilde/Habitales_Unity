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

        public TickContext(TileManager tiles, float fireBonusDamage, float fireSpreadMultiplier, IEntityEventSink events)
        {
            Tiles                = tiles;
            FireBonusDamage      = fireBonusDamage;
            FireSpreadMultiplier = fireSpreadMultiplier;
            Events               = events ?? NullEntityEventSink.Instance;
        }
    }
}
