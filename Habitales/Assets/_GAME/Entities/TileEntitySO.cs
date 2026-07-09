using System.Collections.Generic;
using UnityEngine;
using Habitales.Core;
using ArtificeToolkit.Attributes;

// TileEntitySO — the canonical entity IDENTITY + DATA (arch §5.1). Replaces the
// string `entityType` and the one-C#-class-per-entity model. `displayName` is the
// only authored identity; `EntityId` is a code-derived slug of it (2026-07-07) —
// the runtime is one generic TileEntity (Phase 4) that reads this SO and delegates.
//
// One SO per stage, chained via `nextStage`. DeadTree/Stump are SHARED generic assets
// reused across every species (composability win, S2).
//
// INVARIANT (arch §5.3 / §5.4): this is shared/immutable definition data. NO per-instance
// mutable state lives here — runtime flags (health, daysExisting, isInspected, …) live on
// the runtime TileEntity only.
namespace Habitales.Entities
{
    [CreateAssetMenu(menuName = "Habitales/Entities/Entity Definition")]
    public class TileEntitySO : ScriptableObject
    {
        [BoxGroup("Identity")]
        [Tooltip("Human-facing name, e.g. \"Narra Tree\". THE only author-facing handle (design " +
                 "decision 2026-07-07) — the machine id is derived from this, never authored directly.")]
        public string displayName;
        [BoxGroup("Identity")]
        [Tooltip("Broad behavioural grouping. Replaces `is VillageEntity` type-checks.")]
        public EntityCategory category;

        /// <summary>
        /// Stable machine id, derived from <see cref="displayName"/> via <see cref="GenerateId"/>.
        /// THE identity + save key — but code-derived, never hand-authored (design decision
        /// 2026-07-07). Renaming displayName changes this id; there is no persistent save data
        /// keyed by entity ids in the prototype, so that tradeoff is accepted.
        /// </summary>
        public string EntityId => GenerateId(displayName);

        /// <summary>
        /// Deterministic displayName → id slug: trim, lowercase, collapse any run of
        /// non-alphanumeric characters to a single underscore, trim leading/trailing underscores.
        /// Shared by TileEntitySO.EntityId and EntityRegistry lookups so a display name and its
        /// derived id always resolve to the same entry.
        /// </summary>
        public static string GenerateId(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return string.Empty;
            string lowered = displayName.Trim().ToLowerInvariant();
            string collapsed = System.Text.RegularExpressions.Regex.Replace(lowered, "[^a-z0-9]+", "_");
            return collapsed.Trim('_');
        }

        [BoxGroup("Visuals")]
        [PreviewSprite]
        [Tooltip("Typed Sprite so the Inspector renders a real preview (Law 3).")]
        public Sprite tileSprite;
        [BoxGroup("Visuals")]
        [Tooltip("Explicit VFX key — no longer derived from a string concat.")]
        public string vfxKey;
        [BoxGroup("Visuals")]
        public float billboardScale = 1f;

        [BoxGroup("Lifecycle / Stage Chain")]
        [Tooltip("Days before the timed transition fires (0 = no timed transition).")]
        public int promoteAfterDays;
        [BoxGroup("Lifecycle / Stage Chain")]
        [Tooltip("Where the timed transition goes. Non-null = transform into it (promote). " +
                 "Null = the entity is REMOVED after promoteAfterDays (e.g. DeadTree decomposes, TrashBio clears).")]
        public TileEntitySO nextStage;
        [BoxGroup("Lifecycle / Stage Chain")]
        [Tooltip("Effects applied at the transition moment — on promote OR on timed removal. " +
                 "E.g. seedling +10 VegetationCover on promote; TrashBio -10 BiologicalActivity on clear.")]
        public List<StatChange> transitionEffects = new List<StatChange>();
        [BoxGroup("Lifecycle / Stage Chain")]
        [Tooltip("If true, the timed transition ALSO requires promoteWhen to be satisfied.")]
        public bool requirePromoteCondition;
        [BoxGroup("Lifecycle / Stage Chain")]
        [EnableIf(nameof(requirePromoteCondition))]
        [Tooltip("Pure stat gate — promotion only fires while this holds. The destination is `nextStage`, " +
                 "not this gate. Only shown when Require Promote Condition is ticked.")]
        public StatGate promoteWhen;

        [BoxGroup("Daily Effects")]
        [EnableIf(nameof(category), EntityCategory.Plant)]
        [Tooltip("Plants only: per-day delta for each of the 8 writable substats, as sliders (-10..+10).")]
        public DailyStatDeltas plantDailyDeltas;
        [BoxGroup("Daily Effects")]
        [EnableIf(nameof(category), EntityCategory.Building, EntityCategory.Hazard, EntityCategory.Debris)]
        [Tooltip("Non-plant entities: stat deltas applied every day this entity lives.")]
        public List<StatChange> dailyEffects = new List<StatChange>();

        [BoxGroup("Weather Resilience")]
        [EnableIf(nameof(category), EntityCategory.Plant)]
        [UnityEngine.Range(0f, 1f)] // fully qualified — ArtificeToolkit ships its own RangeAttribute
        [Tooltip("Plants only: 0 = fully exposed, 1 = immune. Scales down drought growth-stall/regression " +
                 "AND the drought wither roll. Trees run high (~0.6+), tender crops low (~0.1).")]
        public float droughtResistance;
        [BoxGroup("Weather Resilience")]
        [EnableIf(nameof(category), EntityCategory.Plant)]
        [UnityEngine.Range(0f, 1f)] // fully qualified — ArtificeToolkit ships its own RangeAttribute
        [Tooltip("Plants only: 0 = fully exposed, 1 = immune. Scales down deluge growth-stall/regression " +
                 "AND the rain/storm kill roll (which is deadlier than drought). Trees high, crops low.")]
        public float floodResistance;

        [BoxGroup("Death")]
        [Tooltip("Condition → outcome (remove / transform-to). First satisfied condition wins.")]
        public List<StatCondition> deathConditions = new List<StatCondition>();

        [BoxGroup("Custom Behaviour (optional)")]
        [Tooltip("Null for pure-data entities; set for bespoke ones (Fire, Village).")]
        public EntityBehaviourHook behaviour;

        // Per-asset loud-fail validation (arch Law 3). Duplicate/registry-wide checks are NOT
        // done here — EntityRegistry.ValidateAll() owns those at boot. `nextStage == null` is
        // deliberately NOT flagged: it's meaningful (entity is REMOVED after promoteAfterDays).
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(displayName))
                Debug.LogError($"TileEntitySO '{name}': displayName is blank — it is the only author-facing " +
                                "handle and the source of the derived EntityId (save key). It must be set.", this);

            if (promoteAfterDays < 0)
                Debug.LogWarning($"TileEntitySO '{name}': promoteAfterDays is negative ({promoteAfterDays}).", this);

            if (category == EntityCategory.Plant && dailyEffects.Count > 0)
                Debug.LogWarning($"TileEntitySO '{name}': category is Plant but the legacy dailyEffects list is non-empty — " +
                                 "plants must use plantDailyDeltas; the runtime applies both, so leftovers double-apply.", this);

            if (category != EntityCategory.Plant &&
                (plantDailyDeltas.nutrientBalance != 0f    || plantDailyDeltas.soilOrganicMatter != 0f ||
                 plantDailyDeltas.soilStructure != 0f      || plantDailyDeltas.biologicalActivity != 0f ||
                 plantDailyDeltas.waterDynamics != 0f      || plantDailyDeltas.erosionResistance != 0f ||
                 plantDailyDeltas.vegetationCover != 0f    || plantDailyDeltas.contamination != 0f))
                Debug.LogWarning($"TileEntitySO '{name}': category is not Plant but plantDailyDeltas has nonzero fields — " +
                                 "non-plants must use dailyEffects.", this);
        }
    }
}
