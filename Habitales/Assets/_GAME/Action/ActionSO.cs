using System.Collections.Generic;
using UnityEngine;
using ArtificeToolkit.Attributes;

// ActionSO — the author-facing DATA half of an action (arch §3.4). With the effects list
// it is now fully data-driven: a GenericPlayerAction reads this SO and runs its effects,
// so the boring common case needs NO C# subclass at all (the unique case still writes a
// tiny ActionEffectHook). Authored entirely in the Inspector via the Artifice toolkit,
// mirroring TileEntitySO's attribute-driven layout.
namespace Habitales.Actions
{
    // Author-facing action grouping. Deliberately THREE values (the design's Examine /
    // Intervene / Cleanup tabs) — GenericPlayerAction maps this onto the broader global
    // ActionCategory enum, whose 4th value (Emergency) is dormant and not authorable.
    public enum ActionGroup
    {
        Examine,
        Intervene,
        Cleanup
    }

    [CreateAssetMenu(menuName = "Habitales/Actions/Action Definition")]
    public class ActionSO : ScriptableObject
    {
        [BoxGroup("Identity")]
        [Tooltip("Stable machine ID for this action.")]
        public string actionId;
        [BoxGroup("Identity")]
        [Tooltip("Human-facing name shown on the action card.")]
        public string displayName;
        [BoxGroup("Identity")]
        [Tooltip("Which of the three action groups this belongs to.")]
        public ActionGroup group;
        [BoxGroup("Identity")]
        [TextArea] public string description;

        [BoxGroup("Visuals")]
        [PreviewSprite]
        [Tooltip("The action card's sprite image.")]
        public Sprite icon;

        [BoxGroup("Targeting")]
        [Tooltip("How tiles are picked: Single (one tile), or multi via FloodFill / Adjacent / NonAdjacent.")]
        public SelectionMode selectionMode;

        [BoxGroup("Effects")]
        [Tooltip("What the action does to each selected tile, run in order. Use '+' to Add Effect.")]
        public List<ActionEffect> effects = new List<ActionEffect>();

        [BoxGroup("Cost / Efficiency")]
        [Tooltip("Base days at the minimum crew. days = ceil(BaseDays / sqrt(peoplePerTile / MinPeoplePerTile)), clamped to MinDays.")]
        public int baseDays = 1;
        [BoxGroup("Cost / Efficiency")]
        public int minDays = 1;
        [BoxGroup("Cost / Efficiency")]
        public int minPeoplePerTile = 1;
        [BoxGroup("Cost / Efficiency")]
        public float fatigueMultiplierPerTile = 2.0f;

        [BoxGroup("Boogle")]
        [TextArea] public string lore;
        [BoxGroup("Boogle")]
        [TextArea] public string infoTooltip;
        [BoxGroup("Boogle")]
        public List<Sprite> supplementaryImages = new List<Sprite>();
    }
}
