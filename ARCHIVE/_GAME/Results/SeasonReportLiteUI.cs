using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Habitales.Meta;
using Habitales.UI;

/// <summary>
/// Modal-LITE mid-run trajectory report (arch ENDGAME_BUILD_PLAN §4) — EndGameScreenUI's
/// little sibling: trajectory instead of totals. Opened by tapping the report check-in
/// message/ribbon (see DaysLeftPopupNotifier). Dim + tap-out-closes, but unlike
/// EndGameScreenUI it does NOT pause-hijack the sim — opening mid-run while the player is
/// paused-by-choice is fine, this panel never calls PauseForEvent/ResumeFromEvent.
///
/// Unscaled-time safe: the player may be paused when this opens, so no LeanTween/coroutine
/// in this file (there is none) ever depends on Time.timeScale.
///
/// Implements IUISubsystem so it can join UIManager's subsystems list (U-hub pattern —
/// see TooltipController for the reference shape).
///
/// WIRING (human):
///   1. Build the SeasonReportLite prefab (see ENDGAME_BUILD_PLAN §4 "Panel contents" for the
///      element order) and add this component to its root.
///   2. Wire every [SerializeField] below — all are loud-fail required (Awake validates and
///      disables the component on any miss, Law 3). This differs from the live EndGameScreenUI's
///      soft-fail-with-warning style deliberately: that screen is already scene-wired and must
///      degrade gracefully; this prefab doesn't exist in-scene until a human builds it, so a
///      hard loud-fail at Awake is the correct signal (nothing to "gracefully" run without).
///   3. Add this component to UIManager's serialized `subsystems` list.
///   4. Wire the dim/blocker Image's own Button (or add one) — clicking it calls Hide()
///      the same as the explicit close button.
/// </summary>
public class SeasonReportLiteUI : MonoBehaviour, IUISubsystem
{
    public static SeasonReportLiteUI Instance { get; private set; }

    [Header("Panel Root")]
    [SerializeField] private GameObject panelRoot; // toggled by Show/Hide

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI headerText;
    [SerializeField] private string headerTemplate = "Day {0} of {1} — Mid-season report";

    [Header("Sparkline")]
    [SerializeField] private HealthSparklineUI sparkline;

    [Header("Tile Tier Counts")]
    [SerializeField] private TextMeshProUGUI thrivingCountText;
    [SerializeField] private TextMeshProUGUI degradedCountText;
    [SerializeField] private TextMeshProUGUI criticalCountText;
    [SerializeField] private Color thrivingColor = new Color(0.26f, 0.48f, 0.13f, 1f);
    [SerializeField] private Color degradedColor = new Color(0.85f, 0.44f, 0.10f, 1f);
    [SerializeField] private Color criticalColor = new Color(0.63f, 0.17f, 0.17f, 1f);

    [Header("Trend")]
    [SerializeField] private TrendIndicatorUI trendIndicator;

    [Header("Projected Grade")]
    [Tooltip("Hidden entirely when the projection is SeasonGrade.Collapse — reads absurd mid-run.")]
    [SerializeField] private GameObject projectedGradeRoot;
    [SerializeField] private TextMeshProUGUI projectedGradeText;
    [SerializeField] private string projectedGradeTemplate = "On track for: {0}";

    [Header("Highlight")]
    [SerializeField] private GameObject highlightRoot;
    [SerializeField] private TextMeshProUGUI highlightText;
    [SerializeField] private string highlightTemplate = "Most-used tool: {0}";

    [Header("Azi Commentary")]
    [SerializeField] private TextMeshProUGUI aziCommentaryText;

    [Header("Modal-lite chrome")]
    [SerializeField] private Image  dimBlocker;   // click closes — assign a Button on the same GO or child
    [SerializeField] private Button dimBlockerButton;
    [SerializeField] private Button closeButton;

    // ── IUISubsystem ──────────────────────────────────────────────────────
    public string SubsystemId => "seasonReportLite";
    public bool   IsVisible   => panelRoot != null && panelRoot.activeSelf;
    public void   SetVisible(bool visible)
    {
        if (panelRoot != null) panelRoot.SetActive(visible);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError($"{name}: A second SeasonReportLiteUI instance was created. Only one is allowed in the scene. Destroying this duplicate.", this);
            Destroy(gameObject);
            return;
        }
        Instance = this;

        ValidateRefs();
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void OnEnable()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Hide);
        if (dimBlockerButton != null) dimBlockerButton.onClick.AddListener(Hide);
    }

    private void OnDisable()
    {
        if (closeButton != null) closeButton.onClick.RemoveListener(Hide);
        if (dimBlockerButton != null) dimBlockerButton.onClick.RemoveListener(Hide);
    }

    private void ValidateRefs()
    {
        bool ok = true;
        ok &= Require(panelRoot,          nameof(panelRoot));
        ok &= Require(headerText,         nameof(headerText));
        ok &= Require(sparkline,          nameof(sparkline));
        ok &= Require(thrivingCountText,  nameof(thrivingCountText));
        ok &= Require(degradedCountText,  nameof(degradedCountText));
        ok &= Require(criticalCountText,  nameof(criticalCountText));
        ok &= Require(trendIndicator,     nameof(trendIndicator));
        ok &= Require(projectedGradeRoot, nameof(projectedGradeRoot));
        ok &= Require(projectedGradeText, nameof(projectedGradeText));
        ok &= Require(highlightRoot,      nameof(highlightRoot));
        ok &= Require(highlightText,      nameof(highlightText));
        ok &= Require(aziCommentaryText,  nameof(aziCommentaryText));
        ok &= Require(dimBlocker,         nameof(dimBlocker));
        ok &= Require(closeButton,        nameof(closeButton));

        if (!ok) enabled = false;
    }

    private bool Require(Object field, string fieldName)
    {
        if (field != null) return true;
        Debug.LogError($"{name}: SeasonReportLiteUI.{fieldName} missing — wire it in the Inspector.", this);
        return false;
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Populates and shows the panel from a freshly-built <see cref="SeasonReportData"/>.
    /// Caller (DaysLeftPopupNotifier) is responsible for rebuilding the data at open time,
    /// not send time, so the report reflects the day it's actually opened.
    /// </summary>
    public void Show(SeasonReportData data, string aziLine)
    {
        if (!enabled)
        {
            Debug.LogError($"{name}: Show() called but component is disabled (missing refs) — ignoring.", this);
            return;
        }
        if (data == null)
        {
            Debug.LogWarning($"{name}: Show() called with null SeasonReportData — ignoring. (RunManager.BuildSeasonReportData returns null when runEndCoordinator is unwired.)", this);
            return;
        }

        Populate(data, aziLine);
        panelRoot.SetActive(true);
    }

    public void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    // ── Populate — pure data binding ─────────────────────────────────────

    private void Populate(SeasonReportData data, string aziLine)
    {
        headerText.text = string.Format(headerTemplate, data.currentDay, data.runLengthDays);

        sparkline.SetHistory(data.healthHistory);

        thrivingCountText.text  = data.thrivingCount.ToString();
        degradedCountText.text  = data.degradedCount.ToString();
        criticalCountText.text  = data.criticalCount.ToString();
        thrivingCountText.color = thrivingColor;
        degradedCountText.color = degradedColor;
        criticalCountText.color = criticalColor;

        trendIndicator.SetTrend(data.worldHealthTrend);

        // Projected grade — omitted entirely for Collapse (reads absurd mid-run; Collapse is
        // only ever a forced END-of-run state, not a mid-run trajectory a player should see).
        bool showGrade = data.projectedGrade != SeasonGrade.Collapse;
        projectedGradeRoot.SetActive(showGrade);
        if (showGrade)
            projectedGradeText.text = string.Format(projectedGradeTemplate, data.projectedGrade);

        bool showHighlight = !string.IsNullOrEmpty(data.topActionName);
        highlightRoot.SetActive(showHighlight);
        if (showHighlight)
            highlightText.text = string.Format(highlightTemplate, data.topActionName);

        aziCommentaryText.text = aziLine ?? string.Empty;
    }
}
