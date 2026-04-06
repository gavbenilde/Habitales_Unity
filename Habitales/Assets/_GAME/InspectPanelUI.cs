using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text;

/// <summary>
/// Side panel shown during Inspect Mode.
/// Health is always displayed.
/// Issues are gated on tile.issuesRevealed.
/// Substats (soil composite only — 6 stats) are gated on tile.isAnalyzed.
/// Each substat row has a pre-built Image that lerps green → red via LerpHSV.
/// </summary>
public class InspectPanelUI : MonoBehaviour
{
    [Header("Panel Root")]
    [SerializeField] private GameObject panelRoot;

    [Header("Empty State")]
    [SerializeField] private GameObject emptyStateRoot;

    [Header("Health")]
    [SerializeField] private TextMeshProUGUI healthValueText;
    [SerializeField] private TextMeshProUGUI healthStatusText;
    [SerializeField] private Image healthBarFill; // optional, can be null

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

    // ── Panel lifecycle ──────────────────────────────────────────────────────

    public void Show()
    {
        panelRoot.SetActive(true);
        ShowEmpty();
    }

    public void Hide()
    {
        panelRoot.SetActive(false);
    }

    public void ShowEmpty()
    {
        emptyStateRoot.SetActive(true);
        issuesSectionRoot.SetActive(false);
        substatsSectionRoot.SetActive(false);

        healthValueText.text  = "—";
        healthStatusText.text = "";
        if (healthBarFill != null) healthBarFill.fillAmount = 0f;
    }

    // ── Main populate ────────────────────────────────────────────────────────

    public void Populate(Tile tile)
    {
        if (tile == null) { ShowEmpty(); return; }

        emptyStateRoot.SetActive(false);

        PopulateHealth(tile);
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











