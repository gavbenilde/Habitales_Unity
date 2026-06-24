using System.Collections.Generic;
using UnityEngine;
using Habitales.Core;

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
        [Header("Identity")]
        [Tooltip("Stable machine ID, e.g. \"narra_mature\". THE identity + save key. Stable-forever / write-once.")]
        public string entityId;
        [Tooltip("Human-facing name, e.g. \"Narra Tree\".")]
        public string displayName;
        [Tooltip("Broad behavioural grouping. Replaces `is VillageEntity` type-checks.")]
        public EntityCategory category;

        [Header("Visuals")]
        [Tooltip("Typed Sprite so the Inspector renders a real preview (Law 3).")]
        public Sprite tileSprite;
        [Tooltip("Explicit VFX key — no longer derived from a string concat.")]
        public string vfxKey;
        public float billboardScale = 1f;

        [Header("Lifecycle / Stage Chain")]
        [Tooltip("Days before the timed transition fires (0 = no timed transition).")]
        public int promoteAfterDays;
        [Tooltip("Where the timed transition goes. Non-null = transform into it (promote). " +
                 "Null = the entity is REMOVED after promoteAfterDays (e.g. DeadTree decomposes, TrashBio clears).")]
        public TileEntitySO nextStage;
        [Tooltip("Effects applied at the transition moment — on promote OR on timed removal. " +
                 "E.g. seedling +10 VegetationCover on promote; TrashBio -10 BiologicalActivity on clear.")]
        public List<StatChange> transitionEffects = new List<StatChange>();
        [Tooltip("If true, the timed transition ALSO requires promoteWhen to be satisfied.")]
        public bool requirePromoteCondition;
        [Tooltip("Optional gate — when requirePromoteCondition is true, promotion only fires while this holds.")]
        public StatCondition promoteWhen;

        [Header("Daily Effects")]
        [Tooltip("The pure-data common case: stat deltas applied every day this entity lives.")]
        public List<StatChange> dailyEffects = new List<StatChange>();

        [Header("Death")]
        [Tooltip("Condition → outcome (remove / transform-to). First satisfied condition wins.")]
        public List<StatCondition> deathConditions = new List<StatCondition>();

        [Header("Custom Behaviour (optional)")]
        [Tooltip("Null for pure-data entities; set for bespoke ones (Fire, Village).")]
        public EntityBehaviourHook behaviour;
    }
}
