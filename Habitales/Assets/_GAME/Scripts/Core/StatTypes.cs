using System;
using System.Collections.Generic;
using Habitales.Entities;

// Shared stat-addressing + effect/condition data types (arch §3.5).
// Consumed by the entity system (daily effects, death, promotion) AND GenericSpawnAction.
// Two DISTINCT named types — never one overloaded struct — so each consumer gets its own shape.
namespace Habitales.Core
{
    // The 8 TileStats fields + the derived reads. One shared enum so every consumer
    // addresses stats identically.
    public enum TargetStat
    {
        NutrientBalance,
        SoilOrganicMatter,
        SoilStructure,
        BiologicalActivity,
        WaterDynamics,
        ErosionResistance,
        VegetationCover,
        Contamination,
        SoilComposite   // read-only derived; valid in a StatCondition, INVALID as a StatChange target
    }

    public enum Comparator { LessThan, LessOrEqual, GreaterThan, GreaterOrEqual }

    // Outcome of a satisfied condition. TransformTo carries the target definition.
    public enum OutcomeKind { None, Remove, TransformTo }

    // Daily effects, spawn-action effects.
    [Serializable]
    public struct StatChange
    {
        public TargetStat stat;
        public float      delta;   // applied DIRECTLY to the one named stat, clamped 0–100
                                   // (NOT routed through ModifyTileStats' ÷6 soilDelta distribution) — arch §3.5
    }

    // Death conditions, promotion gates.
    [Serializable]
    public struct StatCondition
    {
        public TargetStat       stat;
        public Comparator       comparator;
        public float            threshold;
        public OutcomeKind      outcome;          // None for a pure promotion gate
        public TileEntitySO     transformTarget;  // used only when outcome == TransformTo
        public List<StatChange> onSatisfied;      // optional effects applied when the condition fires
                                                  // (e.g. Sapling returns +10 SOM on death). Applied before the outcome.
    }
}
