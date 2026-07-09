using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    [Header("Progression Display")]
    [SerializeField] private PlayerProgressionSO playerProgression;
    [SerializeField] private TextMeshProUGUI     levelText;
    [SerializeField] private TextMeshProUGUI     xpText;

    [Header("Level-Up Overlay")]
    [SerializeField] private LevelUpScreenUI levelUpScreen;

    [Header("Scene Names")]
    [Tooltip("Scene to load when Start Run is pressed.")]
    [SerializeField] private string runSceneName = "Vertical Slice";

    [Header("Season Select")]
    [Tooltip("The season picker shown when Play is pressed (writes RunConfig.SelectedSeasons, " +
             "then this loads the run scene). Unwired → Play loads the scene directly at the " +
             "scene-default run length, with a warning.")]
    [SerializeField] private Habitales.UI.SeasonSelectPanelUI seasonSelectPanel;

    void Start()
    {
        ProgressionPersistence.Load(playerProgression);

        // Show the lastSeen snapshot first — the level-up overlay (if it plays)
        // is what animates the player up to current state.
        RefreshDisplay();

        if (playerProgression == null) return;

        bool hasUnseenXp      = playerProgression.totalXp > playerProgression.lastSeenTotalXp;
        bool hasUnseenUnlocks = playerProgression.unlockedPlantIds.Count > playerProgression.lastSeenUnlockCount;
        if (!hasUnseenXp && !hasUnseenUnlocks) return;
        if (levelUpScreen == null) return;

        int xpBefore = playerProgression.lastSeenTotalXp;
        int xpEarned = playerProgression.totalXp - playerProgression.lastSeenTotalXp;

        levelUpScreen.Show(xpBefore, xpEarned, playerProgression, () =>
        {
            playerProgression.lastSeenLevel       = playerProgression.level;
            playerProgression.lastSeenTotalXp     = playerProgression.totalXp;
            playerProgression.lastSeenUnlockCount = playerProgression.unlockedPlantIds.Count;
            ProgressionPersistence.Save(playerProgression);
            RefreshDisplay();
        });
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
