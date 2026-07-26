using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Habitales.UI;   // IUISubsystem

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
    public class ActionBarUI : MonoBehaviour, IUISubsystem
    {
        // ─── IUISubsystem ─────────────────────────────────────────────────────

        /// <summary>
        /// The root Canvas (or its GameObject) to show/hide for the master visibility sweep.
        /// Wire the action-bar root Canvas component here in the Inspector.
        /// </summary>
        [Header("UISubsystem Root")]
        [SerializeField] private Canvas rootCanvas;

        // Whether input is currently allowed (cleared during modal popups).
        private bool _interactable = true;

        /// <inheritdoc/>
        public string SubsystemId => "actionbar";

        /// <inheritdoc/>
        public bool IsVisible => rootCanvas != null && rootCanvas.gameObject.activeSelf;

        /// <inheritdoc/>
        public void SetVisible(bool visible)
        {
            if (rootCanvas != null)
                rootCanvas.gameObject.SetActive(visible);
            else
                gameObject.SetActive(visible);
        }

        /// <summary>
        /// Called by UIManager to block/unblock user input during an intrusive (modal) popup.
        /// Toggles the root CanvasGroup's interactable + blocksRaycasts so no state is destroyed.
        /// </summary>
        public void SetInteractable(bool interactable)
        {
            _interactable = interactable;

            // Prefer a CanvasGroup on the root for a clean raycast block without hiding content.
            CanvasGroup cg = rootCanvas != null
                ? rootCanvas.GetComponent<CanvasGroup>()
                : GetComponent<CanvasGroup>();

            if (cg != null)
            {
                cg.interactable    = interactable;
                cg.blocksRaycasts  = interactable;
            }
        }

        [Header("Views")]
        [SerializeField] private ActionCategoryBar categoryBar;
        [SerializeField] private ActionStripView    strip;
        [SerializeField] private ActionEstimatePanel estimatePanel;
        [SerializeField] private FlowerBudToggle     flower;

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

        /// <summary>
        /// Programmatically blooms/collapses the category flower — lets OnboardingDirector open the
        /// categories during a guided step without simulating a click. Routes through the same
        /// controller path as a user toggle (Law-1-clean: mutation through the owning system).
        /// </summary>
        public void SetCategoriesExpanded(bool expanded) => HandleBloomToggled(expanded);

        /// <summary>
        /// The flower button's RectTransform, or null if unwired. Law-1 read-only getter — consumed
        /// by OnboardingDirector to point a coach-mark at the flower ("tap here to see your actions").
        /// </summary>
        public RectTransform GetFlowerCueRect()
            => flower != null ? flower.GetFlowerRect() : null;

        /// <summary>
        /// True when the action strip (category cards) is currently open. Law-1 read-only getter —
        /// consumed by OnboardingDirector's phase-4 gate to detect the player opening the Intervene strip.
        /// </summary>
        public bool IsStripOpen => strip != null && strip.IsVisible;

        /// <summary>
        /// Onboarding phase-5 lock: dims and disables every action card EXCEPT <paramref name="keep"/>,
        /// steering the player toward the single taught action (Plant Trees). Delegates to the strip view.
        /// Always pair with <see cref="ClearCardLock"/> — a card left dimmed after onboarding is a soft-lock.
        /// </summary>
        public void LockCardsExcept(PlayerAction keep) => strip?.SetCardsLockedExcept(keep);

        /// <summary>Restores every card to its normal interactive state (undoes <see cref="LockCardsExcept"/>).</summary>
        public void ClearCardLock() => strip?.ClearCardLock();

        /// <summary>
        /// Onboarding-only: guarantees <paramref name="category"/> is revealed with its card strip open,
        /// WITHOUT the side-effects of a user gesture. Blooms the flower only if it isn't already bloomed
        /// (so the reveal tween never replays) and opens the strip only if it isn't already open on this
        /// category (so it never toggles closed and never rebuilds/flickers the cards). Idempotent — safe
        /// to re-call on phase re-entry.
        ///
        /// <para>Deliberately NOT <see cref="SelectCategory"/>: that method is a user TOGGLE — re-invoking
        /// it on the already-open category collapses the strip. Phase 5 runs right after the player opened
        /// Intervene to clear phase 4, so calling SelectCategory there reset the bar (2026-07-22 fix).</para>
        /// </summary>
        public void RevealCategory(ActionCategory category)
        {
            // Bloom only on a real state change — re-blooming replays FlowerBudToggle's reveal tween.
            if (flower != null && !flower.IsBloomed)
                SetCategoriesExpanded(true);

            bool alreadyOpen = currentCategory == category && strip != null && strip.IsVisible;
            if (alreadyOpen) return;   // leave the player's open strip exactly as-is (no rebuild/flicker)

            if (currentAction != null) Disarm();
            currentCategory = category;
            if (strip       != null) strip.Render(GetActionsFor(category));
            if (strip       != null) strip.SetVisible(true);
            if (categoryBar != null) categoryBar.SetActiveCategory(category);
            // Match SelectCategory: opening a strip hides the category tabs (Back re-shows them).
            if (categoryBar != null) categoryBar.SetVisible(false);
        }

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

            // IUISubsystem root — warn (not fatal) so the bar still works without a dedicated Canvas ref;
            // SetVisible will fall back to toggling this GameObject.
            if (rootCanvas == null)
                Debug.LogError($"{name}: rootCanvas is not wired — assign the action-bar root Canvas in the Inspector. " +
                               "SetVisible will fall back to toggling this GameObject.", this);

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
            // Flower bloom is DEPRECATED — the category tabs are shown directly now. A missing flower
            // is fine (warn only), so removing it from the scene never disables the whole action bar.
            if (tileSelector == null)
            {
                Debug.LogError($"{name}: tileSelector is not found — ensure a TileSelector is in the scene or wire it in the Inspector.", this);
                ok = false;
            }

            if (!ok) { enabled = false; return; }

            // Start on the category-tabs state: strip + estimate hidden, tabs shown.
            // (Flower bloom is deprecated, so the tabs are no longer gated behind it.)
            strip.SetVisible(false);
            estimatePanel.SetVisible(false);
            if (categoryBar != null) categoryBar.SetVisible(true);
        }

        void OnEnable()
        {
            if (tileSelector != null)
            {
                tileSelector.OnTileSelected            += HandleTileClicked;
                tileSelector.OnMultiSelectionConfirmed += HandleConfirmed;
                tileSelector.OnBrushSizeChanged        += HandleBrushSizeFromSelector;
            }

            if (categoryBar != null)
                categoryBar.OnCategorySelected += SelectCategory;

            if (strip != null)
            {
                strip.OnActionCardClicked += ArmAction;
                strip.OnBackClicked       += ShowCategories;
            }

            if (estimatePanel != null)
            {
                estimatePanel.OnConfirmClicked   += HandleConfirmClicked;
                estimatePanel.OnCancelClicked    += HandleCancelClicked;
                estimatePanel.OnBrushSizeChanged += HandleBrushSliderChanged;
            }

            if (flower != null)
                flower.OnBloomToggled += HandleBloomToggled;
            
            OnActionConfirmed += ShowCategories;
        }

        void OnDisable()
        {
            if (tileSelector != null)
            {
                tileSelector.OnTileSelected            -= HandleTileClicked;
                tileSelector.OnMultiSelectionConfirmed -= HandleConfirmed;
                tileSelector.OnBrushSizeChanged        -= HandleBrushSizeFromSelector;
            }

            if (categoryBar != null)
                categoryBar.OnCategorySelected -= SelectCategory;

            if (strip != null)
            {
                strip.OnActionCardClicked -= ArmAction;
                strip.OnBackClicked       -= ShowCategories;
            }

            if (estimatePanel != null)
            {
                estimatePanel.OnConfirmClicked   -= HandleConfirmClicked;
                estimatePanel.OnCancelClicked    -= HandleCancelClicked;
                estimatePanel.OnBrushSizeChanged -= HandleBrushSliderChanged;
            }

            if (flower != null)
                flower.OnBloomToggled -= HandleBloomToggled;
            
            OnActionConfirmed -= ShowCategories;
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

        private void HandleConfirmClicked()
        {
            // Controller mediates the TileSelector write — views never touch TileSelector (Law 1).
            if (tileSelector != null) tileSelector.ConfirmSelection();
        }

        /// <summary>
        /// Cancel pressed on the estimate panel: disarm the action (which cancels any in-progress
        /// selection and hides the estimate panel) so the player drops back to the still-open action
        /// strip to pick a different card. The strip is never hidden while an action is armed, so no
        /// re-show is needed here.
        /// </summary>
        private void HandleCancelClicked() => Disarm();

        // ─── Brush-size mediation (flood-fill slider ↔ TileSelector) ──────────

        /// <summary>Blob resized by drag/Ctrl+Scroll/initial flood → sync (and reveal) the slider.</summary>
        private void HandleBrushSizeFromSelector(int current, int min, int max)
        {
            if (estimatePanel != null) estimatePanel.ShowBrush(min, max, current);
        }

        /// <summary>Slider dragged by the player → resize the blob (Law 1: controller mediates the write).</summary>
        private void HandleBrushSliderChanged(int size)
        {
            if (tileSelector != null) tileSelector.SetBrushSize(size);
        }

        // ─── Category selection ───────────────────────────────────────────────

        /// <summary>
        /// Selects a category: hides the category tabs and opens the action strip for that category.
        /// The strip's Back button (see <see cref="ShowCategories"/>) returns the player to the tabs.
        /// Called by the controller itself (category bar event) and may be called externally to force
        /// a category open.
        /// </summary>
        public void SelectCategory(ActionCategory category)
        {
            if (currentAction != null) Disarm();

            currentCategory = category;
            if (strip       != null) strip.Render(GetActionsFor(category));
            if (strip       != null) strip.SetVisible(true);
            if (categoryBar != null) categoryBar.SetActiveCategory(category);
            // Hide the category tabs so only the strip (+ its Back button) is shown.
            if (categoryBar != null) categoryBar.SetVisible(false);
        }

        /// <summary>
        /// Closes the action strip and returns to the category tabs (the strip's Back/exit button).
        /// Disarms any armed action first so the estimate panel doesn't linger over the tabs.
        /// </summary>
        public void ShowCategories()
        {
            if (currentAction != null) Disarm();

            currentCategory = null;
            if (strip != null)
            {
                strip.SetVisible(false);
                strip.HideButtons();
            }
            if (categoryBar != null) categoryBar.SetActiveCategory(null);
            if (categoryBar != null) categoryBar.SetVisible(true);
        }

        private IEnumerable<PlayerAction> GetActionsFor(ActionCategory category)
        {
            if (actionManager == null) return Array.Empty<PlayerAction>();
            return actionManager.GetAvailableActions().Where(a => a.Category == category);
        }

        // ─── Flower bloom (category bar reveal) ───────────────────────────────

        /// <summary>
        /// Handles a flower toggle request from the FlowerBudToggle view. The controller owns the
        /// bloom state machine: it drives the flower presentation down and, when collapsing, closes
        /// the whole category UI (disarm + collapse strip + clear active category) so nothing is
        /// left dangling behind a hidden flower.
        /// </summary>
        private void HandleBloomToggled(bool wantBloom)
        {
            if (flower != null) flower.SetBloomed(wantBloom);

            if (wantBloom)
            {
                // Blooming always lands on the category-tabs state: no strip open, tabs shown.
                if (currentAction != null) Disarm();
                currentCategory = null;
                if (strip       != null) strip.SetVisible(false);
                if (categoryBar != null) categoryBar.SetActiveCategory(null);
                if (categoryBar != null) categoryBar.SetVisible(true);
            }
            else
            {
                if (currentAction != null) Disarm();
                currentCategory = null;
                if (strip       != null) strip.SetVisible(false);
                if (categoryBar != null) categoryBar.SetActiveCategory(null);
            }
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
            // Hide any lingering brush slider from a previously armed FloodFill action;
            // a FloodFill entry re-shows it via TileSelector.OnBrushSizeChanged.
            if (estimatePanel != null) estimatePanel.HideBrush();
            // Arming swaps the strip out for the estimate panel — the two are stacked states, not
            // shown together. Disarm/Cancel/Confirm re-show the strip via Disarm().
            if (strip         != null) strip.SetVisible(false);

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
            if (estimatePanel != null) estimatePanel.HideBrush();
            if (strip         != null) strip.Highlight(null);

            // Return to the strip view (the estimate panel replaced it on Arm). Only when a category
            // is still open — callers that leave the strip entirely (ShowCategories / bloom collapse)
            // null out currentCategory first, then hide the strip again after this returns.
            if (currentCategory != null && strip != null) strip.SetVisible(true);
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

            // ExecuteAction can refuse (event pause, workforce, CanExecute). A refused confirm
            // used to fire OnActionConfirmed and Disarm anyway, so the bar reset exactly as if
            // the action ran — the "actions silently do nothing" symptom (2026-07-19 fix). On
            // refusal the action stays armed; TileSelector already cleared the selection, so
            // the player just re-picks tiles and confirms again.
            if (!actionManager.ExecuteAction(currentAction, tiles))
            {
                Debug.LogWarning($"{name}: '{currentAction.ActionName}' confirm refused — see the ActionManager warning above. Action stays armed.", this);
                return;
            }

            Debug.Log("Card Confirmed");
            
            // Law-2: fire confirmed at the moment of meaning (action committed), before Disarm clears state.
            OnActionConfirmed?.Invoke();
            
            if (currentAction != null)
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
