using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Habitales.Meta;

/// <summary>
/// DEV-ONLY: summons the End Report screen on demand with synthetic data, so the whole
/// screen — staged reveal ceremony + results-minipanel carousel + grade variants — can be
/// eyeballed without playing a full run to game-over.
///
/// Deliberately decoupled from the real run-end path (RunManager.TriggerGameOver /
/// BuildEndGameData are private and drag in the End Conversation + live managers). This
/// builds its own <see cref="EndGameData"/> and calls the public <see cref="EndGameScreenUI.Show"/>
/// directly — no live run required.
///
/// Fires three ways, pick whichever is least effort:
///   • Right-click this component in the Inspector → "Show End Screen (Debug)" (zero wiring).
///   • Assign <c>triggerButton</c> for an on-screen button.
///   • Press <c>hotkey</c> (default F9; editor-only) in Play Mode.
///
/// Flip <c>previewGrade</c> to preview the Collapse (muted) vs non-Collapse (celebratory)
/// presentation variants — endReason is derived to match.
/// </summary>
public class EndGameScreenDebugTrigger : MonoBehaviour
{
    [Header("Triggers (all optional — context-menu always works)")]
    [Tooltip("Optional on-screen Button. Its onClick summons the end screen.")]
    [SerializeField] private Button triggerButton;
    [Tooltip("Editor-only hotkey. Set to None to disable.")]
    [SerializeField] private KeyCode hotkey = KeyCode.F9;

    [Header("Preview Data")]
    [Tooltip("Collapse → muted variant + 'Ecosystem Collapse'; anything else → celebratory + 'Field Season Complete'.")]
    [SerializeField] private SeasonGrade previewGrade = SeasonGrade.Commendable;
    [Tooltip("How many synthetic health-history points to generate (drives the sparkline sweep).")]
    [SerializeField] private int historyPoints = 120;
    [SerializeField] private int thrivingCount = 14;
    [SerializeField] private int degradedCount = 6;
    [SerializeField] private int criticalCount = 2;

    private void OnEnable()
    {
        if (triggerButton != null) triggerButton.onClick.AddListener(ShowEndScreen);
    }

    private void OnDisable()
    {
        if (triggerButton != null) triggerButton.onClick.RemoveListener(ShowEndScreen);
    }

    private void Update()
    {
    #if UNITY_EDITOR
        if (hotkey != KeyCode.None && Input.GetKeyDown(hotkey))
            ShowEndScreen();
    #endif
    }

    [ContextMenu("Show End Screen (Debug)")]
    public void ShowEndScreen()
    {
        if (EndGameScreenUI.Instance == null)
        {
            Debug.LogError("[EndGameScreenDebugTrigger] EndGameScreenUI.Instance is null — no end screen in the scene to show. Is the EndGameScreenUI GameObject present and active?", this);
            return;
        }

        Debug.Log($"[EndGameScreenDebugTrigger] Showing end screen with synthetic data (grade={previewGrade}).");
        EndGameScreenUI.Instance.Show(BuildDebugData());
    }

    private EndGameData BuildDebugData()
    {
        bool collapse = previewGrade == SeasonGrade.Collapse;

        var history = BuildSyntheticHistory(Mathf.Max(historyPoints, 2), collapse, out int peakAtDay);

        return new EndGameData
        {
            endReason      = collapse ? "Ecosystem Collapse" : "Field Season Complete",
            aziSummaryLine = "[debug] Azi summary line.",
            currentYear    = 1,
            totalDays      = history.Count,
            worldHealth    = history[history.Count - 1],
            healthHistory  = history,

            thrivingCount = thrivingCount,
            degradedCount = degradedCount,
            criticalCount = collapse ? Mathf.Max(criticalCount, thrivingCount + degradedCount * 4) : criticalCount,

            peakThrivingCount = thrivingCount,
            peakScreenshot    = null, // RawImage shows empty — fine for a debug preview
            peakAtDay         = peakAtDay,

            // DORMANT (2026-07-18): level-ups cut — EndGameData.xpEarned/xpBefore removed.

            seasonGrade = previewGrade,

            zoneHealths = new Dictionary<int, float>
            {
                { 0, collapse ? 18f : 82f },
                { 1, collapse ? 12f : 64f },
                { 2, collapse ? 25f : 71f },
            },

            topWorker = null, // PopulateWorker handles null → shows "—"

            favouriteAction   = "Plant Sunflower",
            mostAvoidedAction = "Controlled Burn",
            mostChattedWorker = "Bob",
        };
    }

    /// <summary>
    /// A deterministic curve so the sparkline sweep looks like a real run: a noisy rise
    /// toward a peak, then (on collapse) a sharp fall. Returns the peak index in
    /// <paramref name="peakAtDay"/> so the peak marker lands on the high-water mark.
    /// </summary>
    private static List<float> BuildSyntheticHistory(int count, bool collapse, out int peakAtDay)
    {
        var list = new List<float>(count);
        int peakIndex = collapse ? Mathf.RoundToInt(count * 0.55f) : count - 1;
        float peakValue = 0f;
        peakAtDay = 0;

        for (int i = 0; i < count; i++)
        {
            float t = count <= 1 ? 0f : i / (float)(count - 1);
            float baseline;

            if (collapse)
            {
                // Rise to ~80 around the peak index, then crash toward ~10.
                float peakT = peakIndex / (float)(count - 1);
                baseline = t <= peakT
                    ? Mathf.Lerp(35f, 80f, t / Mathf.Max(peakT, 0.0001f))
                    : Mathf.Lerp(80f, 10f, (t - peakT) / Mathf.Max(1f - peakT, 0.0001f));
            }
            else
            {
                baseline = Mathf.Lerp(30f, 85f, t); // steady climb
            }

            // Deterministic pseudo-noise (no Random → same curve every press).
            float noise = Mathf.Sin(i * 1.37f) * 3.5f;
            float value = Mathf.Clamp(baseline + noise, 0f, 100f);
            list.Add(value);

            if (value >= peakValue) { peakValue = value; peakAtDay = i; }
        }

        return list;
    }
}
