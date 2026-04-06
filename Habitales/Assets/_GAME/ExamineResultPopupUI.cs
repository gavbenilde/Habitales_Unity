using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Blocking overlay popup shown after an Examine action completes.
/// Dismissed by clicking the overlay or pressing Escape.
/// Trigger via ExamineResultPopupUI.Instance.ShowExamineResult(tiles, type).
/// </summary>
public class ExamineResultPopupUI : MonoBehaviour
{
    public static ExamineResultPopupUI Instance { get; private set; }

    [Header("Roots")]
    [SerializeField] private GameObject popupRoot;       // the whole overlay, toggled active/inactive
    [SerializeField] private Button overlayButton;       // full-screen transparent button behind the card

    [Header("Content")]
    [SerializeField] private TextMeshProUGUI titleText;   // e.g. "Ecological Survey Complete"
    [SerializeField] private TextMeshProUGUI summaryText; // the generated body

    private bool _isOpen;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        popupRoot.SetActive(false);
        overlayButton.onClick.AddListener(Dismiss);
    }

    void Update()
    {
        if (_isOpen && Input.GetKeyDown(KeyCode.Escape))
            Dismiss();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Call from ActionManager.FinishAction after ApplyFatigue, before OnActionCompleted.
    /// </summary>
    public void ShowExamineResult(List<Tile> affectedTiles, ExamineActionType examineType)
    {
        if (affectedTiles == null || affectedTiles.Count == 0) return;

        titleText.text = examineType switch
        {
            ExamineActionType.EcologicalSurvey => "Ecological Survey Complete",
            ExamineActionType.SoilAnalysis     => "Soil Analysis Complete",
            ExamineActionType.InspectTrash     => "Trash Inspection Complete",
            _                                  => "Examine Complete"
        };

        summaryText.text = examineType switch
        {
            ExamineActionType.EcologicalSurvey => BuildIssueSummary(affectedTiles),
            ExamineActionType.SoilAnalysis     => BuildSoilSummary(affectedTiles),
            ExamineActionType.InspectTrash     => BuildTrashSummary(affectedTiles),
            _                                  => "Examination complete."
        };

        popupRoot.SetActive(true);
        _isOpen = true;
    }

    public void Dismiss()
    {
        popupRoot.SetActive(false);
        _isOpen = false;
    }

    // ── Summary builders ─────────────────────────────────────────────────────

    private string BuildIssueSummary(List<Tile> tiles)
    {
        // Count how many tiles have each IssueType
        var issueCounts = new Dictionary<IssueType, int>();
        int totalTiles  = tiles.Count;

        foreach (var tile in tiles)
        {
            if (tile.issues == null) continue;
            foreach (var issue in tile.issues)
            {
                if (!issueCounts.ContainsKey(issue.type))
                    issueCounts[issue.type] = 0;
                issueCounts[issue.type]++;
            }
        }

        if (issueCounts.Count == 0)
            return "The survey found <b>no significant issues</b> in this area.";

        // Sort by prevalence descending, show top 3
        var sorted = new List<KeyValuePair<IssueType, int>>(issueCounts);
        sorted.Sort((a, b) => b.Value.CompareTo(a.Value));

        var sb = new StringBuilder();
        int shown = Mathf.Min(sorted.Count, 3);

        for (int i = 0; i < shown; i++)
        {
            IssueType type  = sorted[i].Key;
            int       count = sorted[i].Value;
            float     ratio = (float)count / totalTiles;
            sb.AppendLine($"{Prevalence(ratio)} of the tiles show <b>{FormatIssueType(type)}</b>.");
        }

        return sb.ToString().TrimEnd();
    }

    private string BuildSoilSummary(List<Tile> tiles)
    {
        float avgComposite = 0f;
        float avgContam    = 0f;

        foreach (var tile in tiles)
        {
            avgComposite += tile.stats.soilComposite;
            avgContam    += tile.stats.contamination;
        }
        avgComposite /= tiles.Count;
        avgContam    /= tiles.Count;

        string soilLabel = avgComposite >= 67f ? "healthy"
                         : avgComposite >= 33f ? "moderately degraded"
                         :                       "severely degraded";

        string contamLine = avgContam > 60f
            ? "\n\nContamination levels are <b>critically high</b> — immediate remediation recommended."
            : avgContam > 30f
            ? "\n\nSome contamination is present in the soil."
            : "";

        return $"On average, the soil in this area is <b>{soilLabel}</b> " +
               $"(composite: {avgComposite:F1}).{contamLine}";
    }

    private string BuildTrashSummary(List<Tile> tiles)
    {
        int bio    = 0;
        int nonBio = 0;

        foreach (var tile in tiles)
        {
            if (tile.entity is TrashBioEntity)    bio++;
            if (tile.entity is TrashNonBioEntity) nonBio++;
        }

        if (bio == 0 && nonBio == 0)
            return "The inspection found <b>no notable debris</b> in this area.";

        var sb = new StringBuilder();
        int total = tiles.Count;

        if (bio > 0)
            sb.AppendLine(
                $"{Prevalence((float)bio / total)} of the inspected tiles contain " +
                $"<b>biodegradable waste</b> that will self-degrade over time.");

        if (nonBio > 0)
            sb.AppendLine(
                $"{Prevalence((float)nonBio / total)} of the inspected tiles contain " +
                $"<b>non-biodegradable waste</b> that must be manually cleared.");

        return sb.ToString().TrimEnd();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Returns "Most", "Around half", or "Some" based on tile ratio.</summary>
    private static string Prevalence(float ratio)
    {
        if (ratio >= 0.66f) return "Most";
        if (ratio >= 0.40f) return "Around half";
        return "Some";
    }

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

/// <summary>Passed to ShowExamineResult to select the right summary builder.</summary>
public enum ExamineActionType
{
    EcologicalSurvey,
    SoilAnalysis,
    InspectTrash
}
