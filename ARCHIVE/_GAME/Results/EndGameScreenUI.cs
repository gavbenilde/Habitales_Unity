using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Habitales.Dialogue;

/// <summary>
/// Full-screen end-game panel. Add to your existing Canvas at a high Sort Order.
/// Mirrors EventPopupUI in structure: singleton, Show(data) / Hide().
/// Has a minimize button so the player can peek at the map without closing.
/// </summary>
public class EndGameScreenUI : MonoBehaviour
{
    public static EndGameScreenUI Instance { get; private set; }

    [Header("Panel Roots")]
    [SerializeField] private GameObject overlayPanel;   // root — toggled by Show/Hide
    [SerializeField] private GameObject fullContent;    // everything below the top bar

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI endReasonText;
    [SerializeField] private TextMeshProUGUI worldHealthText;
    [SerializeField] private TextMeshProUGUI yearDayText;

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

    [Header("Footer")]
    [SerializeField] private TextMeshProUGUI researchPointsText;
    [SerializeField] private Button          playAgainButton;        // reloads the run scene for a fresh attempt
    [SerializeField] private Button          exitToMainMenuButton;   // exits to the main menu (or prototype menu — see toggle)
    [SerializeField] private Button          minimizeButton;

    [Header("Menu Routing")]
    [Tooltip("When true, the Exit button loads 'PrototypeMenu' instead of 'Main Menu'. Use during development.")]
    [SerializeField] private bool usePrototypeMenu = false;
    [SerializeField] private string mainMenuSceneName      = "Main Menu";
    [SerializeField] private string prototypeMenuSceneName = "PrototypeMenu";

    [Header("Level-Up Handoff (DORMANT — moved to MainMenu)")]
    [SerializeField] private LevelUpScreenUI     levelUpScreen;
    [SerializeField] private PlayerProgressionSO playerProgression;

    private bool isMinimized;
    private EndGameData _lastData;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        overlayPanel.SetActive(false);
    }

    private void OnEnable()
    {
        if (minimizeButton       != null) minimizeButton.onClick.AddListener(ToggleMinimize);
        if (playAgainButton      != null) playAgainButton.onClick.AddListener(OnPlayAgain);
        if (exitToMainMenuButton != null) exitToMainMenuButton.onClick.AddListener(OnExitToMainMenu);
    }

    private void OnDisable()
    {
        if (minimizeButton       != null) minimizeButton.onClick.RemoveListener(ToggleMinimize);
        if (playAgainButton      != null) playAgainButton.onClick.RemoveListener(OnPlayAgain);
        if (exitToMainMenuButton != null) exitToMainMenuButton.onClick.RemoveListener(OnExitToMainMenu);
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

        Debug.Log($"[EndGameScreenUI.Show] activeInHierarchy={gameObject.activeInHierarchy} | overlayPanel={(overlayPanel != null)} | fullContent={(fullContent != null)} | endReasonText={(endReasonText != null)} | playAgainButton={(playAgainButton != null)}");

        if (overlayPanel == null || fullContent == null)
        {
            Debug.LogError("[EndGameScreenUI] overlayPanel or fullContent is NULL — panel can't activate. Wire them on the EndGameScreenUI GameObject. Falling back to direct Main Menu load.");
            SceneManager.LoadScene("Main Menu");
            return;
        }

        _lastData = data;
        Populate(data);
        isMinimized = false;
        fullContent.SetActive(true);
        overlayPanel.SetActive(true);
    }

    public void Hide()
    {
        overlayPanel.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Minimize toggle — lets player peek at the map without closing the screen
    // -------------------------------------------------------------------------

    private void ToggleMinimize()
    {
        bool showing = !fullContent.activeSelf; 
        fullContent.SetActive(showing);
        minimizeButton.GetComponentInChildren<TextMeshProUGUI>().text = showing ? "−" : "+";
    }

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
        // MainMenu.Start fires the level-up overlay whenever current progression
        // is ahead of PlayerProgressionSO.lastSeen*; Play Again skips this path,
        // so accumulated XP/unlocks from consecutive reloads land all at once.
        Hide();
        string target = usePrototypeMenu ? prototypeMenuSceneName : mainMenuSceneName;
        SceneManager.LoadScene(target);
    }

    // -------------------------------------------------------------------------
    // Populate — pure data binding, no game logic
    // -------------------------------------------------------------------------

    private void Populate(EndGameData data)
    {
        // Header
        endReasonText.text   = data.endReason;
        worldHealthText.text = $"{data.worldHealth:F1}% World Health";
        yearDayText.text     = $"Year {data.currentYear}, Day {data.totalDays}";

        // Zone pills — clear old, spawn new
        foreach (Transform child in zonePillContainer)
            Destroy(child.gameObject);

        foreach (var kvp in data.zoneHealths)
        {
            var pill = Instantiate(zonePillPrefab, zonePillContainer);
            pill.Setup(kvp.Key, kvp.Value);
        }

        // Snapshot — null texture leaves RawImage in its default (empty) state
        if (snapshotImage != null)
            snapshotImage.texture = data.snapshot?.peakScreenshot;

        // Tile counts
        thrivingCountText.text = data.thrivingCount.ToString();
        degradedCountText.text = data.degradedCount.ToString();
        criticalCountText.text = data.criticalCount.ToString();
        if (peakThrivingText != null)
            peakThrivingText.text = (data.snapshot?.peakThrivingCount ?? 0).ToString();

        // Employee of the Year
        PopulateWorker(data.topWorker);

        // Silly stats
        favouriteActionText.text = Coalesce(data.favouriteAction);
        mostAvoidedText.text     = Coalesce(data.mostAvoidedAction);
        mostChattedText.text     = Coalesce(data.mostChattedWorker);

        // Footer
        researchPointsText.text = $"{data.researchPoints} RP";
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
}
