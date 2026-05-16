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
    [SerializeField] private Transform actionStripContent;
    [SerializeField] private GameObject actionCardPrefab;
    [SerializeField] private Color defaultCardColor = new Color(0.75f, 0.75f, 0.75f);
    [SerializeField] private float plantCardWhitening = 0.4f;

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

    // ─── State ───────────────────────────────────────────────────────────────
    private ActionCategory? currentCategory;
    private PlayerAction currentAction;
    private readonly Dictionary<PlayerAction, GameObject> cardObjects = new Dictionary<PlayerAction, GameObject>();

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    void Awake()
    {
        if (actionManager == null) actionManager = FindObjectOfType<ActionManager>();
        if (tileSelector == null)  tileSelector  = FindObjectOfType<TileSelector>();

        if (lockModal != null) lockModal.SetActive(false);
        if (brushControls != null) brushControls.SetActive(false);
    }

    void OnEnable()
    {
        if (tileSelector != null)
        {
            tileSelector.OnTileSelected            += HandleTileClicked;
            tileSelector.OnMultiSelectionConfirmed += HandleConfirmed;
        }

        if (brushSizeSlider != null)
            brushSizeSlider.onValueChanged.AddListener(OnSliderChanged);

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

        if (brushSizeSlider != null)
            brushSizeSlider.onValueChanged.RemoveListener(OnSliderChanged);

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

    public void SelectCategory(ActionCategory category)
    {
        if (currentAction != null)
            Disarm();

        currentCategory = category;
        RebuildActionStrip(category);
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

        Tile seed = tileSelector?.GetSelectedTile();
        if (seed != null)
        {
            tileSelector.EnterFloodFillMode(currentAction, seed);
            RefreshBrushBounds();
        }
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

        if (action is PlantingAction plantingAction)
        {
            PlantingProfileSO profile = plantingAction.Profile;
            if (bg != null)
                bg.color = HWBColor.HWBToRGB(profile.hue, profile.blackness + plantCardWhitening, profile.blackness);

            TMP_Text label = card.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.text  = profile.plantName;
                label.color = Color.black;
            }

            // Disable icon Image child if present
            Transform icon = card.transform.Find("ActionIcon");
            if (icon != null) icon.gameObject.SetActive(false);

            // Wire Boogle "?" button
            Transform boogleGO = card.transform.Find("BoogleButton");
            if (boogleGO != null)
            {
                Button boogleBtn = boogleGO.GetComponent<Button>();
                if (boogleBtn != null)
                {
                    var capturedProfile = profile;
                    boogleBtn.onClick.AddListener(() => BooglePanelUI.Instance?.Show(capturedProfile));
                }
            }
        }
        else
        {
            if (bg != null) bg.color = defaultCardColor;

            TMP_Text label = card.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.text  = action.ActionName;
                label.color = Color.black;
            }

            Transform icon = card.transform.Find("ActionIcon");
            if (icon != null) icon.gameObject.SetActive(false);
        }

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

        Transform boogleGO = card.transform.Find("BoogleButton");
        if (boogleGO != null) boogleGO.gameObject.SetActive(false);

        Button cardBtn = card.GetComponent<Button>();
        if (cardBtn != null)
            cardBtn.onClick.AddListener(() => { if (lockModal != null) lockModal.SetActive(true); });
    }

    // ─── Tile Event Handlers ──────────────────────────────────────────────────

    void HandleTileClicked(Tile tile, Vector3 _)
    {
        if (currentAction == null) return;
        tileSelector.EnterFloodFillMode(currentAction, tile);
        RefreshBrushBounds();
    }

    void HandleConfirmed(List<Tile> tiles)
    {
        if (currentAction == null || actionManager == null) return;
        actionManager.ExecuteAction(currentAction, tiles);
        Disarm();
    }

    // ─── Slider ───────────────────────────────────────────────────────────────

    void OnSliderChanged(float value)
    {
        if (tileSelector == null || !tileSelector.IsFloodFillMode) return;
        int count = Mathf.RoundToInt(value);
        tileSelector.SetFloodFillSize(count);
    }

    // Recomputes slider min/max from the (potentially new) seed's reachable count.
    // Preserves the player's current brush size where possible (clamped to new max).
    void RefreshBrushBounds()
    {
        if (brushSizeSlider == null || tileSelector == null) return;

        int min = tileSelector.MinSelectableTiles;
        int max = Mathf.Max(min, Mathf.Min(tileSelector.MaxSelectableTiles, tileSelector.FloodFillReachableCount));

        brushSizeSlider.wholeNumbers = true;
        brushSizeSlider.minValue     = min;
        brushSizeSlider.maxValue     = max;

        // Preserve the previous brush size across reseeds, clamped into the new range.
        int desired = Mathf.Clamp(Mathf.RoundToInt(brushSizeSlider.value), min, max);
        brushSizeSlider.value = desired;
        tileSelector.SetFloodFillSize(desired);
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
