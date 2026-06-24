using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Always-visible bottom action bar (Phase 4). Replaces the dormant ActionUI tile-first flow
/// with an action-first paradigm: pick category → pick action → click tile → confirm.
/// </summary>
public class ActionBarUI : MonoBehaviour
{
    [Header("Bar Root")]
    [SerializeField] private RectTransform barRoot;

    [Header("Category Tabs")]
    [SerializeField] private Button examineTab;
    [SerializeField] private Button interveneTab;
    [SerializeField] private Button emergencyTab;
    [SerializeField] private Button cleanupTab;

    [Header("Action Strip")]
    [Tooltip("Optional. The whole panel toggled by category buttons (background + content). If null, falls back to actionStripContent.gameObject.")]
    [SerializeField] private GameObject actionStripRoot;
    [SerializeField] private Transform actionStripContent;
    [SerializeField] private GameObject actionCardPrefab;
    [SerializeField] private Color defaultCardColor = new Color(0.75f, 0.75f, 0.75f);

    [Header("Brush Controls")]
    [SerializeField] private GameObject brushControls;
    [SerializeField] private Slider brushSizeSlider;
    [SerializeField] private Button confirmButton;
    [SerializeField] private TMP_Text armedActionNameText;
    [SerializeField] private TMP_Text tileCountText;
    [SerializeField] private TMP_Text daysEstimateText;
    [SerializeField] private TMP_Text fatigueEstimateText;

    [Header("Lock Modal")]
    [SerializeField] private GameObject lockModal;
    [SerializeField] private Button lockModalContinueButton;

    [Header("Systems")]
    [SerializeField] private ActionManager actionManager;
    [SerializeField] private TileSelector tileSelector;

    // ─── Events ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Fires when an action is armed (selected from the action strip). Law-2: fired inside
    /// ArmAction() at the moment the action becomes the active armed selection.
    /// </summary>
    public event Action<PlayerAction> OnActionArmed;

    /// <summary>
    /// Fires when the player presses Confirm (i.e. the selection is committed to execution).
    /// Law-2: fired at the moment of meaning (confirm pressed), not on field mutation.
    /// </summary>
    public event Action OnActionConfirmed;

    // ─── State ───────────────────────────────────────────────────────────────
    private ActionCategory? currentCategory;
    private PlayerAction currentAction;
    private readonly Dictionary<PlayerAction, GameObject> cardObjects = new Dictionary<PlayerAction, GameObject>();

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    void Awake()
    {
        if (actionManager == null) actionManager = ActionManager.Instance;
        if (tileSelector == null)  tileSelector  = FindObjectOfType<TileSelector>();

        if (lockModal != null) lockModal.SetActive(false);
        if (brushControls != null) brushControls.SetActive(false);
        // Drag-driven trail replaces the brush-size slider; hide it permanently.
        if (brushSizeSlider != null) brushSizeSlider.gameObject.SetActive(false);
        SetStripVisible(false); // strip starts collapsed until a category is pressed
    }

    void OnEnable()
    {
        if (tileSelector != null)
        {
            tileSelector.OnTileSelected            += HandleTileClicked;
            tileSelector.OnMultiSelectionConfirmed += HandleConfirmed;
        }

        if (confirmButton != null)
            confirmButton.onClick.AddListener(() => tileSelector.ConfirmSelection());

        if (lockModalContinueButton != null)
            lockModalContinueButton.onClick.AddListener(() => lockModal.SetActive(false));

        if (examineTab   != null) examineTab.onClick.AddListener(  () => SelectCategory(ActionCategory.Examine));
        if (interveneTab != null) interveneTab.onClick.AddListener(() => SelectCategory(ActionCategory.Intervene));
        if (emergencyTab != null) emergencyTab.onClick.AddListener(() => SelectCategory(ActionCategory.Emergency));
        if (cleanupTab   != null) cleanupTab.onClick.AddListener(  () => SelectCategory(ActionCategory.Cleanup));
    }

    void OnDisable()
    {
        if (tileSelector != null)
        {
            tileSelector.OnTileSelected            -= HandleTileClicked;
            tileSelector.OnMultiSelectionConfirmed -= HandleConfirmed;
        }

        if (confirmButton != null)
            confirmButton.onClick.RemoveAllListeners();

        if (lockModalContinueButton != null)
            lockModalContinueButton.onClick.RemoveAllListeners();

        if (examineTab   != null) examineTab.onClick.RemoveAllListeners();
        if (interveneTab != null) interveneTab.onClick.RemoveAllListeners();
        if (emergencyTab != null) emergencyTab.onClick.RemoveAllListeners();
        if (cleanupTab   != null) cleanupTab.onClick.RemoveAllListeners();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && currentAction != null)
            Disarm();

        if (currentAction != null)
            RefreshEstimates();

        if (confirmButton != null)
            confirmButton.interactable = currentAction != null && tileSelector != null && tileSelector.GetSelectedTile() != null;
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// The currently armed action, or null if nothing is selected.
    /// Law-1 getter — read-only; write via ArmAction() / Disarm() only.
    /// Consumed by OnboardingDirector to poll for "Plant Trees selected" without a dedicated event.
    /// </summary>
    public PlayerAction CurrentArmedAction => currentAction;

    /// <summary>
    /// Returns the RectTransform a coach-mark should point at to guide the player toward
    /// arming <paramref name="action"/>. If the action's card is currently built (its category
    /// strip is open) we point at the card; otherwise the strip is collapsed and there is no
    /// card yet, so we point at the category tab that opens it. Pass null to get the Intervene
    /// tab as a generic "open your action bar" target. Wiring-free — uses existing serialized refs.
    /// </summary>
    public RectTransform GetArmCueRect(PlayerAction action)
    {
        if (action != null && cardObjects.TryGetValue(action, out GameObject card) && card != null)
            return card.transform as RectTransform;

        Button tab = TabForCategory(action != null ? action.Category : ActionCategory.Intervene);
        return tab != null ? tab.transform as RectTransform : null;
    }

    /// <summary>
    /// The Confirm button's RectTransform, or null if unwired. Law-1 read-only getter —
    /// consumed by OnboardingDirector to point a FidgetArrow at Confirm once a selection exists.
    /// </summary>
    public RectTransform GetConfirmButtonRect()
        => confirmButton != null ? confirmButton.transform as RectTransform : null;

    private Button TabForCategory(ActionCategory category)
    {
        switch (category)
        {
            case ActionCategory.Examine:   return examineTab;
            case ActionCategory.Intervene: return interveneTab;
            case ActionCategory.Emergency: return emergencyTab;
            case ActionCategory.Cleanup:   return cleanupTab;
            default:                       return interveneTab;
        }
    }

    public void SelectCategory(ActionCategory category)
    {
        if (currentAction != null)
            Disarm();

        // Toggle: pressing the same category that's currently open collapses the strip.
        // Pressing a different category swaps content and keeps the strip open.
        bool sameCategoryAlreadyOpen = currentCategory == category && IsStripVisible();
        if (sameCategoryAlreadyOpen)
        {
            currentCategory = null;
            SetStripVisible(false);
            return;
        }

        currentCategory = category;
        RebuildActionStrip(category);
        SetStripVisible(true);
    }

    // ─── Strip visibility helpers ─────────────────────────────────────────────

    private GameObject StripToggleTarget =>
        actionStripRoot != null ? actionStripRoot :
        (actionStripContent != null ? actionStripContent.gameObject : null);

    void SetStripVisible(bool visible)
    {
        var target = StripToggleTarget;
        if (target != null) target.SetActive(visible);
    }

    bool IsStripVisible()
    {
        var target = StripToggleTarget;
        return target != null && target.activeSelf;
    }

    public void ArmAction(PlayerAction action)
    {
        if (currentAction == action)
        {
            Disarm();
            return;
        }

        currentAction = action;

        if (armedActionNameText != null)
            armedActionNameText.text = action.ActionName;

        if (brushControls != null)
            brushControls.SetActive(true);

        RefreshCardHighlights();

        // Law-2: fire the armed event now that the action is meaningfully selected.
        OnActionArmed?.Invoke(currentAction);

        Tile seed = tileSelector?.GetSelectedTile();
        if (seed != null)
            tileSelector.EnterFloodFillMode(currentAction, seed);
    }

    public void Disarm()
    {
        currentAction = null;

        if (tileSelector != null && tileSelector.IsFloodFillMode)
            tileSelector.CancelSelection();

        if (brushControls != null)
            brushControls.SetActive(false);

        RefreshCardHighlights();
    }

    // ─── Strip Builder ────────────────────────────────────────────────────────

    void RebuildActionStrip(ActionCategory category)
    {
        cardObjects.Clear();

        foreach (Transform child in actionStripContent)
            Destroy(child.gameObject);

        if (actionManager == null) return;

        List<PlayerAction> all = actionManager.GetAvailableActions();

        // TODO Phase 5: dedup variant groups (e.g. cover crop variants) once they're un-dormant.
        foreach (PlayerAction action in all)
        {
            if (action.Category != category) continue;
            SpawnActionCard(action);
        }

        SpawnLockCard();
    }

    void SpawnActionCard(PlayerAction action)
    {
        if (actionCardPrefab == null || actionStripContent == null) return;

        GameObject card = Instantiate(actionCardPrefab, actionStripContent);
        card.name = $"Card_{action.ActionName}";
        cardObjects[action] = card;

        Image bg = card.GetComponent<Image>();
        if (bg != null) bg.color = defaultCardColor;

        TMP_Text label = card.GetComponentInChildren<TMP_Text>();
        if (label != null)
        {
            label.text  = action.ActionName;
            label.color = Color.black;
        }

        Transform icon = card.transform.Find("ActionIcon");
        if (icon != null) icon.gameObject.SetActive(false);

        Button cardBtn = card.GetComponent<Button>();
        if (cardBtn != null)
        {
            var captured = action;
            cardBtn.onClick.AddListener(() => ArmAction(captured));
        }
    }

    void SpawnLockCard()
    {
        if (actionCardPrefab == null || actionStripContent == null) return;

        GameObject card = Instantiate(actionCardPrefab, actionStripContent);
        card.name = "Card_Locked";

        Image bg = card.GetComponent<Image>();
        if (bg != null) bg.color = defaultCardColor;

        TMP_Text label = card.GetComponentInChildren<TMP_Text>();
        if (label != null)
        {
            label.text  = "Locked";
            label.color = Color.black;
        }

        Transform icon = card.transform.Find("ActionIcon");
        if (icon != null) icon.gameObject.SetActive(false);

        Button cardBtn = card.GetComponent<Button>();
        if (cardBtn != null)
            cardBtn.onClick.AddListener(() => { if (lockModal != null) lockModal.SetActive(true); });
    }

    // ─── Tile Event Handlers ──────────────────────────────────────────────────

    void HandleTileClicked(Tile tile, Vector3 _)
    {
        if (currentAction == null) return;
        tileSelector.EnterFloodFillMode(currentAction, tile);
    }

    void HandleConfirmed(List<Tile> tiles)
    {
        if (currentAction == null || actionManager == null) return;
        // Law-2: fire confirmed at the moment of meaning (action committed), before Disarm clears state.
        OnActionConfirmed?.Invoke();
        actionManager.ExecuteAction(currentAction, tiles);
        Disarm();
    }

    // ─── Estimates ───────────────────────────────────────────────────────────

    void RefreshEstimates()
    {
        if (tileSelector == null || ResourceManager.Instance == null) return;

        int tileCount = tileSelector.SelectedTileCount;
        int people    = ResourceManager.Instance.AvailablePeople;

        if (tileCountText != null)
            tileCountText.text = $"{tileCount} tiles";

        if (daysEstimateText != null)
        {
            int days = currentAction.CalculateDays(people, tileCount);
            daysEstimateText.text = $"{days} days";
        }

        if (fatigueEstimateText != null && tileCount > 0)
        {
            int minRequired = tileCount * currentAction.MinPeoplePerTile;
            float exertion  = Mathf.Clamp01((float)minRequired / Mathf.Max(1, people));
            float expected  = people * exertion * 0.25f;
            int displayCount = Mathf.Max(1, Mathf.RoundToInt(expected));
            fatigueEstimateText.text = $"~{displayCount}";
        }
    }

    // ─── Card Highlights ──────────────────────────────────────────────────────

    void RefreshCardHighlights()
    {
        foreach (var kvp in cardObjects)
        {
            Button btn = kvp.Value != null ? kvp.Value.GetComponent<Button>() : null;
            if (btn == null) continue;

            if (kvp.Key == currentAction)
            {
                btn.Select();
            }
            else
            {
                var colors = btn.colors;
                btn.targetGraphic?.CrossFadeColor(colors.normalColor, 0f, true, true);
            }
        }
    }

    // ─── Phase 5 Stub ────────────────────────────────────────────────────────

    // TODO Phase 5: LeanTween.scale(cubeTransform.gameObject, originalScale, 0.4f).setEaseOutBack();
    private void OnPlantSpawnedTween(Transform cubeTransform) { }
}
