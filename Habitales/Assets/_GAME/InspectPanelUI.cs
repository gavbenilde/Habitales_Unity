using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text;
using Habitales.UI;   // IUISubsystem

/// <summary>
/// Always-on side panel. Listens to TileSelector and populates from the
/// currently selected tile (or shows the empty state when nothing is selected).
/// Health is always displayed. Substats and issues are gated on tile flags
/// (isAnalyzed / issuesRevealed) but those default to true in the prototype.
/// Hide() is intentionally a no-op so external callers can't deactivate it.
/// Each substat row has a pre-built Image that lerps green → red via LerpHSV.
/// </summary>
public class InspectPanelUI : MonoBehaviour, IUISubsystem
{
    // ── IUISubsystem ─────────────────────────────────────────────────────────
    //
    // Hide() (below) is deliberately a no-op — a prototype decision that the panel
    // is always on for gameplay callers (InspectModeManager, legacy ActionUI).
    // SetVisible, by contrast, is the hub's hide-ALL-UI path for clean end-report
    // screenshots — it must genuinely toggle panelRoot even though Hide() won't,
    // otherwise the panel would be stuck on-screen during a screenshot. The two
    // are separate concerns: Hide() is a gameplay no-op, SetVisible is the hub's
    // master-visibility switch.

    public string SubsystemId => "inspectPanel";
    public bool   IsVisible   => panelRoot != null && panelRoot.activeSelf;
    public void   SetVisible(bool visible)
    {
        if (panelRoot != null) panelRoot.SetActive(visible);
    }

    [Header("Panel Root")]
    [SerializeField] private GameObject panelRoot;

    [Header("Systems")]
    [SerializeField] private TileSelector tileSelector;

    [Header("Empty State")]
    [SerializeField] private GameObject emptyStateRoot;

    [Header("Health")]
    [SerializeField] private TextMeshProUGUI healthValueText;
    [SerializeField] private TextMeshProUGUI healthStatusText;
    [SerializeField] private Image healthBarFill; // optional, can be null

    [Header("Entity")]
    [Tooltip("Optional: shows the selected tile's entity display name (TileEntitySO.displayName). Blank when the tile is empty.")]
    [SerializeField] private TextMeshProUGUI entityNameText; // optional, can be null

    [Header("Issues Section")]
    [SerializeField] private GameObject issuesSectionRoot;
    [SerializeField] private TextMeshProUGUI issuesText;
    [SerializeField] private TextMeshProUGUI issuesLockedText;

    [Header("Substats Section")]
    [SerializeField] private GameObject substatsSectionRoot;
    [SerializeField] private TextMeshProUGUI substatsLockedText;

    [Header("Substat Row Images (soil composite, in order)")]
    [Tooltip("Assign in order: NutrientBalance, SoilOrganicMatter, SoilStructure, BiologicalActivity, WaterDynamics, ErosionResistance")]
    [SerializeField] private Image[] substatIndicators = new Image[6];

    // ── Colors ───────────────────────────────────────────────────────────────

    private static readonly Color ColorThriving = new Color(0.26f, 0.48f, 0.13f);
    private static readonly Color ColorDegraded = new Color(0.85f, 0.44f, 0.10f);
    private static readonly Color ColorCritical = new Color(0.85f, 0.20f, 0.15f);
    private static readonly Color ColorLocked   = new Color(0.55f, 0.55f, 0.55f);

    private static readonly Color SubstatGreen  = new Color(0.26f, 0.72f, 0.20f);
    private static readonly Color SubstatRed    = new Color(0.85f, 0.18f, 0.12f);

    // ── Lifecycle ────────────────────────────────────────────────────────────

    void Awake()
    {
        if (tileSelector == null)
            tileSelector = FindObjectOfType<TileSelector>();

        Show();
        ShowEmpty();
    }

    void OnEnable()
    {
        if (tileSelector != null)
        {
            tileSelector.OnTileSelected   += HandleTileSelected;
            tileSelector.OnTileDeselected += HandleTileDeselected;
        }
    }

    void OnDisable()
    {
        if (tileSelector != null)
        {
            tileSelector.OnTileSelected   -= HandleTileSelected;
            tileSelector.OnTileDeselected -= HandleTileDeselected;
        }
    }

    private void HandleTileSelected(Tile tile, Vector3 _) => Populate(tile);
    private void HandleTileDeselected() => ShowEmpty();

    // ── Panel lifecycle ──────────────────────────────────────────────────────

    public void Show()
    {
        panelRoot.SetActive(true);
        ShowEmpty();
    }

    public void Hide()
    {
        // No-op in prototype: panel stays visible. Kept for callers
        // (InspectModeManager, ActionUI legacy) that still invoke it.
    }

    public void ShowEmpty()
    {
        emptyStateRoot.SetActive(true);
        issuesSectionRoot.SetActive(false);
        substatsSectionRoot.SetActive(false);

        healthValueText.text  = "—";
        healthStatusText.text = "";
        if (healthBarFill != null) healthBarFill.fillAmount = 0f;
        if (entityNameText != null) entityNameText.text = "";
    }

    // ── Main populate ────────────────────────────────────────────────────────

    public void Populate(Tile tile)
    {
        if (tile == null) { ShowEmpty(); return; }

        emptyStateRoot.SetActive(false);

        PopulateHealth(tile);
        PopulateEntity(tile);
        PopulateIssues(tile);
        PopulateSubstats(tile);
    }

    // ── Section builders ─────────────────────────────────────────────────────

    private void PopulateHealth(Tile tile)
    {
        float health = tile.stats.CalculateHealth();
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

    // The designated runtime consumer of TileEntitySO.displayName: the panel names
    // whatever occupies the tile, or clears the label when the tile is empty.
    private void PopulateEntity(Tile tile)
    {
        if (entityNameText == null) return;

        entityNameText.text = (tile.entity != null && tile.entity.def != null)
            ? tile.entity.def.displayName
            : "";
    }

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

    private void PopulateSubstats(Tile tile)
    {
        substatsSectionRoot.SetActive(true);

        if (tile.isAnalyzed)
        {
            substatsLockedText.gameObject.SetActive(false);

            TileStats s = tile.stats;

            // Order must match substatIndicators array in the Inspector:
            // [0] NutrientBalance  [1] SoilOrganicMatter  [2] SoilStructure
            // [3] BiologicalActivity  [4] WaterDynamics  [5] ErosionResistance
            float[] values =
            {
                s.nutrientBalance,
                s.soilOrganicMatter,
                s.soilStructure,
                s.biologicalActivity,
                s.waterDynamics,
                s.erosionResistance
            };

            for (int i = 0; i < substatIndicators.Length; i++)
            {
                if (substatIndicators[i] == null) continue;

                // t = 1 → green (healthy), t = 0 → red (degraded)
                float t = Mathf.Clamp01(values[i] / 100f);
                substatIndicators[i].color = ColorUtils.LerpHSV(SubstatRed, SubstatGreen, t);
                substatIndicators[i].gameObject.SetActive(true);
            }
        }
        else
        {
            substatsLockedText.gameObject.SetActive(true);
            substatsLockedText.text  = "Run <b>Soil Analysis</b> to reveal substats.";
            substatsLockedText.color = ColorLocked;

            // Grey out all indicators while locked
            foreach (var indicator in substatIndicators)
            {
                if (indicator == null) continue;
                indicator.color = ColorLocked;
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











