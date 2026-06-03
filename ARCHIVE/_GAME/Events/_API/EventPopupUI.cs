using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EventPopupUI : MonoBehaviour
{
    public static EventPopupUI Instance { get; private set; }

    [Header("Root")]
    [SerializeField] private GameObject overlayBlocker;   // full-screen dark overlay
    [SerializeField] private GameObject card;             // the centered popup card

    [Header("Speaker Row")]
    [SerializeField] private GameObject speakerRow;       // parent — hide for headline format
    [SerializeField] private Image speakerPortrait;
    [SerializeField] private TextMeshProUGUI speakerLabel;

    [Header("Content")]
    [SerializeField] private TextMeshProUGUI headlineText;
    [SerializeField] private TextMeshProUGUI bodyText;

    [Header("Buttons")]
    [SerializeField] private Button continueButton;       // "Continue" or "OK"
    [SerializeField] private TextMeshProUGUI continueLabel;
    [SerializeField] private Button abortButton;          // hidden for non-interruptible events

    private Action onContinue;
    private Action onAbort;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Hide();
    }

    // Called by EventManager
    // Convenience overload — passes raw asset strings through, no resolution
    public void Show(GameEventSO ev, Action onContinueCallback = null, Action onAbortCallback = null)
        => Show(ev, ev.headline, ev.bodyText, onContinueCallback, onAbortCallback);

    // Full overload — used by EventManager after token resolution
    public void Show(GameEventSO ev, string resolvedHeadline, string resolvedBody,
        Action onContinueCallback = null, Action onAbortCallback = null)
    {
        onContinue = onContinueCallback;
        onAbort    = onAbortCallback;

        // Content — uses the resolved strings, NOT the raw asset fields
        headlineText.text = resolvedHeadline;
        bodyText.text     = resolvedBody;

        // Speaker row
        bool hasSpeaker = !string.IsNullOrEmpty(ev.speakerName);
        speakerRow.SetActive(hasSpeaker);
        if (hasSpeaker)
        {
            speakerLabel.text = ev.speakerName;
            speakerPortrait.gameObject.SetActive(ev.speakerPortrait != null);
            if (ev.speakerPortrait != null)
                speakerPortrait.sprite = ev.speakerPortrait;
        }

        // Buttons
        bool canInterrupt = ev.canInterruptAction && onAbortCallback != null;
        abortButton.gameObject.SetActive(canInterrupt);
        continueLabel.text = canInterrupt ? "Continue" : "OK";

        continueButton.onClick.RemoveAllListeners();
        abortButton.onClick.RemoveAllListeners();
        continueButton.onClick.AddListener(OnContinueClicked);
        abortButton.onClick.AddListener(OnAbortClicked);

        overlayBlocker.SetActive(true);
        card.SetActive(true);
    }

    public void Hide()
    {
        overlayBlocker.SetActive(false);
        card.SetActive(false);
    }

    void OnContinueClicked()
    {
        Hide();
        onContinue?.Invoke();
    }

    void OnAbortClicked()
    {
        Hide();
        onAbort?.Invoke();
    }
}