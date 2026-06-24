using System.Collections.Generic;
using UnityEngine;
using Habitales.Core;
using ArtificeToolkit.Attributes;

// TileEntitySO — the canonical entity IDENTITY + DATA (arch §5.1). Replaces the
// string `entityType` and the one-C#-class-per-entity model. `entityId` is identity;
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
        [Tooltip("Stable machine ID, e.g. \"narra_mature\". THE identity + save key. Stable-forever / write-once.")]
        public string entityId;
        [BoxGroup("Identity")]
        [Tooltip("Human-facing name, e.g. \"Narra Tree\".")]
        public string displayName;
        [BoxGroup("Identity")]
        [Tooltip("Broad behavioural grouping. Replaces `is VillageEntity` type-checks.")]
        public EntityCategory category;

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

        [BoxGroup("Death")]
        [Tooltip("Condition → outcome (remove / transform-to). First satisfied condition wins.")]
        public List<StatCondition> deathConditions = new List<StatCondition>();

        [BoxGroup("Custom Behaviour (optional)")]
        [Tooltip("Null for pure-data entities; set for bespoke ones (Fire, Village).")]
        public EntityBehaviourHook behaviour;
    }
}
