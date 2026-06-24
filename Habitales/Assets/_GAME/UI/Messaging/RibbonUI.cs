using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Habitales.Dialogue;

namespace Habitales.UI
{
    /// <summary>
    /// Messenger-style preview ribbon for incoming messages.
    ///
    /// Behaviour:
    ///   • Queues incoming message previews.
    ///   • Releases ONE ribbon at a time, ONLY when the sim is at rest:
    ///       — Drains on RunManager.OnDayResolved.
    ///       — Skips while RunManager.IsEventPaused is true.
    ///   • Slide in → dwell → slide out (all durations serialized).
    ///   • Tap calls ChatAppUI.ToggleChatApp() then ChatAppUI.OpenThread(tabID).
    ///   • The FIRST ribbon fires on RunManager.OnRegionUnlocked (region-2 unlock beat,
    ///     per habitales_onboarding_alpha.md §first-ribbon).
    ///
    /// Inspector refs (EVERY null is a loud error per Law 3):
    ///   ribbonPanel     — root RectTransform of the ribbon (anchored off-screen when hidden).
    ///   senderNameText  — TMP label for the sender's display name.
    ///   previewBodyText — TMP label for the truncated message preview.
    ///   tapButton       — Button covering the ribbon; wired to OpenChat().
    ///   chatAppUI       — reference to the ChatAppUI that owns ToggleChatApp/OpenThread.
    ///
    /// Placement assumption: ribbonPanel sits on the same canvas as the messaging icon,
    /// anchored so its hidden position is fully off the right edge (or top, your call).
    /// Set hiddenAnchoredX (serialized) to match.
    /// </summary>
    public class RibbonUI : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────

        [Header("Panel")]
        [SerializeField] private RectTransform ribbonPanel;
        [SerializeField] private TextMeshProUGUI senderNameText;
        [SerializeField] private TextMeshProUGUI previewBodyText;
        [SerializeField] private Button           tapButton;

        [Header("Chat App")]
        [SerializeField] private ChatAppUI chatAppUI;

        [Header("Animation")]
        [SerializeField] private float hiddenAnchoredX  = 420f;   // px off-screen (right)
        [SerializeField] private float visibleAnchoredX = 0f;     // on-screen resting X
        [SerializeField] private float slideInDuration  = 0.30f;
        [SerializeField] private float dwellDuration    = 2.50f;
        [SerializeField] private float slideOutDuration = 0.25f;

        [Header("Preview")]
        [SerializeField] private int maxPreviewChars = 60;

        // ── Runtime ────────────────────────────────────────────────────

        private struct RibbonEntry
        {
            public string tabID;
            public string senderName;
            public string previewBody;
        }

        private readonly Queue<RibbonEntry> _queue    = new Queue<RibbonEntry>();
        private bool                         _showing  = false;
        private string                       _currentTabID;

        // ── Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            bool ok = true;

            if (ribbonPanel == null)
            {
                Debug.LogError($"{name}: RibbonUI — ribbonPanel is not wired. Drag the ribbon RectTransform.", this);
                ok = false;
            }
            if (senderNameText == null)
            {
                Debug.LogError($"{name}: RibbonUI — senderNameText is not wired. Drag the sender TMP label.", this);
                ok = false;
            }
            if (previewBodyText == null)
            {
                Debug.LogError($"{name}: RibbonUI — previewBodyText is not wired. Drag the body TMP label.", this);
                ok = false;
            }
            if (tapButton == null)
            {
                Debug.LogError($"{name}: RibbonUI — tapButton is not wired. Drag the tap Button.", this);
                ok = false;
            }
            if (chatAppUI == null)
            {
                Debug.LogError($"{name}: RibbonUI — chatAppUI is not wired. Drag the ChatAppUI component.", this);
                ok = false;
            }

            if (!ok) { enabled = false; return; }

            // Park the panel off-screen at startup.
            ParkOffScreen();
        }

        private void OnEnable()
        {
            tapButton.onClick.AddListener(OpenChat);

            if (DialogueManager.Instance != null)
            {
                DialogueManager.Instance.OnMessagesUpdated -= HandleMessagesUpdated;
                DialogueManager.Instance.OnMessagesUpdated += HandleMessagesUpdated;
            }

            if (RunManager.Instance != null)
            {
                RunManager.Instance.OnDayResolved     -= HandleDayResolved;
                RunManager.Instance.OnDayResolved     += HandleDayResolved;
                RunManager.Instance.OnRegionUnlocked  -= HandleRegionUnlocked;
                RunManager.Instance.OnRegionUnlocked  += HandleRegionUnlocked;
            }
        }

        private void OnDisable()
        {
            tapButton.onClick.RemoveListener(OpenChat);

            if (DialogueManager.Instance != null)
                DialogueManager.Instance.OnMessagesUpdated -= HandleMessagesUpdated;

            if (RunManager.Instance != null)
            {
                RunManager.Instance.OnDayResolved    -= HandleDayResolved;
                RunManager.Instance.OnRegionUnlocked -= HandleRegionUnlocked;
            }
        }

        // ── Event Handlers ─────────────────────────────────────────────

        /// <summary>New message arrived — enqueue a preview.</summary>
        private void HandleMessagesUpdated(string tabID)
        {
            string preview = BuildPreview(tabID, out string sender);
            if (string.IsNullOrEmpty(preview)) return;

            _queue.Enqueue(new RibbonEntry
            {
                tabID       = tabID,
                senderName  = sender,
                previewBody = preview
            });

            // If the sim is currently at rest (no event running), try to show immediately.
            TryDequeue();
        }

        /// <summary>
        /// First-ribbon trigger: region unlock is the "welcome to the messaging app" beat
        /// (habitales_onboarding_alpha §first-ribbon). Fires once; subsequent unlocks
        /// are already covered by real messages.
        /// </summary>
        private bool _regionUnlockRibbonSent = false;
        private void HandleRegionUnlocked()
        {
            if (_regionUnlockRibbonSent) return;
            _regionUnlockRibbonSent = true;

            _queue.Enqueue(new RibbonEntry
            {
                tabID       = "",   // no specific thread to open
                senderName  = "Messages",
                previewBody = "Your crew is checking in — new messages!"
            });

            TryDequeue();
        }

        /// <summary>Day fully settled — release the next queued ribbon if safe to do so.</summary>
        private void HandleDayResolved(int _) => TryDequeue();

        // ── Queue Drain ────────────────────────────────────────────────

        private void TryDequeue()
        {
            if (_showing)            return;
            if (_queue.Count == 0)   return;

            RunManager run = RunManager.Instance;
            if (run != null && run.IsEventPaused) return;

            RibbonEntry entry = _queue.Dequeue();
            ShowRibbon(entry);
        }

        // ── Animation ──────────────────────────────────────────────────

        private void ShowRibbon(RibbonEntry entry)
        {
            _showing      = true;
            _currentTabID = entry.tabID;

            senderNameText.text  = entry.senderName;
            previewBodyText.text = entry.previewBody;

            LeanTween.cancel(ribbonPanel.gameObject);

            // Slide in
            LeanTween.value(ribbonPanel.gameObject, hiddenAnchoredX, visibleAnchoredX, slideInDuration)
                .setEase(LeanTweenType.easeOutCubic)
                .setOnUpdate(SetRibbonX)
                .setOnComplete(() =>
                {
                    // Dwell
                    LeanTween.delayedCall(ribbonPanel.gameObject, dwellDuration, () =>
                    {
                        // Slide out
                        LeanTween.value(ribbonPanel.gameObject, visibleAnchoredX, hiddenAnchoredX, slideOutDuration)
                            .setEase(LeanTweenType.easeInCubic)
                            .setOnUpdate(SetRibbonX)
                            .setOnComplete(() =>
                            {
                                ParkOffScreen();
                                _showing = false;
                                // Try the next one, if any
                                TryDequeue();
                            });
                    });
                });
        }

        private void SetRibbonX(float x)
        {
            Vector2 ap = ribbonPanel.anchoredPosition;
            ap.x = x;
            ribbonPanel.anchoredPosition = ap;
        }

        private void ParkOffScreen()
        {
            if (ribbonPanel == null) return;
            Vector2 ap = ribbonPanel.anchoredPosition;
            ap.x = hiddenAnchoredX;
            ribbonPanel.anchoredPosition = ap;
        }

        // ── Tap ────────────────────────────────────────────────────────

        private void OpenChat()
        {
            // Immediately dismiss the ribbon.
            LeanTween.cancel(ribbonPanel.gameObject);
            ParkOffScreen();
            _showing = false;

            if (chatAppUI == null) return;
            chatAppUI.ToggleChatApp();

            if (!string.IsNullOrEmpty(_currentTabID))
                chatAppUI.OpenThread(_currentTabID);
        }

        // ── Preview Builder ────────────────────────────────────────────

        /// <summary>
        /// Reads the last resolved line from DialogueManager for the given tab and
        /// truncates to maxPreviewChars. Returns empty string if nothing to show.
        /// </summary>
        private string BuildPreview(string tabID, out string sender)
        {
            sender = tabID; // fallback

            if (DialogueManager.Instance == null) return "";

            var lines = DialogueManager.Instance.GetChatLines(tabID);
            if (lines == null || lines.Count == 0) return "";

            var last = lines[lines.Count - 1];

            // Skip player bubbles and stickers — no ribbon for outgoing or emoji-only.
            if (last.isPlayerBubble || last.isStickerBubble) return "";

            sender = string.IsNullOrEmpty(last.displayName) ? tabID : last.displayName;

            string body = last.body ?? "";
            if (body.Length > maxPreviewChars)
                body = body.Substring(0, maxPreviewChars).TrimEnd() + "…";

            return body;
        }
    }
}
