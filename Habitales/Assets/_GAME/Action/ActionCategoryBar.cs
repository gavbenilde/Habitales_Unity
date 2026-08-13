using System;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

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
        [Tooltip("The container holding the category tabs, toggled off while the action strip is open. " +
                 "Falls back to this GameObject if unwired (wire the CategoryTabs grid here).")]
        [SerializeField] private GameObject categoryRoot;
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
            if (examineTab   != null) examineTab.onClick.AddListener(() => OnAnyButtonClicked(examineTab));
            if (examineTab   != null) examineTab.interactable = false;
            if (interveneTab != null) interveneTab.onClick.AddListener(() => OnCategorySelected?.Invoke(ActionCategory.Intervene));
            if (interveneTab   != null) interveneTab.onClick.AddListener(() => OnAnyButtonClicked(interveneTab));
            if (interveneTab != null) interveneTab.interactable = true;
            if (cleanupTab   != null) cleanupTab.onClick.AddListener(  () => OnCategorySelected?.Invoke(ActionCategory.Cleanup));
            if (cleanupTab   != null) cleanupTab.onClick.AddListener(() => OnAnyButtonClicked(cleanupTab));
            if (cleanupTab   != null) cleanupTab.interactable = true;
        }

        void OnDisable()
        {
            if (examineTab   != null) examineTab.onClick.RemoveAllListeners();
            if (interveneTab != null) interveneTab.onClick.RemoveAllListeners();
            if (cleanupTab   != null) cleanupTab.onClick.RemoveAllListeners();
        }

        /// <summary>
        /// Shows or hides the category tab container. The controller hides the tabs while the
        /// action strip is open (the strip's Back button re-shows them), so the two never overlap.
        /// Toggles <see cref="categoryRoot"/> if wired, otherwise this GameObject.
        /// </summary>
        public void SetVisible(bool visible)
        {
            GameObject target = categoryRoot != null ? categoryRoot : gameObject;
            target.SetActive(visible);
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

        public void DisableIntervene()
        {
            interveneTab.interactable = false;
        }
        
        public void DisableCleanup()
        {
            cleanupTab.interactable = false;
        }
        
        private void OnAnyButtonClicked(Button button)
        {
            FMODUnity.EventReference ev = FMODEvents.instance.uiSelect;
            
            float randomPitch = Random.Range(0.9f, 1.1f);
            
            AudioManager.instance.PlayOneShot(ev, Vector3.zero, randomPitch);
        }
    }
}
