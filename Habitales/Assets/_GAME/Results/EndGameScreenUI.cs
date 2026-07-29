using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Habitales.Dialogue;
using Habitales.Meta;
using Habitales.UI;   // IUISubsystem

/// <summary>
/// Full-screen end-game panel. Add to your existing Canvas at a high Sort Order.
/// Structure: singleton, Show(data) / Hide().
/// Has a minimize button so the player can peek at the map without closing.
/// </summary>
public class EndGameScreenUI : MonoBehaviour, IUISubsystem
{
    // ── IUISubsystem ─────────────────────────────────────────────────────────
    //
    // Maps straight onto the existing overlayPanel root toggle that Show()/Hide()
    // already drive — SetVisible(true) does NOT call Show(data) (no data to show,
    // and re-triggering the staged reveal ceremony from a screenshot restore
    // would be wrong), it only reactivates whatever was already populated and
    // on-screen. SetVisible(false) does not go through Hide() either, because
    // Hide() also calls StopReveal() (cancels the in-flight reveal coroutine +
    // tweens) — a mid-ceremony hide-all-for-screenshot should not kill the
    // reveal, it should resume exactly where it left off once restored. So
    // SetVisible is a passive SetActive on overlayPanel only, leaving the reveal
    // coroutine (which mutates its own elements regardless of overlayPanel's
    // active state) running untouched underneath.

    public string SubsystemId => "endGameScreen";
    public bool   IsVisible   => overlayPanel != null && overlayPanel.activeSelf;
    public void   SetVisible(bool visible)
    {
        if (overlayPanel != null) overlayPanel.SetActive(visible);
    }

    public static EndGameScreenUI Instance { get; private set; }

    [Header("Panel Roots")]
    [SerializeField] private GameObject overlayPanel;   // root — toggled by Show/Hide

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI endReasonText;
    [SerializeField] private TextMeshProUGUI worldHealthText;
    [SerializeField] private TextMeshProUGUI yearDayText;

    [Header("Grade Stamp (OPTIONAL — screen still works unwired, see Show())")]
    [Tooltip("Root of the grade-stamp element. Placeholder = TMP text in a bordered box; final art swaps into this slot.")]
    [SerializeField] private GameObject      gradeStampRoot;
    [SerializeField] private TextMeshProUGUI gradeStampText;
    [SerializeField] private string          gradeStampTemplate = "HQ rates this expedition: {0}";

    [Header("Season Sparkline (OPTIONAL — screen still works unwired, see Show())")]
    [Tooltip("Renders data.thrivingHistory (thriving-tile count per day) with a peak-day marker at data.peakAtDay. Reveals right before the grade stamp.")]
    [SerializeField] private HealthSparklineUI seasonSparkline;

    [Header("Presentation Variants")]
    [Tooltip("Applied when data.seasonGrade == SeasonGrade.Collapse.")]
    [SerializeField] private EndScreenStyle collapseStyle = new EndScreenStyle();
    [Tooltip("Applied for every non-Collapse grade (Season Complete).")]
    [SerializeField] private EndScreenStyle seasonCompleteStyle = new EndScreenStyle();
    [Tooltip("Optional — tinted by the active style's headerTint. Leave null to skip.")]
    [SerializeField] private Image headerBackground;
    [Tooltip("Optional — swapped to the active style's aziMood sprite. Leave null to skip.")]
    [SerializeField] private Image aziMoodImage;

    [Header("Zone Pills")]
    [SerializeField] private Transform  zonePillContainer;
    [SerializeField] private ZonePillUI zonePillPrefab;

    [Header("Snapshot")]
    [SerializeField] private RawImage snapshotImage;

    [Header("Tile Counts")]
    [SerializeField] private TextMeshProUGUI thrivingCountText;
    [SerializeField] private TextMeshProUGUI degradedCountText;
    [SerializeField] private TextMeshProUGUI criticalCountText;
    [SerializeField] private TextMeshProUGUI peakThrivingText;
    
    [Header("Stamp Images")]
    [SerializeField] private Image passedStampImage;
    [SerializeField] private Image failedStampImage;

    [Header("Mood Images")]
    [SerializeField] private Image aziHappyImage;
    [SerializeField] private Image aziSadImage;
    
    [Header("Employee of the Year")]
    [SerializeField] private Image           workerPortraitImage; // active when StockPhoto
    [SerializeField] private GameObject      workerInitialRoot;   // active when GeneratedInitial
    [SerializeField] private Image           workerInitialBg;     // tinted with worker.initialColor
    [SerializeField] private TextMeshProUGUI workerInitialText;   // first letter of worker name
    [SerializeField] private TextMeshProUGUI workerNameText;
    [SerializeField] private TextMeshProUGUI workerTraitText;
    [SerializeField] private TextMeshProUGUI workerActionsText;

    [Header("Silly Stats")]
    [SerializeField] private TextMeshProUGUI favouriteActionText;
    [SerializeField] private TextMeshProUGUI mostAvoidedText;
    [SerializeField] private TextMeshProUGUI mostChattedText;
    [SerializeField] private TextMeshProUGUI commentsText;

    [Header("Comment Message")]
    [SerializeField] private string passComments;
    [SerializeField] private string failComments;

    [Header("Results Minipanel Carousel (OPTIONAL — unwired falls back to the flat layout)")]
    // WIRING (human): minipanelRoot is one shared container holding 3 slide roots that all
    // occupy the same rect (snapshotSlide / sparklineSlide / sillyStatsSlide), plus
    // slidePrevButton/slideNextButton as children of that same container. Move the existing
    // snapshotImage into snapshotSlide, seasonSparkline into sparklineSlide, and the
    // favouriteAction/mostAvoided/mostChatted texts into sillyStatsSlide. Only one slide is
    // SetActive at a time; minipanelRoot itself (buttons included) is what the reveal
    // ceremony fades in as a single unit.
    [Tooltip("Shared container: 3 slides + nav buttons. Fades in as one unit during the reveal ceremony.")]
    [SerializeField] private GameObject minipanelRoot;
    [SerializeField] private GameObject snapshotSlide;    // slide 0 root — contains snapshotImage in-scene
    [SerializeField] private GameObject sparklineSlide;   // slide 1 root — contains seasonSparkline in-scene
    [SerializeField] private GameObject sillyStatsSlide;  // slide 2 root — contains the silly-stat texts in-scene
    [SerializeField] private Button     slidePrevButton;
    [SerializeField] private Button     slideNextButton;
    [SerializeField] private float      sparklineSweepSeconds = 1.2f; // sweep duration each time slide 1 is shown
    [SerializeField] private float      minipanelFadeSeconds  = 0.4f; // ceremony fade-in of the whole minipanel

    [Header("Footer")]
    [SerializeField] private Button          playAgainButton;        // reloads the run scene for a fresh attempt
    [SerializeField] private Button          exitToMainMenuButton;   // exits to the main menu (or prototype menu — see toggle)
    [SerializeField] private Button          minimizeButton;

    [Header("Menu Routing")]
    [Tooltip("When true, the Exit button loads 'PrototypeMenu' instead of 'Main Menu'. Use during development.")]
    [SerializeField] private bool usePrototypeMenu = false;
    [SerializeField] private string mainMenuSceneName      = "Main Menu";
    [SerializeField] private string prototypeMenuSceneName = "PrototypeMenu";

    [Header("Staged Reveal Ceremony")]
    [Tooltip("When true, Show() plays the staged reveal coroutine. When false, everything lands instantly (skip state).")]
    [SerializeField] private bool playStagedReveal = true;
    [Tooltip("Optional — an invisible full-rect Button over the panel that skips the reveal on click. Leave null to disable click-to-skip.")]
    [SerializeField] private Button skipCatcherButton;
    [SerializeField] private float zonePillStaggerSeconds = 0.15f;
    [SerializeField] private float tileCountCountUpSeconds = 0.5f;
    [SerializeField] private float snapshotFadeSeconds = 0.4f;
    [SerializeField] private float employeeSlideSeconds = 0.45f;
    [SerializeField] private float sillyStatsFadeSeconds = 0.3f;
    [SerializeField] private float sparklineFadeSeconds = 0.3f;
    [SerializeField] private float gradeStampPunchSeconds = 0.5f;

    private bool isMinimized;
    private EndGameData _lastData;
    private Coroutine _revealRoutine;
    private bool _revealInProgress;
    private bool _gradeStampWarned;
    private bool _sparklineWarned;

    private const int SlideCount = 3;
    private int  _slideIndex;
    private bool _carouselWarned;

    // -------------------------------------------------------------------------
    // Presentation style block
    // -------------------------------------------------------------------------

    [Serializable]
    public class EndScreenStyle
    {
        public Color  headerTint       = Color.white;
        public Sprite aziMood;
        public bool   showCelebration  = true;
    }

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        // if (Instance != null && Instance != this) { Debug.Log("Destroyed"); Destroy(gameObject); return; }
        // Instance = this;
        overlayPanel.SetActive(false);
    }

    private void OnEnable()
    {
        // if (minimizeButton       != null) minimizeButton.onClick.AddListener(ToggleMinimize);
        if (playAgainButton      != null) playAgainButton.onClick.AddListener(OnPlayAgain);
        if (exitToMainMenuButton != null) exitToMainMenuButton.onClick.AddListener(OnExitToMainMenu);
        if (skipCatcherButton    != null) skipCatcherButton.onClick.AddListener(SkipReveal);
        if (slidePrevButton      != null) slidePrevButton.onClick.AddListener(OnPrevSlide);
        if (slideNextButton      != null) slideNextButton.onClick.AddListener(OnNextSlide);
    }

    private void OnDisable()
    {
        // if (minimizeButton       != null) minimizeButton.onClick.RemoveListener(ToggleMinimize);
        if (playAgainButton      != null) playAgainButton.onClick.RemoveListener(OnPlayAgain);
        if (exitToMainMenuButton != null) exitToMainMenuButton.onClick.RemoveListener(OnExitToMainMenu);
        if (skipCatcherButton    != null) skipCatcherButton.onClick.RemoveListener(SkipReveal);
        if (slidePrevButton      != null) slidePrevButton.onClick.RemoveListener(OnPrevSlide);
        if (slideNextButton      != null) slideNextButton.onClick.RemoveListener(OnNextSlide);
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public void Show(EndGameData data)
    {
        // Self-heal + diagnostics — mirror the AziSpeechBubble pattern so a busted ref
        // can't silently swallow the run-end UI.
        if (!gameObject.activeSelf)
        {
            Debug.LogWarning("[EndGameScreenUI] Root GameObject was inactive — auto-enabling.");
            gameObject.SetActive(true);
        }

        Debug.Log($"[EndGameScreenUI.Show] activeInHierarchy={gameObject.activeInHierarchy} | overlayPanel={(overlayPanel != null)} | endReasonText={(endReasonText != null)} | playAgainButton={(playAgainButton != null)}");

        if (overlayPanel == null)
        {
            Debug.LogError("[EndGameScreenUI] overlayPanel or fullContent is NULL — panel can't activate. Wire them on the EndGameScreenUI GameObject. Falling back to direct Main Menu load.");
            SceneManager.LoadScene("Main Menu");
            return;
        }

        // StopReveal();

        // PASS OR FAIL image
        if (data.hasCollapsed)
        {
            failedStampImage.gameObject.SetActive(true);
            aziSadImage.gameObject.SetActive(true);
            commentsText.text = failComments;
        }
        else
        {
            passedStampImage.gameObject.SetActive(true);
            aziHappyImage.gameObject.SetActive(true);
            commentsText.text = passComments;
        }
        
        _lastData = data;
        Populate(data);
        ApplyPresentationStyle(data);
        if (IsCarouselWired()) ShowSlide(0);
        isMinimized = false;
        overlayPanel.SetActive(true);

        if (skipCatcherButton != null) skipCatcherButton.gameObject.SetActive(playStagedReveal);

        if (playStagedReveal)
        {
            _revealRoutine = StartCoroutine(PlayRevealCeremony(data));
        }
        else
        {
            FinishReveal(data);
        }
    }

    public void Hide()
    {
        StopReveal();
        overlayPanel.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Minimize toggle — lets player peek at the map without closing the screen
    // -------------------------------------------------------------------------

    // private void ToggleMinimize()
    // {
    //     bool showing = !fullContent.activeSelf;
    //     fullContent.SetActive(showing);
    //     minimizeButton.GetComponentInChildren<TextMeshProUGUI>().text = showing ? "−" : "+";
    // }

    // -------------------------------------------------------------------------
    // Play Again
    // -------------------------------------------------------------------------

    private void OnPlayAgain()
    {
        Hide();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void OnExitToMainMenu()
    {
        // Routes to the prototype menu during dev, or the real main menu otherwise.
        // DORMANT (2026-07-18): level-ups cut — MainMenu no longer shows a level-up overlay.
        Hide();
        // string target = usePrototypeMenu ? prototypeMenuSceneName : mainMenuSceneName;
        // SceneManager.LoadScene(target);
        SceneManager.LoadScene("Main Menu");
    }

    // -------------------------------------------------------------------------
    // Populate — pure data binding, no game logic
    // -------------------------------------------------------------------------

    private void Populate(EndGameData data)
    {
        // Header — progress toward the run's health TARGET, not raw world health: at a 65%
        // target a world health of 65 reads as 100%. Anything above the target still caps at
        // 100 (no 130% readouts).
        // endReasonText.text   = data.endReason;
        worldHealthText.text = $"{HeaderHealthPercent(data):F1}% World Health";
        // yearDayText.text     = $"Year {data.currentYear}, Day {data.totalDays}";

        // // Zone pills — clear old, spawn new
        // foreach (Transform child in zonePillContainer)
        //     Destroy(child.gameObject);
        //
        // foreach (var kvp in data.zoneHealths)
        // {
        //     var pill = Instantiate(zonePillPrefab, zonePillContainer);
        //     pill.Setup(kvp.Key, kvp.Value);
        // }

        // Snapshot — null texture leaves RawImage in its default (empty) state
        if (snapshotImage != null)
            snapshotImage.texture = data.peakScreenshot;

        // Tile counts
        thrivingCountText.text = data.thrivingCount.ToString();
        degradedCountText.text = data.degradedCount.ToString();
        criticalCountText.text = data.criticalCount.ToString();
        if (peakThrivingText != null)
            peakThrivingText.text = data.peakThrivingCount.ToString();

        // // Employee of the Year
        // PopulateWorker(data.topWorker);

        // // Silly stats
        favouriteActionText.text = Coalesce(data.favouriteAction);
        mostAvoidedText.text     = Coalesce(data.mostAvoidedAction);
        // mostChattedText.text     = Coalesce(data.mostChattedWorker);

        // Season sparkline — OPTIONAL-with-warning (Law 3 exception): this screen is already
        // live in-scene, so a missing sparkline ref must degrade gracefully, not brick Show().
        if (seasonSparkline != null)
        {
            seasonSparkline.SetHistory(BuildSparklineSeries(data), data.peakAtDay);
        }
        else if (!_sparklineWarned)
        {
            _sparklineWarned = true;
            Debug.LogWarning("[EndGameScreenUI] seasonSparkline is not wired — the season sparkline will not display. Drag a HealthSparklineUI into the Season Sparkline section (screen will otherwise still function).", this);
        }

        // // Grade stamp — OPTIONAL-with-warning (Law 3 exception): this screen is already
        // // live in-scene, so a missing stamp ref must degrade gracefully, not brick Show().
        // if (gradeStampText != null)
        // {
        //     gradeStampText.text = string.Format(gradeStampTemplate, data.seasonGrade);
        // }
        // else if (!_gradeStampWarned)
        // {
        //     _gradeStampWarned = true;
        //     Debug.LogWarning("[EndGameScreenUI] gradeStampText is not wired — the season grade will not display. Drag a TMP_Text into the Grade Stamp section (screen will otherwise still function).", this);
        // }
    }

    private void PopulateWorker(Worker worker)
    {
        if (worker == null)
        {
            workerPortraitImage.gameObject.SetActive(false);
            workerInitialRoot.SetActive(false);
            workerNameText.text    = "—";
            workerTraitText.text   = "";
            workerActionsText.text = "";
            return;
        }

        bool hasPhoto = worker.portraitType == WorkerPortraitType.StockPhoto
                        && worker.stockPhoto != null;

        workerPortraitImage.gameObject.SetActive(hasPhoto);
        workerInitialRoot.SetActive(!hasPhoto);

        if (hasPhoto)
        {
            workerPortraitImage.sprite = worker.stockPhoto;
        }
        else
        {
            workerInitialBg.color  = worker.initialColor;
            workerInitialText.text = worker.workerName.Substring(0, 1).ToUpper();
        }

        workerNameText.text    = worker.workerName;
        workerTraitText.text   = worker.trait.ToString();
        workerActionsText.text = $"{worker.actionsParticipated} actions";
    }

    private static string Coalesce(string value) =>
        string.IsNullOrEmpty(value) ? "—" : value;

    /// <summary>
    /// The header number: world health rescaled so <see cref="EndGameData.healthTarget"/> IS
    /// 100%. A non-positive target (or an unset one on a hand-built EndGameData) falls back to
    /// raw world health, so callers that don't know about the target still print something sane.
    /// </summary>
    private static float HeaderHealthPercent(EndGameData data)
    {
        if (data.healthTarget <= 0.01f) return data.worldHealth;
        return Mathf.Clamp(data.worldHealth / data.healthTarget * 100f, 0f, 100f);
    }

    /// <summary>
    /// What the season sparkline plots: the run's thriving-tile count per day — a number that
    /// climbs as tiles are healed and as new regions unlock, rather than the average-health
    /// percentage (which plateaus and reads as "nothing happened"). Deliberately the HONEST
    /// per-day count, not a running max: a bad stretch should visibly dip. Falls back to
    /// healthHistory only when no thriving series was recorded, so an older/hand-built
    /// EndGameData still draws something.
    /// </summary>
    private static IReadOnlyList<float> BuildSparklineSeries(EndGameData data)
    {
        if (data.thrivingHistory != null && data.thrivingHistory.Count > 0)
            return data.thrivingHistory;

        return data.healthHistory ?? new List<float>();
    }

    // -------------------------------------------------------------------------
    // Presentation variants — Collapse vs Season Complete
    // -------------------------------------------------------------------------

    private EndScreenStyle ActiveStyle(EndGameData data) =>
        data.seasonGrade == SeasonGrade.Collapse ? collapseStyle : seasonCompleteStyle;

    private void ApplyPresentationStyle(EndGameData data)
    {
        var style = ActiveStyle(data);

        if (headerBackground != null)
            headerBackground.color = style.headerTint;

        if (aziMoodImage != null)
        {
            if (style.aziMood != null)
            {
                aziMoodImage.sprite = style.aziMood;
                aziMoodImage.gameObject.SetActive(true);
            }
            else
            {
                aziMoodImage.gameObject.SetActive(false);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Staged reveal ceremony — zone pills stagger in, tile counts count up, Employee of
    // the Year slides in, grade stamp lands last with a scale-punch. Click-to-skip jumps
    // straight to FinishReveal(), which the coroutine itself also calls at the end —
    // every element must land on identical final values whichever path runs. Sim is
    // paused during this screen, so every tween/wait uses unscaled time.
    //
    // Two modes, chosen by IsCarouselWired():
    //   Carousel wired    — snapshot/silly-stats/sparkline fades are replaced by ONE
    //                        fade-in of the whole results minipanel (buttons included),
    //                        shown on slide 0. Order: pills → counts → employee →
    //                        minipanel fade → grade stamp.
    //   Carousel unwired  — legacy flat layout: pills → counts → snapshot fade → employee
    //                        → silly stats fade → sparkline fade → grade stamp, byte-for-byte
    //                        as it shipped before the carousel existed.
    // -------------------------------------------------------------------------

    private void StopReveal()
    {
        if (_revealRoutine != null)
        {
            StopCoroutine(_revealRoutine);
            _revealRoutine = null;
        }
        _revealInProgress = false;

        // Cancel any in-flight tweens on everything the ceremony touches so a re-Show
        // starts clean.
        if (zonePillContainer != null) LeanTween.cancel(zonePillContainer.gameObject, true);
        if (snapshotImage     != null) LeanTween.cancel(snapshotImage.gameObject);
        if (workerNameText    != null) LeanTween.cancel(workerNameText.gameObject);
        if (favouriteActionText != null) LeanTween.cancel(favouriteActionText.gameObject);
        if (seasonSparkline   != null) seasonSparkline.CancelProgressiveReveal(); // lands on the FULL polyline, never a partial sweep
        if (gradeStampRoot    != null) LeanTween.cancel(gradeStampRoot);
        if (minipanelRoot     != null) LeanTween.cancel(minipanelRoot);
    }

    private void SkipReveal()
    {
        if (!_revealInProgress || _lastData == null) return;
        StopReveal();
        FinishReveal(_lastData);
    }

    private IEnumerator PlayRevealCeremony(EndGameData data)
    {
        _revealInProgress = true;
        bool celebrate = ActiveStyle(data).showCelebration;
        bool carousel = IsCarouselWired();

        // Start everything hidden/zeroed; FinishReveal (skip path) sets these same
        // elements directly to final values, so the "start" state below must be the
        // only place pre-reveal values are set.
        SetZonePillsVisible(false);
        SetTileCountsTo(0, 0, 0);
        SetEmployeeAlpha(0f);
        if (carousel)
        {
            // Snapshot/silly-stats/sparkline now live inside slides — their own alphas
            // must stay at 1 (set at construction time / by Populate) or a slide swap
            // would show invisible content. Only the shared minipanel container fades.
            SetMinipanelAlpha(0f);
        }
        else
        {
            SetSnapshotAlpha(0f);
            SetSillyStatsAlpha(0f);
            SetSparklineAlpha(0f);
        }
        SetGradeStampHidden(celebrate);

        // Zone pills — sequential stagger.
        var pills = GetZonePills();
        foreach (var pill in pills)
        {
            if (pill == null) continue;
            pill.gameObject.SetActive(true);
            yield return WaitUnscaled(zonePillStaggerSeconds);
        }

        // Tile-tier counts — unscaled count-up.
        yield return CountUpTileCounts(data);

        if (!carousel)
        {
            // Peak snapshot fade-in.
            yield return FadeUnscaled(snapshotImage != null ? snapshotImage.gameObject : null, snapshotFadeSeconds, SetSnapshotAlpha);
        }

        // Employee of the Year — slide + fade.
        yield return SlideInEmployee();

        if (carousel)
        {
            // Results minipanel — fades in as one unit (currently showing slide 0, the
            // snapshot) instead of the three legacy flat-layout fade stages.
            yield return FadeUnscaled(minipanelRoot, minipanelFadeSeconds, SetMinipanelAlpha);
        }
        else
        {
            // Silly stats.
            yield return FadeUnscaled(favouriteActionText != null ? favouriteActionText.gameObject : null, sillyStatsFadeSeconds, SetSillyStatsAlpha);

            // Season sparkline — fades in right before the grade stamp.
            yield return FadeUnscaled(seasonSparkline != null ? seasonSparkline.gameObject : null, sparklineFadeSeconds, SetSparklineAlpha);
        }

        // Grade stamp lands last.
        if (celebrate)
            yield return PunchInGradeStamp();
        else
            yield return FadeInGradeStampSomber();

        _revealInProgress = false;
        _revealRoutine = null;

        // Reveal done — retire the invisible skip-catcher so it stops eating clicks meant
        // for the live buttons underneath (carousel nav, Play Again, Exit).
        HideSkipCatcher();
    }

    /// <summary>
    /// Idempotent — jumps every element the coroutine touches straight to its final,
    /// fully-revealed value. Called both by SkipReveal() and at the natural end of a
    /// non-staged Show() (playStagedReveal == false).
    /// </summary>
    private void FinishReveal(EndGameData data)
    {
        SetZonePillsVisible(true);
        SetTileCountsTo(data.thrivingCount, data.degradedCount, data.criticalCount);
        SetSnapshotAlpha(1f);
        SetEmployeeAlpha(1f);
        SetEmployeeOffset(0f);
        SetSillyStatsAlpha(1f);
        SetSparklineAlpha(1f);
        SetMinipanelAlpha(1f); // no-op in flat-layout mode (minipanelRoot null); required in carousel mode
        SetGradeStampFinal();
        _revealInProgress = false;

        // Covers the skip path (SkipReveal → FinishReveal) and the non-staged Show(): once
        // everything's on its final value there's nothing left to skip, so the catcher goes.
        HideSkipCatcher();
    }

    private void HideSkipCatcher()
    {
        if (skipCatcherButton != null) skipCatcherButton.gameObject.SetActive(false);
    }

    // ── Zone pills ─────────────────────────────────────────────────────

    private List<Transform> GetZonePills()
    {
        var list = new List<Transform>();
        if (zonePillContainer == null) return list;
        foreach (Transform child in zonePillContainer)
            list.Add(child);
        return list;
    }

    private void SetZonePillsVisible(bool visible)
    {
        foreach (var pill in GetZonePills())
            if (pill != null) pill.gameObject.SetActive(visible);
    }

    // ── Tile counts ────────────────────────────────────────────────────

    private void SetTileCountsTo(int thriving, int degraded, int critical)
    {
        if (thrivingCountText != null) thrivingCountText.text = thriving.ToString();
        if (degradedCountText != null) degradedCountText.text = degraded.ToString();
        if (criticalCountText != null) criticalCountText.text = critical.ToString();
    }

    private IEnumerator CountUpTileCounts(EndGameData data)
    {
        float elapsed = 0f;
        while (elapsed < tileCountCountUpSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / tileCountCountUpSeconds);
            SetTileCountsTo(
                Mathf.RoundToInt(Mathf.Lerp(0, data.thrivingCount, t)),
                Mathf.RoundToInt(Mathf.Lerp(0, data.degradedCount, t)),
                Mathf.RoundToInt(Mathf.Lerp(0, data.criticalCount, t)));
            yield return null;
        }
        SetTileCountsTo(data.thrivingCount, data.degradedCount, data.criticalCount);
    }

    // ── Snapshot ───────────────────────────────────────────────────────

    private void SetSnapshotAlpha(float a)
    {
        if (snapshotImage == null) return;
        var c = snapshotImage.color;
        c.a = a;
        snapshotImage.color = c;
    }

    // ── Employee of the Year ──────────────────────────────────────────

    private void SetEmployeeAlpha(float a)
    {
        SetTextAlpha(workerNameText, a);
        SetTextAlpha(workerTraitText, a);
        SetTextAlpha(workerActionsText, a);
        if (workerPortraitImage != null) SetImageAlpha(workerPortraitImage, a);
        if (workerInitialBg     != null) SetImageAlpha(workerInitialBg, a);
        SetTextAlpha(workerInitialText, a);
    }

    private void SetEmployeeOffset(float xOffset)
    {
        if (workerNameText == null) return;
        var rt = workerNameText.rectTransform.parent as RectTransform;
        if (rt == null) return;
        var pos = rt.anchoredPosition;
        pos.x = xOffset;
        rt.anchoredPosition = pos;
    }

    private IEnumerator SlideInEmployee()
    {
        if (workerNameText == null)
        {
            SetEmployeeAlpha(1f);
            yield break;
        }

        var rt = workerNameText.rectTransform.parent as RectTransform;
        float startX = rt != null ? -60f : 0f;
        SetEmployeeOffset(startX);

        float elapsed = 0f;
        while (elapsed < employeeSlideSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / employeeSlideSeconds);
            float eased = 1f - Mathf.Pow(1f - t, 3f); // easeOutCubic, unscaled-manual
            SetEmployeeAlpha(eased);
            SetEmployeeOffset(Mathf.Lerp(startX, 0f, eased));
            yield return null;
        }
        SetEmployeeAlpha(1f);
        SetEmployeeOffset(0f);
    }

    // ── Silly stats ────────────────────────────────────────────────────

    private void SetSillyStatsAlpha(float a)
    {
        SetTextAlpha(favouriteActionText, a);
        SetTextAlpha(mostAvoidedText, a);
        SetTextAlpha(mostChattedText, a);
    }

    // ── Results minipanel carousel ────────────────────────────────────

    /// <summary>
    /// True only when all six carousel refs are wired. This is the OPTIONAL-with-warning
    /// (Law 3 exception) idiom used elsewhere on this screen (grade stamp, sparkline):
    /// fully unwired is a silent, valid configuration (matches how the screen ships today,
    /// with the flat 3-section layout); PARTIALLY wired is a misconfiguration and warns once.
    /// </summary>
    private bool IsCarouselWired()
    {
        bool allWired = minipanelRoot   != null && snapshotSlide    != null &&
                         sparklineSlide != null && sillyStatsSlide  != null &&
                         slidePrevButton != null && slideNextButton != null;
        if (allWired) return true;

        bool anyWired = minipanelRoot   != null || snapshotSlide    != null ||
                         sparklineSlide != null || sillyStatsSlide  != null ||
                         slidePrevButton != null || slideNextButton != null;
        if (anyWired && !_carouselWarned)
        {
            _carouselWarned = true;
            Debug.LogWarning("[EndGameScreenUI] Results minipanel carousel is PARTIALLY wired — falling back to the flat layout. It needs all six refs: minipanelRoot, snapshotSlide, sparklineSlide, sillyStatsSlide, slidePrevButton, slideNextButton.", this);
        }
        return false;
    }

    private void OnPrevSlide() => ShowSlide(_slideIndex - 1);
    private void OnNextSlide() => ShowSlide(_slideIndex + 1);

    /// <summary>
    /// Swaps the visible slide via SetActive — not scrolling/marquee. Wraps both ways.
    /// Landing on slide 1 (sparkline) replays its progressive-reveal sweep every time,
    /// deliberately, so the history "draws itself" again on every visit.
    /// </summary>
    private void ShowSlide(int index)
    {
        if (!IsCarouselWired()) return;

        _slideIndex = ((index % SlideCount) + SlideCount) % SlideCount;
        snapshotSlide.SetActive(_slideIndex == 0);
        sparklineSlide.SetActive(_slideIndex == 1);
        sillyStatsSlide.SetActive(_slideIndex == 2);

        if (_slideIndex == 1 && seasonSparkline != null)
            seasonSparkline.PlayProgressiveReveal(sparklineSweepSeconds);
    }

    private void SetMinipanelAlpha(float a)
    {
        if (minipanelRoot == null) return;
        var cg = GetOrAddCanvasGroup(minipanelRoot);
        cg.alpha = a;
        bool interactive = a >= 0.99f;
        cg.interactable   = interactive;
        cg.blocksRaycasts = interactive;
    }

    // ── Season sparkline ───────────────────────────────────────────────

    private void SetSparklineAlpha(float a)
    {
        if (seasonSparkline == null) return;
        var cg = GetOrAddCanvasGroup(seasonSparkline.gameObject);
        cg.alpha = a;
    }

    // ── Grade stamp ────────────────────────────────────────────────────

    private void SetGradeStampHidden(bool willPunch)
    {
        if (gradeStampRoot == null) return;
        gradeStampRoot.SetActive(true);
        var cg = GetOrAddCanvasGroup(gradeStampRoot);
        cg.alpha = 0f;
        gradeStampRoot.transform.localScale = willPunch ? Vector3.one * 1.6f : Vector3.one;
    }

    private void SetGradeStampFinal()
    {
        if (gradeStampRoot == null) return;
        gradeStampRoot.SetActive(true);
        var cg = GetOrAddCanvasGroup(gradeStampRoot);
        cg.alpha = 1f;
        gradeStampRoot.transform.localScale = Vector3.one;
    }

    private IEnumerator PunchInGradeStamp()
    {
        if (gradeStampRoot == null) yield break;

        var cg = GetOrAddCanvasGroup(gradeStampRoot);
        LeanTween.value(gradeStampRoot, 0f, 1f, gradeStampPunchSeconds)
            .setOnUpdate(v => cg.alpha = v)
            .setEase(LeanTweenType.easeOutQuad)
            .setIgnoreTimeScale(true);

        bool done = false;
        LeanTween.scale(gradeStampRoot, Vector3.one, gradeStampPunchSeconds)
            .setEase(LeanTweenType.easeOutBack)
            .setIgnoreTimeScale(true)
            .setOnComplete(() => done = true);

        while (!done) yield return null;
        SetGradeStampFinal();
    }

    private IEnumerator FadeInGradeStampSomber()
    {
        if (gradeStampRoot == null) yield break;

        var cg = GetOrAddCanvasGroup(gradeStampRoot);
        bool done = false;
        LeanTween.value(gradeStampRoot, 0f, 1f, gradeStampPunchSeconds)
            .setOnUpdate(v => cg.alpha = v)
            .setEase(LeanTweenType.easeInOutQuad)
            .setIgnoreTimeScale(true)
            .setOnComplete(() => done = true);

        while (!done) yield return null;
        SetGradeStampFinal();
    }

    private static CanvasGroup GetOrAddCanvasGroup(GameObject go)
    {
        var cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    // ── Small shared helpers ───────────────────────────────────────────

    private static void SetTextAlpha(TextMeshProUGUI text, float a)
    {
        if (text == null) return;
        var c = text.color;
        c.a = a;
        text.color = c;
    }

    private static void SetImageAlpha(Image image, float a)
    {
        var c = image.color;
        c.a = a;
        image.color = c;
    }

    private IEnumerator FadeUnscaled(GameObject target, float seconds, Action<float> setAlpha)
    {
        if (target == null) { setAlpha(1f); yield break; }

        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            setAlpha(Mathf.Clamp01(elapsed / seconds));
            yield return null;
        }
        setAlpha(1f);
    }

    private IEnumerator WaitUnscaled(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }
}
