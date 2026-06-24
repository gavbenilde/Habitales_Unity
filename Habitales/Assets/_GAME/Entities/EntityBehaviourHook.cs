using UnityEngine;

// EntityBehaviourHook — the escape hatch for bespoke entity behaviour (arch §5.3).
// Pure-data entities need none; bespoke ones (Fire, Village) reference a
// concrete subclass here, kept small enough that a half-programmer writes one confidently.
//
// The hook is a ScriptableObject (shared, immutable) — it holds NO per-instance state.
// Any per-instance runtime data a hook needs (fireDuration, startingVegetation, …) lives on
// the runtime entity, never on this asset (arch §5.3 / §5.4).
namespace Habitales.Entities
{
    public abstract class EntityBehaviourHook : ScriptableObject
    {
        // Per-day bespoke behaviour. `entity` is the runtime instance this hook is attached to
        // (cast to the concrete runtime type as needed). Receives the decoupled TickContext —
        // never grab singletons here (S1).
        public abstract void OnDailyUpdate(Tile tile, TileEntity entity, in TickContext ctx);

        // If true, the generic data-driven lifecycle (dailyEffects → death → promotion) is
        // SKIPPED and this hook owns the entity's behaviour entirely (e.g. Fire). Default false
        // = the hook SUPPLEMENTS the generic lifecycle, running after it (e.g. Village). arch §5.2.
        public virtual bool ReplacesGenericLifecycle => false;
    }
}
