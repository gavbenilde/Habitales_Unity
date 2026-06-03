using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AziSpeechBubbleUI : MonoBehaviour
{
    public static AziSpeechBubbleUI Instance { get; private set; }

    [Header("Root")]
    [SerializeField] private GameObject overlayBlocker;
    [SerializeField] private GameObject card;

    [Header("Content")]
    [SerializeField] private Image            aziPortrait;   // optional — assign in inspector, display-only
    [SerializeField] private TextMeshProUGUI  speechText;

    [Header("Buttons")]
    // Wire to the overlayBlocker's Button component (or any full-screen Button) — clicking it dismisses the bubble.
    [SerializeField] private Button dismissButton;

    private Action onContinue;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        overlayBlocker.SetActive(false);
        card.SetActive(false);
    }

    public void Show(string line, Action onContinueCallback)
    {
        // Self-heal: if our own GameObject got toggled off in the scene, turn it back on.
        // We can't recover from a disabled PARENT — only manual scene fix can do that.
        if (!gameObject.activeSelf)
        {
            Debug.LogWarning("[AziSpeechBubble] Root GameObject was inactive — auto-enabling.");
            gameObject.SetActive(true);
        }

        Debug.Log($"[AziSpeechBubble] Show called. activeInHierarchy={gameObject.activeInHierarchy} | overlayBlocker={(overlayBlocker != null)} | card={(card != null)} | speechText={(speechText != null)} | dismissButton={(dismissButton != null)}");

        bool refsBroken = overlayBlocker == null || card == null || speechText == null || dismissButton == null;
        bool stillInactive = !gameObject.activeInHierarchy;

        if (refsBroken || stillInactive)
        {
            if (refsBroken)
                Debug.LogError("[AziSpeechBubble] Serialized refs missing — bubble can't show. Skipping to onContinue so EndGameScreen still appears.");
            if (stillInactive)
                Debug.LogError("[AziSpeechBubble] A PARENT in the hierarchy is inactive — bubble can't show. Skipping to onContinue so EndGameScreen still appears. Enable the parent in the scene to restore the bubble.");

            // Don't soft-lock the run-end flow — fire the callback so EndGameScreen still appears.
            onContinueCallback?.Invoke();
            return;
        }

        onContinue = onContinueCallback;
        speechText.text = line;
        dismissButton.onClick.RemoveAllListeners();
        dismissButton.onClick.AddListener(OnDismissClicked);
        overlayBlocker.SetActive(true);
        card.SetActive(true);
    }

    public void Hide()
    {
        overlayBlocker.SetActive(false);
        card.SetActive(false);
    }

    private void OnDismissClicked()
    {
        Hide();
        onContinue?.Invoke();
    }
}
