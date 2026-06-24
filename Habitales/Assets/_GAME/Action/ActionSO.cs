using System.Collections.Generic;
using UnityEngine;

// ActionSO — the author-facing DATA half of an action (arch §3.4). The boring common
// case is pure data; the unique case is a tiny PlayerAction subclass that references
// its ActionSO for all metadata. Replaces ActionIconConfig + the hardcoded per-subclass
// fields. Authored entirely in the Inspector.
//
// Reuses the existing global enums ActionCategory (Examine/Intervene/Emergency/Cleanup)
// and SelectionMode (FloodFill/Adjacent/NonAdjacent); those move into Habitales.Actions
// when PlayerAction is ported (Phase 5).
namespace Habitales.Actions
{
    [CreateAssetMenu(menuName = "Habitales/Actions/Action Definition")]
    public class ActionSO : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable machine ID for this action.")]
        public string actionId;
        public string displayName;
        [TextArea] public string description;
        public Sprite icon;
        public ActionCategory category;
        public SelectionMode selectionMode;

        [Header("Cost / Efficiency")]
        [Tooltip("Base days at the minimum crew. days = ceil(BaseDays / sqrt(peoplePerTile / MinPeoplePerTile)), clamped to MinDays.")]
        public int baseDays = 1;
        public int minDays = 1;
        public int minPeoplePerTile = 1;
        public float fatigueMultiplierPerTile = 2.0f;

        [Header("Info / Lore")]
        [TextArea] public string infoTooltip;
        public List<Sprite> supplementaryImages = new List<Sprite>();
        [TextArea] public string lore;

        [Header("Variants")]
        [Tooltip("Optional — groups variant actions (e.g. CoverCrop legume/grass/phyto) under one UI entry.")]
        public string variantGroupName;
    }
}
