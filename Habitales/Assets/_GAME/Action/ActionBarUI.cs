using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Habitales.UI.Actions
{
    /// <summary>
    /// Controller for the always-visible bottom action bar (Phase 4). Replaces the dormant
    /// ActionUI tile-first flow with an action-first paradigm: pick category → pick action
    /// → click tile → confirm. All rendering is delegated to the four passive view
    /// MonoBehaviours; this class owns the state machine and cross-system coordination.
    ///
    /// <para><b>Frozen public surface</b> (OnboardingDirector + RunManager depend on it):</para>
    /// <list type="bullet">
    ///   <item><see cref="OnActionArmed"/></item>
    ///   <item><see cref="OnActionConfirmed"/></item>
    ///   <item><see cref="CurrentArmedAction"/></item>
    ///   <item><see cref="GetArmCueRect"/></item>
    ///   <item><see cref="GetConfirmButtonRect"/></item>
    /// </list>
    /// </summary>
    public class ActionBarUI : MonoBehaviour
    {
        [Header("Views")]
        [SerializeField] private ActionCategoryBar categoryBar;
        [SerializeField] private ActionStripView    strip;
        [SerializeField] private ActionEstimatePanel estimatePanel;
        [SerializeField] private LockModalView       lockModal;

        [Header("Systems")]
        [SerializeField] private ActionManager actionManager;
        [SerializeField] private TileSelector  tileSelector;

        // ─── Frozen public surface ────────────────────────────────────────────

        /// <summary>
        /// Fires when an action is armed (selected from the action strip). Law-2: fired inside
        /// ArmAction() at the moment the action becomes the active armed selection.
        /// </summary>
        public event Action<PlayerAction> OnActionArmed;

        /// <summary>
        /// Fires when the player presses Confirm (the selection is committed to execution).
        /// Law-2: fired at the moment of meaning (confirm pressed), not on field mutation.
        /// </summary>
        public event Action OnActionConfirmed;

        /// <summary>
        /// The currently armed action, or null if nothing is selected. Law-1 getter — read-only;
        /// write via ArmAction() / Disarm() only. Consumed by OnboardingDirector to poll for
        /// "Plant Trees selected" without a dedicated event.
        /// </summary>
        public PlayerAction CurrentArmedAction => currentAction;

        /// <summary>
        /// Returns the RectTransform a coach-mark should point at to guide the player toward
        /// arming <paramref name="action"/>. If the action's card is currently built (strip open)
        /// we point at the card; otherwise the strip is collapsed so we point at the category tab.
        /// Pass null to get the Intervene tab as a generic "open your action bar" target.
        /// </summary>
        public RectTransform GetArmCueRect(PlayerAction action)
        {
            if (action != null)
            {
                RectTransform cardRect = strip != null ? strip.GetCardRect(action) : null;
                if (cardRect != null) return cardRect;
            }
            return categoryBar != null
                ? categoryBar.GetTabRect(action != null ? action.Category : ActionCategory.Intervene)
                : null;
        }

        /// <summary>
        /// The Confirm button's RectTransform, or null if unwired. Law-1 read-only getter —
        /// consumed by OnboardingDirector to point a FidgetArrow at Confirm once a selection exists.
        /// </summary>
        public RectTransform GetConfirmButtonRect()
            => estimatePanel != null ? estimatePanel.GetConfirmRect() : null;

        // ─── State ────────────────────────────────────────────────────────────

        private ActionCategory? currentCategory;
        private PlayerAction    currentAction;

        // ─── Lifecycle ────────────────────────────────────────────────────────

        void Awake()
        {
            // Auto-resolve system refs.
            if (actionManager == null) actionManager = ActionManager.Instance;
            if (tileSelector  == null) tileSelector  = FindObjectOfType<TileSelector>();

            // Law-3 loud-fail for every view ref — if a view is missing the controller cannot function.
            bool ok = true;
            if (categoryBar == null)
            {
                Debug.LogError($"{name}: categoryBar is not wired — assign the ActionCategoryBar component in the Inspector.", this);
                ok = false;
            }
            if (strip == null)
            {
                Debug.LogError($"{name}: strip is not wired — assign the ActionStripView component in the Inspector.", this);
                ok = false;
            }
            if (estimatePanel == null)
            {
                Debug.LogError($"{name}: estimatePanel is not wired — assign the ActionEstimatePanel component in the Inspector.", this);
                ok = false;
            }
            if (lockModal == null)
            {
                Debug.LogError($"{name}: lockModal is not wired — assign the LockModalView component in the Inspector.", this);
                ok = false;
            }
            if (tileSelector == null)
            {
                Debug.LogError($"{name}: tileSelector is not found — ensure a TileSelector is in the scene or wire it in the Inspector.", this);
                ok = false;
            }

            if (!ok) { enabled = false; return; }

            // Start with both panels collapsed.
            strip.SetVisible(false);
            estimatePanel.SetVisible(false);
        }

        void OnEnable()
        {
            if (tileSelector != null)
            {
                tileSelector.OnTileSelected            += HandleTileClicked;
                tileSelector.OnMultiSelectionConfirmed += HandleConfirmed;
            }

            if (categoryBar != null)
                categoryBar.OnCategorySelected += SelectCategory;

            if (strip != null)
            {
                strip.OnActionCardClicked += ArmAction;
                strip.OnLockClicked       += HandleLockClicked;
            }

            if (estimatePanel != null)
                estimatePanel.OnConfirmClicked += HandleConfirmClicked;
        }

        void OnDisable()
        {
            if (tileSelector != null)
            {
                tileSelector.OnTileSelected            -= HandleTileClicked;
                tileSelector.OnMultiSelectionConfirmed -= HandleConfirmed;
            }

            if (categoryBar != null)
                categoryBar.OnCategorySelected -= SelectCategory;

            if (strip != null)
            {
                strip.OnActionCardClicked -= ArmAction;
                strip.OnLockClicked       -= HandleLockClicked;
            }

            if (estimatePanel != null)
                estimatePanel.OnConfirmClicked -= HandleConfirmClicked;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && currentAction != null)
                Disarm();

            if (currentAction != null)
                RefreshEstimates();

            // Confirm is interactable only when an action is armed AND at least one tile is selected.
            if (estimatePanel != null)
                estimatePanel.SetConfirmInteractable(
                    currentAction != null && tileSelector != null && tileSelector.GetSelectedTile() != null);
        }

        // ─── View event handlers (upward channel) ─────────────────────────────

        private void HandleLockClicked()
        {
            if (lockModal != null) lockModal.Show();
        }

        private void HandleConfirmClicked()
        {
            // Controller mediates the TileSelector write — views never touch TileSelector (Law 1).
            if (tileSelector != null) tileSelector.ConfirmSelection();
        }

        // ─── Category selection ───────────────────────────────────────────────

        /// <summary>
        /// Selects a category, rebuilding the action strip. Toggling the same open category
        /// collapses the strip. Called by the controller itself (category bar event) and may be
        /// called externally to force a category open.
        /// </summary>
        public void SelectCategory(ActionCategory category)
        {
            if (currentAction != null) Disarm();

            bool sameOpen = currentCategory == category && strip != null && strip.IsVisible;
            if (sameOpen)
            {
                currentCategory = null;
                if (strip        != null) strip.SetVisible(false);
                if (categoryBar  != null) categoryBar.SetActiveCategory(null);
                return;
            }

            currentCategory = category;
            if (strip       != null) strip.Render(GetActionsFor(category));
            if (strip       != null) strip.SetVisible(true);
            if (categoryBar != null) categoryBar.SetActiveCategory(category);
        }

        private IEnumerable<PlayerAction> GetActionsFor(ActionCategory category)
        {
            if (actionManager == null) return Array.Empty<PlayerAction>();
            return actionManager.GetAvailableActions().Where(a => a.Category == category);
        }

        // ─── Action arming ────────────────────────────────────────────────────

        /// <summary>
        /// Arms (or disarms if already armed) <paramref name="action"/>. Wires the estimate
        /// panel and seeds the TileSelector with any already-selected tile.
        /// </summary>
        public void ArmAction(PlayerAction action)
        {
            if (currentAction == action)
            {
                Disarm();
                return;
            }

            currentAction = action;

            if (estimatePanel != null) estimatePanel.SetVisible(true);
            if (strip         != null) strip.Highlight(action);

            // Law-2: fire the armed event now that the action is meaningfully selected.
            OnActionArmed?.Invoke(currentAction);

            Tile seed = tileSelector?.GetSelectedTile();
            if (seed != null) EnterSelectionFor(seed);
        }

        /// <summary>
        /// Routes the armed action's selectionMode to the right TileSelector entry point.
        /// FloodFill → paint/blob mode; everything else → click-based multi-select.
        /// </summary>
        private void EnterSelectionFor(Tile seed)
        {
            if (currentAction == null || tileSelector == null || seed == null) return;

            if (currentAction.selectionMode == SelectionMode.FloodFill)
                tileSelector.EnterFloodFillMode(currentAction, seed);
            else
                tileSelector.EnterMultiSelectMode(currentAction, seed);
        }

        /// <summary>
        /// Clears the armed action, cancels any in-progress selection, and hides the estimate panel.
        /// </summary>
        public void Disarm()
        {
            currentAction = null;

            // Covers both flood-fill and click-based multi-select (IsMultiSelectMode is true for both).
            if (tileSelector != null && tileSelector.IsMultiSelectMode)
                tileSelector.CancelSelection();

            if (estimatePanel != null) estimatePanel.SetVisible(false);
            if (strip         != null) strip.Highlight(null);
        }

        // ─── Tile event handlers ──────────────────────────────────────────────

        private void HandleTileClicked(Tile tile, Vector3 _)
        {
            if (currentAction == null) return;
            EnterSelectionFor(tile);
        }

        private void HandleConfirmed(List<Tile> tiles)
        {
            if (currentAction == null || actionManager == null) return;
            // Law-2: fire confirmed at the moment of meaning (action committed), before Disarm clears state.
            OnActionConfirmed?.Invoke();
            actionManager.ExecuteAction(currentAction, tiles);
            Disarm();
        }

        // ─── Estimates ────────────────────────────────────────────────────────

        private void RefreshEstimates()
        {
            if (tileSelector == null || ResourceManager.Instance == null || currentAction == null) return;

            int tileCount = tileSelector.SelectedTileCount;
            int people    = ResourceManager.Instance.AvailablePeople;
            int days      = currentAction.CalculateDays(people, tileCount);

            int fatigue = 0;
            if (tileCount > 0)
            {
                int   minRequired = tileCount * currentAction.MinPeoplePerTile;
                float exertion    = Mathf.Clamp01((float)minRequired / Mathf.Max(1, people));
                fatigue           = Mathf.Max(1, Mathf.RoundToInt(people * exertion * 0.25f));
            }

            if (estimatePanel != null)
                estimatePanel.Render(tileCount, days, fatigue, currentAction.ActionName);
        }
    }
}
