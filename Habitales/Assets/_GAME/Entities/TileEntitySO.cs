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
    // AuthoredContent — reusable Azi-voice content block, used twice on TileEntitySO: tier1Intro
    // (T1 first-inspect bubble) and tier4JournalEntry (T4 journal card).
    // Resolution rules (enforced by EntityRegistry.ResolveContent, NOT here — this asset has no
    // knowledge of the rest of the entity graph):
    //   Default          -> use `text` directly.
    //   FirstStageInChain -> resolve via EntityRegistry's reverse stage map to the chain's first
    //                        stage, then use THAT stage's own field (which must itself be Default).
    //   FromEntitySO      -> borrow the SAME field from `sourceSO`, ONE hop only (sourceSO's own
    //                        field must be Default — no borrow-chains, no cycles).
    [System.Serializable]
    public class AuthoredContent
    {
        public enum Source { Default, FirstStageInChain, FromEntitySO }

        [Tooltip("Default = author `text` directly below. FirstStageInChain = reuse the FIRST stage " +
                 "of this entity's stage chain (e.g. a mature tree's tier1Intro reuses the seedling's " +
                 "own Default text). FromEntitySO = borrow this SAME field from another SO, one hop only.")]
        public Source source = Source.Default;

        [TextArea(2, 5)]
        [EnableIf(nameof(source), Source.Default)]
        [Tooltip("Used when Source == Default. This is what Azi says / what the Journal entry reads.")]
        public string text;

        [EnableIf(nameof(source), Source.FromEntitySO)]
        [Tooltip("Used when Source == FromEntitySO. Borrows THIS SAME FIELD from the referenced SO — " +
                 "that SO's own field must itself be Default (one hop only; no borrow-chains).")]
        public TileEntitySO sourceSO;
    }

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

        [BoxGroup("Azi Tier Content")]
        [Tooltip("Azi's line the FIRST time the player inspects a tile holding this species (T1 — " +
                 "TriggerManager dedups per entityId per run). Plant SOs must resolve this (OnValidate " +
                 "shouts if not); other categories may leave it Default/empty.")]
        public AuthoredContent tier1Intro;

        [BoxGroup("Azi Tier Content")]
        [Tooltip("Journal app entry logged the first time this species dies with a tracked cause in a " +
                 "check-in window (T4). Same resolution rules as tier1Intro. Plant SOs must resolve this.")]
        public AuthoredContent tier4JournalEntry;

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

            // Azi tier content — Plant SOs must resolve tier1Intro/tier4JournalEntry. Only what's
            // checkable WITHOUT the full entity graph is done here: Default -> non-empty text;
            // FromEntitySO -> sourceSO wired AND its own field is Default non-empty (one hop).
            // FirstStageInChain needs the reverse stage map (EntityRegistry owns that graph) —
            // ambiguous/cyclic chains are loud-failed by EntityRegistry.ValidateAll() at boot
            // instead, not here.
            if (category == EntityCategory.Plant)
            {
                // ValidateAuthoredContent(tier1Intro, nameof(tier1Intro));
                // ValidateAuthoredContent(tier4JournalEntry, nameof(tier4JournalEntry));
            }
        }

        private void ValidateAuthoredContent(AuthoredContent content, string fieldName)
        {
            if (content == null)
            {
                Debug.LogError($"TileEntitySO '{name}': {fieldName} is null — Plant entities must author Azi tier content.", this);
                return;
            }

            switch (content.source)
            {
                case AuthoredContent.Source.Default:
                    if (string.IsNullOrWhiteSpace(content.text))
                        Debug.LogError($"TileEntitySO '{name}': {fieldName} is empty (Source.Default with blank text) — " +
                                       "Plant entities must author this (Azi tier content).", this);
                    break;

                case AuthoredContent.Source.FromEntitySO:
                    if (content.sourceSO == null)
                    {
                        Debug.LogError($"TileEntitySO '{name}': {fieldName} is Source.FromEntitySO but sourceSO is unassigned.", this);
                        break;
                    }
                    AuthoredContent borrowed = fieldName == nameof(tier1Intro)
                        ? content.sourceSO.tier1Intro
                        : content.sourceSO.tier4JournalEntry;
                    if (borrowed == null || borrowed.source != AuthoredContent.Source.Default || string.IsNullOrWhiteSpace(borrowed.text))
                        Debug.LogError($"TileEntitySO '{name}': {fieldName} borrows from '{content.sourceSO.name}' via FromEntitySO, " +
                                       "but that SO's own field is not Default non-empty text — only a ONE-HOP borrow off directly-authored text is allowed.", this);
                    break;

                case AuthoredContent.Source.FirstStageInChain:
                    // Deliberately not validated here — see method doc above. EntityRegistry.ValidateAll()
                    // catches an ambiguous/cyclic chain loudly at boot.
                    break;
            }
        }
    }
}
