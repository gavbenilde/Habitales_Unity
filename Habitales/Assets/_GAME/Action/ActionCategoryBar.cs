using System;
using UnityEngine;
using UnityEngine.UI;

namespace Habitales.UI.Actions
{
    /// <summary>
    /// Passive view: owns the four category tab buttons and fires OnCategorySelected when one
    /// is clicked. The controller subscribes in OnEnable and calls SetActiveCategory to reflect
    /// state. All rendering decisions stay in the controller; this view only raises events and
    /// provides rect targets for coach-marks.
    /// </summary>
    public class ActionCategoryBar : MonoBehaviour
    {
        [SerializeField] private Button examineTab;
        [SerializeField] private Button interveneTab;
        [SerializeField] private Button cleanupTab;

        /// <summary>Raised when the player clicks a category tab (Law-2: on the click, not on mutation).</summary>
        public event Action<ActionCategory> OnCategorySelected;

        private ActionCategory? _activeCategory;

        void OnEnable()
        {
            // Emergency is retired — the tab is gone entirely (no field, no handler).
            if (examineTab   != null) examineTab.onClick.AddListener(  () => OnCategorySelected?.Invoke(ActionCategory.Examine));
            if (interveneTab != null) interveneTab.onClick.AddListener(() => OnCategorySelected?.Invoke(ActionCategory.Intervene));
            if (cleanupTab   != null) cleanupTab.onClick.AddListener(  () => OnCategorySelected?.Invoke(ActionCategory.Cleanup));
        }

        void OnDisable()
        {
            if (examineTab   != null) examineTab.onClick.RemoveAllListeners();
            if (interveneTab != null) interveneTab.onClick.RemoveAllListeners();
            if (cleanupTab   != null) cleanupTab.onClick.RemoveAllListeners();
        }

        /// <summary>
        /// Stores the active category for state tracking. Visual selection feedback can be
        /// added here later; the method is a no-op visually for now but is part of the contract.
        /// </summary>
        public void SetActiveCategory(ActionCategory? category)
        {
            _activeCategory = category;
            // Future: highlight the matching tab button here.
        }

        /// <summary>
        /// Returns the RectTransform of the tab corresponding to <paramref name="category"/>.
        /// Used by the controller to resolve coach-mark targets. Mirrors the old TabForCategory.
        /// </summary>
        public RectTransform GetTabRect(ActionCategory category)
        {
            Button tab;
            switch (category)
            {
                case ActionCategory.Examine:   tab = examineTab;   break;
                case ActionCategory.Intervene: tab = interveneTab; break;
                case ActionCategory.Cleanup:   tab = cleanupTab;   break;
                // Emergency (retired) falls through to the default coach-mark target.
                default:                       tab = interveneTab; break;
            }
            return tab != null ? tab.transform as RectTransform : null;
        }
    }
}
