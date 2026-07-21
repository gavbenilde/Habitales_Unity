using System.Collections.Generic;
using UnityEngine;
using Habitales.Core;

// GenericTileEntity — the single generic runtime entity (arch §5.1). Reads its TileEntitySO
// `def` and delegates: applies dailyEffects per-stat, checks deathConditions, handles the timed
// transition (promote to nextStage, or remove if nextStage is null), then runs an optional
// behaviour hook. Replaces the one-class-per-entity hand-coded model.
//
// Per-instance runtime state lives HERE, never on the shared SO (arch §5.3 / §5.4).
//
// Extends the (legacy) global TileEntity so it is assignable to Tile.entity during the
// migration. Both ticks coexist until Phase 4d deletes the hand-coded subclasses.
namespace Habitales.Entities
{
    public class GenericTileEntity : TileEntity
    {
        public int daysExisting = 0;

        // Per-instance scratch for behaviour hooks (fireDuration, startingVegetation, isInspected…).
        // Keeps mutable per-instance state off the shared SO.
        public readonly Dictionary<string, float> hookState = new Dictionary<string, float>();

        public GenericTileEntity(TileEntitySO definition)
        {
            def        = definition;
            entityId   = definition != null ? definition.EntityId : null;
            health     = 100f;
        }

        public override void OnDailyUpdate(Tile tile, in TickContext ctx)
        {
            if (def == null) return;
            daysExisting += GrowthStepFor(tile, in ctx);
            if (daysExisting < 0) daysExisting = 0;

            bool hookReplaces = def.behaviour != null && def.behaviour.ReplacesGenericLifecycle;

            if (!hookReplaces)
            {
                // 1. Daily effects (per-stat, direct, clamped 0–100). Non-plant entities use the
                //    StatChange list; Plants use the 8 substat sliders. The other is zero/empty,
                //    so applying both is correct with no category branch.
                ApplyEffects(tile, def.dailyEffects);
                ApplyDailyDeltas(tile, def.plantDailyDeltas);

                // 2. Death conditions — first satisfied wins; apply its bonus, then its outcome.
                if (def.deathConditions != null)
                {
                    foreach (var cond in def.deathConditions)
                    {
                        if (Satisfied(tile, cond))
                        {
                            ApplyEffects(tile, cond.onSatisfied);   // e.g. Sapling +10 SOM on death
                            ResolveOutcome(tile, ctx, cond);
                            return;                                  // entity transformed/removed — stop
                        }
                    }
                }

                // 3. Timed transition — promote to nextStage, or remove when nextStage is null.
                if (def.promoteAfterDays > 0 && daysExisting >= def.promoteAfterDays && PromoteGateOpen(tile))
                {
                    ApplyEffects(tile, def.transitionEffects);       // e.g. Seedling +10 VC on promote
                    if (def.nextStage != null) ctx.Tiles.ReplaceWithSO(tile, def.nextStage);
                    else                       ctx.Tiles.RemoveEntity(tile);
                    return;
                }
            }

            // 4. Behaviour hook (supplements the generic lifecycle, or replaces it when flagged).
            if (def.behaviour != null)
                def.behaviour.OnDailyUpdate(tile, this, in ctx);
        }

        private bool PromoteGateOpen(Tile tile)
            => !def.requirePromoteCondition || Satisfied(tile, def.promoteWhen);

        // An active drought/deluge can STALL (+0) or REGRESS (−1) a plant's growth instead of the
        // normal +1 day. Stress = spell depth × tile exposure (1 − VegCover/100) × species
        // vulnerability (1 − resistance): a lush tile shelters its plant, a sturdy species (tree)
        // shrugs off what stalls a tender crop, and both bite harder the longer the spell runs.
        // Non-plants (buildings, debris timers, hazards) always advance normally.
        private int GrowthStepFor(Tile tile, in TickContext ctx)
        {
            if (def.category != EntityCategory.Plant) return 1;

            int spellDepth = ctx.DroughtStreakDays > 0 ? ctx.DroughtStreakDays : ctx.DelugeStreakDays;
            if (spellDepth <= 0) return 1;

            float resistance = ctx.DroughtStreakDays > 0 ? def.droughtResistance : def.floodResistance;
            float exposure   = 1f - tile.stats.vegetationCover / 100f;
            float stress     = spellDepth * exposure * (1f - resistance);

            if (stress >= ctx.GrowthRegressPoint) return -1;
            if (stress >= ctx.GrowthStallPoint)   return 0;
            return 1;
        }

        // Plant daily sliders — same per-stat direct+clamp path as ApplyOne, applied to the 8
        // writable substats. All-zero for non-plant defs (harmless no-op).
        private static void ApplyDailyDeltas(Tile tile, in DailyStatDeltas d)
        {
            var s = tile.stats;
            s.nutrientBalance    = Clamp(s.nutrientBalance    + d.nutrientBalance);
            s.soilOrganicMatter  = Clamp(s.soilOrganicMatter  + d.soilOrganicMatter);
            s.soilStructure      = Clamp(s.soilStructure      + d.soilStructure);
            s.biologicalActivity = Clamp(s.biologicalActivity + d.biologicalActivity);
            s.waterDynamics      = Clamp(s.waterDynamics      + d.waterDynamics);
            s.erosionResistance  = Clamp(s.erosionResistance  + d.erosionResistance);
            s.vegetationCover    = Clamp(s.vegetationCover    + d.vegetationCover);
            s.contamination      = Clamp(s.contamination      + d.contamination);
        }

        // A satisfied death condition IS a death — cause "environment" (the tile's own stats
        // killed it, as opposed to a weather kill roll). Remove uses TileManager.RemoveEntity's
        // cause param; TransformTo uses KillAndTransform so OnEntityDied fires BEFORE the swap
        // into transformTarget (DeadTree, etc.) — the same "fire the death event first, then
        // transform" mechanics the weather-death debris path reuses (TileManager.ApplyWeatherStress).
        private void ResolveOutcome(Tile tile, in TickContext ctx, StatCondition c)
        {
            switch (c.outcome)
            {
                case OutcomeKind.Remove:
                    ctx.Tiles.RemoveEntity(tile, cause: "environment");
                    break;
                case OutcomeKind.TransformTo:
                    if (c.transformTarget != null) ctx.Tiles.KillAndTransform(tile, c.transformTarget, "environment");
                    else { Debug.LogError($"[{entityId}] death outcome=TransformTo but transformTarget is null."); ctx.Tiles.RemoveEntity(tile, cause: "environment"); }
                    break;
                case OutcomeKind.None:
                    break; // condition with no outcome — used as a pure gate; no-op here
            }
        }

        // ── Stat helpers (per-stat direct application, matching the hand-coded entities) ──────
        private static void ApplyEffects(Tile tile, List<StatChange> effects)
        {
            if (effects == null) return;
            foreach (var e in effects) ApplyOne(tile, e);
        }

        private static void ApplyOne(Tile tile, StatChange c)
        {
            var s = tile.stats;
            switch (c.stat)
            {
                case TargetStat.NutrientBalance:    s.nutrientBalance    = Clamp(s.nutrientBalance    + c.delta); break;
                case TargetStat.SoilOrganicMatter:  s.soilOrganicMatter  = Clamp(s.soilOrganicMatter  + c.delta); break;
                case TargetStat.SoilStructure:      s.soilStructure      = Clamp(s.soilStructure      + c.delta); break;
                case TargetStat.BiologicalActivity: s.biologicalActivity = Clamp(s.biologicalActivity + c.delta); break;
                case TargetStat.WaterDynamics:      s.waterDynamics      = Clamp(s.waterDynamics      + c.delta); break;
                case TargetStat.ErosionResistance:  s.erosionResistance  = Clamp(s.erosionResistance  + c.delta); break;
                case TargetStat.VegetationCover:    s.vegetationCover    = Clamp(s.vegetationCover    + c.delta); break;
                case TargetStat.Contamination:      s.contamination      = Clamp(s.contamination      + c.delta); break;
                case TargetStat.SoilComposite:
                    Debug.LogError($"[{tile.entity?.entityId}] StatChange targets SoilComposite (derived, not writable). Ignored.");
                    break;
            }
        }

        private static float GetStat(Tile tile, TargetStat stat)
        {
            var s = tile.stats;
            switch (stat)
            {
                case TargetStat.NutrientBalance:    return s.nutrientBalance;
                case TargetStat.SoilOrganicMatter:  return s.soilOrganicMatter;
                case TargetStat.SoilStructure:      return s.soilStructure;
                case TargetStat.BiologicalActivity: return s.biologicalActivity;
                case TargetStat.WaterDynamics:      return s.waterDynamics;
                case TargetStat.ErosionResistance:  return s.erosionResistance;
                case TargetStat.VegetationCover:    return s.vegetationCover;
                case TargetStat.Contamination:      return s.contamination;
                case TargetStat.SoilComposite:      return s.soilComposite;   // derived read — valid in conditions
                default:                            return 0f;
            }
        }

        // Death condition and promotion gate share the same threshold test — one core, two shapes.
        private static bool Satisfied(Tile tile, StatCondition c)
            => Satisfied(tile, c.stat, c.comparator, c.threshold);

        private static bool Satisfied(Tile tile, in StatGate g)
            => Satisfied(tile, g.stat, g.comparator, g.threshold);

        private static bool Satisfied(Tile tile, TargetStat stat, Comparator comparator, float threshold)
        {
            float v = GetStat(tile, stat);
            switch (comparator)
            {
                case Comparator.LessThan:       return v <  threshold;
                case Comparator.LessOrEqual:    return v <= threshold;
                case Comparator.GreaterThan:    return v >  threshold;
                case Comparator.GreaterOrEqual: return v >= threshold;
                default:                        return false;
            }
        }

        private static float Clamp(float v) => Mathf.Clamp(v, 0f, 100f);
    }
}
