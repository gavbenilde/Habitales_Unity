using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text;
using Habitales.UI;   // IUISubsystem

/// <summary>
/// Body of the Selected Info Panel — the health + 6-substat readout. As of 2026-07-22 this is
/// MODE-AWARE and no longer selection-driven: <c>SelectedInfoPanelController</c> owns visibility
/// and pushes either a single Tile (<see cref="Populate"/>) or its parent region
/// (<see cref="PopulateRegion"/>). The retired auto-show/auto-hide (subscribe TileSelector →
/// self-show on select, self-hide on deselect, 2026-07-15) is gone; the net "no selection →
/// panel closed" behaviour is preserved by the controller's tween.
/// Health is always displayed. Substats are gated on tile.isAnalyzed (region path: ALL tiles in
/// the region analyzed — defaults true in the prototype → effectively always shown). The issues
/// section is DORMANT (2026-07-15 region/inspector pass) — force-hidden, never populated; code
/// kept for revival. Each substat row has a pre-built Image icon (substatIndicators) — as of
/// 2026-07-23 this is a STATIC sprite, no longer value-tinted — plus an optional StatTooltipTrigger
/// that receives the live value. Each row can also drive an optional static name label
/// (substatLabels, assigned once in Awake), an optional per-row progress bar (substatBars,
/// Image.fillAmount — same pattern as healthBarFill, itself untinted), an optional percentage
/// value text (substatValueTexts, e.g. "22%"), and an optional wrapper Graphic
/// (substatBarWrappers) that visually contains the bar + value text and carries the ONLY
/// green→red LerpHSV value tint left in the row. All are null-guarded like substatIndicators
/// and shared by both Populate (tile) and PopulateRegion.
/// </summary>
public class InspectPanelUI : MonoBehaviour, IUISubsystem
{
    // ── IUISubsystem ─────────────────────────────────────────────────────────
    //
    // SetVisible is the hub's hide-ALL-UI path for clean end-report screenshots —
    // a master switch that forces panelRoot off regardless of selection. It is
    // deliberately NOT the same concern as Show/Hide (the gameplay path, which is
    // driven by tile selection): UIManager snapshots visibility before hiding and
    // reapplies it on restore, so a panel hidden here comes back iff a tile is
    // still selected.

    public string SubsystemId => "inspectPanel";
    public bool   IsVisible   => panelRoot != null && panelRoot.activeSelf;
    public void   SetVisible(bool visible)
    {
        if (panelRoot != null) panelRoot.SetActive(visible);
    }

    [Header("Panel Root")]
    [Tooltip("The BODY subtree root (distinct from the controller's shared sliding root). " +
             "IUISubsystem.SetVisible toggles THIS for the screenshot hide-all master switch.")]
    [SerializeField] private GameObject panelRoot;

    [Header("Empty State")]
    [SerializeField] private GameObject emptyStateRoot;

    [Header("Health")]
    [SerializeField] private TextMeshProUGUI healthValueText;
    [SerializeField] private TextMeshProUGUI healthStatusText;
    [SerializeField] private Image healthBarFill; // optional, can be null

    [Header("Entity")]
    [Tooltip("Optional: shows the selected tile's entity display name (TileEntitySO.displayName). Blank when the tile is empty.")]
    [SerializeField] private TextMeshProUGUI entityNameText; // optional, can be null

    [Header("Issues Section (DORMANT — never shown)")]
    [SerializeField] private GameObject issuesSectionRoot;
    [SerializeField] private TextMeshProUGUI issuesText;
    [SerializeField] private TextMeshProUGUI issuesLockedText;

    [Header("Substats Section")]
    [SerializeField] private GameObject substatsSectionRoot;
    [SerializeField] private TextMeshProUGUI substatsLockedText;

    [Header("Substat Row Images (soil composite, in order)")]
    [Tooltip("Assign in order: NutrientBalance, SoilOrganicMatter, SoilStructure, BiologicalActivity, WaterDynamics, ErosionResistance")]
    [SerializeField] private Image[] substatIndicators = new Image[6];

    [Header("Substat Tooltips (same order as indicators)")]
    [Tooltip("Optional: StatTooltipTrigger per substat icon; receives the live value on populate. Empty slots are auto-filled from the matching indicator's GameObject in Awake.")]
    [SerializeField] private StatTooltipTrigger[] substatTooltips = new StatTooltipTrigger[6];

    [Header("Substat Row Labels (same order as indicators)")]
    [Tooltip("OPTIONAL, null-guarded. Static stat NAME per row (NOT a tooltip) — order: " +
             "NutrientBalance, SoilOrganicMatter, SoilStructure, BiologicalActivity, WaterDynamics, ErosionResistance. " +
             "Assigned once from SubstatNames in Awake and never touched again by populate.")]
    [SerializeField] private TextMeshProUGUI[] substatLabels = new TextMeshProUGUI[6];

    [Header("Substat Row Bars (same order as indicators)")]
    [Tooltip("OPTIONAL, null-guarded. Per-row progress bar Slider (min 0 / max 100, driven via .value), " +
             "order: NutrientBalance, SoilOrganicMatter, SoilStructure, BiologicalActivity, WaterDynamics, " +
             "ErosionResistance. Min/max are forced to 0–100 in Awake, same pattern as the header healthSlider.")]
    [SerializeField] private Slider[] substatBars = new Slider[6];

    [Header("Substat Row Value Texts (same order as indicators)")]
    [Tooltip("OPTIONAL, null-guarded. Percentage readout per row (e.g. \"22%\"), order: NutrientBalance, " +
             "SoilOrganicMatter, SoilStructure, BiologicalActivity, WaterDynamics, ErosionResistance.")]
    [SerializeField] private TextMeshProUGUI[] substatValueTexts = new TextMeshProUGUI[6];

    [Header("Substat Row Bar Wrappers (same order as indicators)")]
    [Tooltip("OPTIONAL, null-guarded. The element visually WRAPPING each row's bar + value text " +
             "(so tinting it colors that whole readout together) — order: NutrientBalance, " +
             "SoilOrganicMatter, SoilStructure, BiologicalActivity, WaterDynamics, ErosionResistance. " +
             "Graphic (not Image) so it can be either an Image container or a TMP_Text — both derive " +
             "from Graphic. Carries the green→red LerpHSV value tint; the icon and bar themselves are " +
             "no longer tinted.")]
    [SerializeField] private Graphic[] substatBarWrappers = new Graphic[6];

    // ── Colors ───────────────────────────────────────────────────────────────

    private static readonly Color ColorThriving = new Color(0.26f, 0.48f, 0.13f);
    private static readonly Color ColorDegraded = new Color(0.85f, 0.44f, 0.10f);
    private static readonly Color ColorCritical = new Color(0.85f, 0.20f, 0.15f);
    private static readonly Color ColorLocked   = new Color(0.55f, 0.55f, 0.55f);

    private static readonly Color SubstatGreen  = new Color(0.26f, 0.72f, 0.20f);
    private static readonly Color SubstatRed    = new Color(0.85f, 0.18f, 0.12f);

    // Human-readable names, same fixed order as substatIndicators/substatLabels/substatBars/
    // substatValueTexts — matches the StatTooltipTrigger._statName convention.
    private static readonly string[] SubstatNames =
    {
        "Nutrient Balance",
        "Soil Organic Matter",
        "Soil Structure",
        "Biological Activity",
        "Water Dynamics",
        "Erosion Resistance"
    };

    // ── Lifecycle ────────────────────────────────────────────────────────────

    void Awake()
    {
        // Auto-fill empty tooltip slots from the indicator's own GameObject so the
        // common case (trigger sits on the icon Image) needs no extra wiring.
        for (int i = 0; i < substatTooltips.Length && i < substatIndicators.Length; i++)
        {
            if (substatTooltips[i] == null && substatIndicators[i] != null)
                substatTooltips[i] = substatIndicators[i].GetComponent<StatTooltipTrigger>();
        }

        // Static row name labels — assigned once, never touched by populate. Stay present even
        // when substats are locked (only the bar/value reflect the locked state).
        for (int i = 0; i < substatLabels.Length && i < SubstatNames.Length; i++)
        {
            if (substatLabels[i] != null)
                substatLabels[i].text = SubstatNames[i];
        }

        // Force every substat bar Slider onto the 0–100 scale (mirrors the header healthSlider),
        // so populate can push the raw stat value straight into .value.
        foreach (var bar in substatBars)
        {
            if (bar == null) continue;
            bar.minValue = 0;
            bar.maxValue = 100;
        }

        // VISIBILITY & SELECTION are owned by SelectedInfoPanelController now (2026-07-22): this
        // body no longer subscribes to TileSelector, and no longer auto-shows on select or
        // auto-hides on deselect. It is a passive view — the controller calls Populate /
        // PopulateRegion to fill it and slides the shared root to reveal/hide it.
        //
        // Historical reasoning preserved (why the RETIRED subscription used Awake, not OnEnable):
        // this component sits on its own panelRoot, so hiding the panel deactivates its own
        // GameObject. Under OnEnable/OnDisable a subscription would have unsubscribed the moment
        // the panel hid, deadlocking it shut — no later selection could re-show it. Plain C#
        // delegates keep firing on an inactive GameObject, so Awake-scoped subscription survived
        // the panel hiding itself. The controller now applies that same Awake rule for selection.

        // Nothing is selected at boot, so start cleared/hidden.
        ShowEmpty();
    }

    /// <summary>
    /// Legacy no-arg show — kept for the older InspectModeManager driver's compat. Visibility is
    /// owned by SelectedInfoPanelController now; this just re-activates the body root. Do NOT wire
    /// both drivers (InspectModeManager and SelectedInfoPanelController) against this panel at once.
    /// </summary>
    public void Show()
    {
        if (panelRoot != null) panelRoot.SetActive(true);
    }

    /// <summary>
    /// Hides the body and clears its fields. No longer selection-coupled — the controller closes
    /// the whole panel by sliding the shared root out; call this to blank the body directly.
    /// </summary>
    public void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    /// <summary>
    /// The "nothing selected" state: the body hides entirely. Fields are still
    /// cleared and emptyStateRoot re-armed so the panel can't flash stale values
    /// on its next show, and so the empty-state visuals still work if the panel
    /// is ever reverted to always-on.
    /// </summary>
    public void ShowEmpty()
    {
        if (panelRoot != null) panelRoot.SetActive(false);

        emptyStateRoot.SetActive(true);
        if (issuesSectionRoot != null) issuesSectionRoot.SetActive(false);
        substatsSectionRoot.SetActive(false);

        healthValueText.text  = "—";
        healthStatusText.text = "";
        if (healthBarFill != null) healthBarFill.fillAmount = 0f;
        if (entityNameText != null) entityNameText.text = "";
    }

    // ── Main populate ────────────────────────────────────────────────────────

    /// <summary>TILE path: renders the selected tile's health + substats.</summary>
    public void Populate(Tile tile)
    {
        if (tile == null) { ShowEmpty(); return; }

        if (panelRoot != null) panelRoot.SetActive(true);
        emptyStateRoot.SetActive(false);

        PopulateHealthValue(tile.stats.CalculateHealth());
        PopulateEntity(tile);
        // Issues section is dormant — force-hidden instead of populated.
        if (issuesSectionRoot != null) issuesSectionRoot.SetActive(false);

        TileStats s = tile.stats;
        PopulateSubstatsValues(SubstatValues(s), tile.isAnalyzed);
    }

    /// <summary>
    /// REGION path (added 2026-07-22): renders the parent region's AGGREGATED health + substats,
    /// pulled from <see cref="RegionManager.GetRegionStats"/>. Drives the same 6 substat Images +
    /// StatTooltipTrigger + LerpHSV path as a tile. Substat reveal gate: ALL tiles in the region
    /// isAnalyzed (defaults true → effectively always shown now).
    /// </summary>
    public void PopulateRegion(int regionID)
    {
        RegionManager rm = RegionManager.Instance;
        if (rm == null)
        {
            Debug.LogError($"{name}: InspectPanelUI.PopulateRegion needs RegionManager.Instance — none in scene.", this);
            ShowEmpty();
            return;
        }

        TileStats agg = rm.GetRegionStats(regionID);

        if (panelRoot != null) panelRoot.SetActive(true);
        emptyStateRoot.SetActive(false);

        PopulateHealthValue(agg.CalculateHealth());

        // A region has no single occupant — clear the entity label.
        if (entityNameText != null) entityNameText.text = "";

        // Issues section is dormant — force-hidden instead of populated.
        if (issuesSectionRoot != null) issuesSectionRoot.SetActive(false);

        PopulateSubstatsValues(SubstatValues(agg), AllTilesAnalyzed(regionID));
    }

    // ── Section builders ─────────────────────────────────────────────────────

    private void PopulateHealthValue(float health)
    {
        healthValueText.text = $"{health:F1}%";

        if (health < 33f)
        {
            healthStatusText.text  = "Critical";
            healthStatusText.color = ColorCritical;
        }
        else if (health < 67f)
        {
            healthStatusText.text  = "Degraded";
            healthStatusText.color = ColorDegraded;
        }
        else
        {
            healthStatusText.text  = "Thriving";
            healthStatusText.color = ColorThriving;
        }

        if (healthBarFill != null)
            healthBarFill.fillAmount = Mathf.Clamp01(health / 100f);
    }

    // Order must match substatIndicators array in the Inspector:
    // [0] NutrientBalance  [1] SoilOrganicMatter  [2] SoilStructure
    // [3] BiologicalActivity  [4] WaterDynamics  [5] ErosionResistance
    private static float[] SubstatValues(TileStats s) => new[]
    {
        s.nutrientBalance,
        s.soilOrganicMatter,
        s.soilStructure,
        s.biologicalActivity,
        s.waterDynamics,
        s.erosionResistance
    };

    // Region substat reveal gate: shown iff EVERY tile in the region is analyzed
    // (defaults true in the prototype → effectively always shown).
    private static bool AllTilesAnalyzed(int regionID)
    {
        if (TileManager.Instance == null) return true;
        var tiles = TileManager.Instance.GetTilesInRegion(regionID);
        if (tiles == null) return true;
        foreach (Tile t in tiles)
            if (t != null && !t.isAnalyzed) return false;
        return true;
    }

    // The designated runtime consumer of TileEntitySO.displayName: the panel names
    // whatever occupies the tile, or clears the label when the tile is empty.
    private void PopulateEntity(Tile tile)
    {
        if (entityNameText == null) return;

        entityNameText.text = (tile.entity != null && tile.entity.def != null)
            ? tile.entity.def.displayName
            : "";
    }

    // DORMANT (2026-07-15): no callers — issues are no longer surfaced in the
    // inspect panel. Kept intact (with FormatIssueType) for possible revival.
    private void PopulateIssues(Tile tile)
    {
        issuesSectionRoot.SetActive(true);

        if (tile.issuesRevealed)
        {
            issuesLockedText.gameObject.SetActive(false);
            issuesText.gameObject.SetActive(true);

            if (tile.issues == null || tile.issues.Count == 0)
            {
                issuesText.text = "No issues detected.";
            }
            else
            {
                var sb = new StringBuilder();
                foreach (var issue in tile.issues)
                    sb.AppendLine($"• {FormatIssueType(issue.type)}");
                issuesText.text = sb.ToString().TrimEnd();
            }
        }
        else
        {
            issuesText.gameObject.SetActive(false);
            issuesLockedText.gameObject.SetActive(true);
            issuesLockedText.text  = "Run <b>Ecological Survey</b> to reveal issues.";
            issuesLockedText.color = ColorLocked;
        }
    }

    // Shared substat renderer for both the tile path and the region path — fed the 6 values
    // (same order as substatIndicators) plus whether they should be revealed.
    private void PopulateSubstatsValues(float[] values, bool analyzed)
    {
        substatsSectionRoot.SetActive(true);

        if (analyzed)
        {
            substatsLockedText.gameObject.SetActive(false);

            for (int i = 0; i < substatIndicators.Length; i++)
            {
                if (i < substatTooltips.Length && substatTooltips[i] != null)
                    substatTooltips[i].SetValue(values[i]);

                // t = 1 → green (healthy), t = 0 → red (degraded)
                float t = Mathf.Clamp01(values[i] / 100f);

                // Icon is a STATIC sprite as of 2026-07-23 — no value tint, just ensure it's shown.
                if (substatIndicators[i] != null)
                    substatIndicators[i].gameObject.SetActive(true);

                if (i < substatBars.Length && substatBars[i] != null)
                    substatBars[i].value = values[i]; // 0–100 Slider

                if (i < substatValueTexts.Length && substatValueTexts[i] != null)
                    substatValueTexts[i].text = $"{values[i]:F0}%";

                // The wrapper (bar + value text container) carries the ONLY value tint left in the row.
                if (i < substatBarWrappers.Length && substatBarWrappers[i] != null)
                    substatBarWrappers[i].color = ColorUtils.LerpHSV(SubstatRed, SubstatGreen, t);
            }
        }
        else
        {
            substatsLockedText.gameObject.SetActive(true);
            substatsLockedText.text  = "Run <b>Soil Analysis</b> to reveal substats.";
            substatsLockedText.color = ColorLocked;

            // Icon stays a static sprite while locked too — no grey-out anymore.

            foreach (var tooltip in substatTooltips)
            {
                if (tooltip == null) continue;
                tooltip.SetUnknown();
            }

            // Bars/value texts mirror the tooltip's locked fallback: emptied bar, "?" readout.
            // Labels are left alone — the name stays visible even while locked.
            for (int i = 0; i < substatBars.Length; i++)
            {
                if (substatBars[i] == null) continue;
                substatBars[i].value = 0f;
            }

            for (int i = 0; i < substatValueTexts.Length; i++)
            {
                if (substatValueTexts[i] == null) continue;
                substatValueTexts[i].text = "?";
            }

            for (int i = 0; i < substatBarWrappers.Length; i++)
            {
                if (substatBarWrappers[i] == null) continue;
                substatBarWrappers[i].color = ColorLocked;
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string FormatIssueType(IssueType type) => type switch
    {
        IssueType.LoggedTrees             => "Logged Trees",
        IssueType.NutrientDepletion       => "Nutrient Depletion",
        IssueType.HeavyMetalContamination => "Heavy Metal Contamination",
        IssueType.ActiveErosion           => "Active Erosion",
        IssueType.DrainageCollapse        => "Drainage Collapse",
        IssueType.SoilCompaction          => "Soil Compaction",
        IssueType.ChemicalBurnout         => "Chemical Burnout",
        _                                 => type.ToString()
    };
}











