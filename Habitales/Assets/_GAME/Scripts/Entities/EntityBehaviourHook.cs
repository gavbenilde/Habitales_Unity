using UnityEngine;

// EntityBehaviourHook — the escape hatch for bespoke entity behaviour (arch §5.3).
// Pure-data entities need none; bespoke ones (Fire, Village, CoverCrop) reference a
// concrete subclass here, kept small enough that a half-programmer writes one confidently.
//
// PHASE 1: base type only, so TileEntitySO can hold a `behaviour` reference.
// PHASE 4: gains the daily hook once the runtime TileEntity + TickContext exist:
//     public abstract void OnDailyUpdate(Tile tile, TileEntity entity, in TickContext ctx);
//   The orchestrator states per-hook whether it SUPPLEMENTS or REPLACES the generic
//   data-driven lifecycle evaluation (Fire replaces; Village supplements) — arch §5.2.
namespace Habitales.Entities
{
    public abstract class EntityBehaviourHook : ScriptableObject
    {
    }
}
