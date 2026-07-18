using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    [Header("Progression Display")]
    [SerializeField] private PlayerProgressionSO playerProgression;
    [SerializeField] private TextMeshProUGUI     levelText;
    [SerializeField] private TextMeshProUGUI     xpText;

    [Header("Scene Names")]
    [Tooltip("Scene to load when Start Run is pressed.")]
    [SerializeField] private string runSceneName = "Vertical Slice";

    [Header("Season Select")]
    [Tooltip("The season picker shown when Play is pressed (writes RunConfig.SelectedSeasons, " +
             "then this loads the run scene). Unwired → Play loads the scene directly at the " +
             "scene-default run length, with a warning.")]
    [SerializeField] private Habitales.UI.SeasonSelectPanelUI seasonSelectPanel;

    // DORMANT (2026-07-18): level-ups cut — this used to show a level-up overlay when
    // current progression was ahead of PlayerProgressionSO.lastSeen*. That overlay path is
    // removed; nothing awards XP anymore (see RunEndCoordinator.ProcessRunEnd), so
    // lastSeen* would never fall behind current values in the first place. Level/XP text
    // still displays a static snapshot via RefreshDisplay().
    void Start()
    {
        ProgressionPersistence.Load(playerProgression);
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        if (playerProgression == null) return;
        if (levelText != null) levelText.text = $"Level {playerProgression.lastSeenLevel}";
        if (xpText    != null) xpText.text    = $"{playerProgression.lastSeenTotalXp} XP";
    }

    // ─── Button hooks (zero-arg — wire directly in the Button's OnClick inspector) ───

    /// <summary>Start Run button. Opens the season picker first (2026-07-08); the run scene
    /// loads when the player confirms a season count. Falls back to a direct load (previous
    /// behavior) when the picker isn't wired, so an unwired menu still starts runs.</summary>
    public void StartRun()
    {
        if (seasonSelectPanel != null)
        {
            seasonSelectPanel.Open(LoadRunScene);
            return;
        }

        Debug.LogWarning("[MainMenu] seasonSelectPanel not wired — starting the run at the scene-default length. Wire the picker for player-chosen season counts.");
        LoadRunScene();
    }

    private void LoadRunScene()
    {
        SceneManager.LoadScene(runSceneName);
    }

    /// <summary>String-arg variant for Settings / Credits / etc. — type the scene name in the inspector field on the Button's OnClick.</summary>
    public void LoadScene(string name)
    {
        SceneManager.LoadScene(name);
    }

    /// <summary>Wipes the JSON save and refreshes the level/XP text. Wire to a "Reset Progress" debug button.</summary>
    public void ResetProgress()
    {
        if (playerProgression == null) return;
        ProgressionPersistence.Reset(playerProgression);
        RefreshDisplay();
    }

    /// <summary>Quit button. Stops play-mode in editor; quits in build.</summary>
    public void QuitGame()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
}
