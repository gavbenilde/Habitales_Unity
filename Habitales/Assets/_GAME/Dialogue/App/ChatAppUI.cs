using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Habitales.Dialogue;

namespace Habitales.UI
{
    /// <summary>
    /// Main controller for the Chat App UI.
    /// Owns the panel open/close toggle and the two-view state machine:
    ///   Tab List <-> Thread View
    ///
    /// Assign this to the root ChatApp GameObject inside the Phone panel.
    /// The phone HUD button calls ToggleChatApp().
    /// </summary>
    public class ChatAppUI : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────

        [Header("Panel")]
        [SerializeField] private GameObject chatPanel;

        [Header("Tab List View")]
        [SerializeField] private GameObject tabListView;
        [SerializeField] private Transform tabListContent;
        [SerializeField] private TabRowUI tabRowPrefab;

        [Header("Thread View")]
        [SerializeField] private GameObject threadView;
        [SerializeField] private Button backButton;
        [SerializeField] private Image headerPortrait;
        [SerializeField] private TextMeshProUGUI headerNameText;
        [SerializeField] private ScrollRect bubbleScrollRect;
        [SerializeField] private Transform bubbleContent;
        [SerializeField] private ChatBubbleUI npcBubblePrefab;
        [SerializeField] private ChatBubbleUI playerBubblePrefab;

        [Header("Sticker Tray")]
        [SerializeField] private Button stickerToggleButton;
        [SerializeField] private GameObject stickerTray;
        [SerializeField] private Transform stickerTrayContent;
        [SerializeField] private StickerButtonUI stickerButtonPrefab;

        // ── Runtime State ──────────────────────────────────────────────

        private bool _isOpen;
        private string _currentTabID;
        private bool _stickerTrayOpen;

        private readonly List<TabRowUI>       _tabRows        = new();
        private readonly List<ChatBubbleUI>   _bubbles        = new();
        private readonly List<StickerButtonUI> _stickerButtons = new();

        // ── Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            chatPanel.SetActive(false);
            stickerTray.SetActive(false);
        }

        private void OnEnable()
        {
            DialogueManager.Instance.OnMessagesUpdated += HandleMessagesUpdated;
            DialogueManager.Instance.OnUnreadChanged   += HandleUnreadChanged;
            backButton.onClick.AddListener(ShowTabList);
            stickerToggleButton.onClick.AddListener(ToggleStickerTray);
        }

        private void OnDisable()
        {
            if (DialogueManager.Instance == null) return;
            DialogueManager.Instance.OnMessagesUpdated -= HandleMessagesUpdated;
            DialogueManager.Instance.OnUnreadChanged   -= HandleUnreadChanged;
            backButton.onClick.RemoveListener(ShowTabList);
            stickerToggleButton.onClick.RemoveListener(ToggleStickerTray);
        }

        // ── Panel Toggle ───────────────────────────────────────────────

        /// <summary>Called by the phone HUD messaging app button.</summary>
        public void ToggleChatApp()
        {
            _isOpen = !_isOpen;
            chatPanel.SetActive(_isOpen);

            if (_isOpen)
                ShowTabList();
            else
                CloseStickerTray();
        }

        // ── Tab List ───────────────────────────────────────────────────

        public void ShowTabList()
        {
            _currentTabID = null;
            threadView.SetActive(false);
            tabListView.SetActive(true);
            CloseStickerTray();
            RefreshTabList();
        }

        private void RefreshTabList()
        {
            foreach (var row in _tabRows)
                Destroy(row.gameObject);
            _tabRows.Clear();

            foreach (var preview in DialogueManager.Instance.GetTabPreviews())
            {
                var row = Instantiate(tabRowPrefab, tabListContent);
                row.Setup(preview, OpenThread);
                _tabRows.Add(row);
            }
        }

        // ── Thread View ────────────────────────────────────────────────

        public void OpenThread(string tabID)
        {
            _currentTabID = tabID;
            tabListView.SetActive(false);
            threadView.SetActive(true);

            // Populate header
            var previews = DialogueManager.Instance.GetTabPreviews();
            var preview  = previews.Find(p => p.tabID == tabID);
            if (preview != null)
            {
                headerPortrait.sprite  = preview.portrait;
                headerPortrait.enabled = preview.portrait != null;
                headerNameText.text    = preview.displayName;
            }

            RebuildBubbles();
            DialogueManager.Instance.MarkTabRead(tabID);
        }

        private void RebuildBubbles()
        {
            foreach (var bubble in _bubbles)
                Destroy(bubble.gameObject);
            _bubbles.Clear();

            if (string.IsNullOrEmpty(_currentTabID)) return;

            var lines = DialogueManager.Instance.GetChatLines(_currentTabID);
            foreach (var line in lines)
                SpawnBubble(line);

            ScrollToBottom();
        }

        private void SpawnBubble(ResolvedLine line)
        {
            var prefab = line.isPlayerBubble ? playerBubblePrefab : npcBubblePrefab;
            var bubble = Instantiate(prefab, bubbleContent);
            bubble.Setup(line);
            _bubbles.Add(bubble);
        }

        private void ScrollToBottom()
        {
            Canvas.ForceUpdateCanvases();
            bubbleScrollRect.verticalNormalizedPosition = 0f;
        }

        // ── Sticker Tray ───────────────────────────────────────────────

        private void ToggleStickerTray()
        {
            _stickerTrayOpen = !_stickerTrayOpen;
            stickerTray.SetActive(_stickerTrayOpen);

            if (_stickerTrayOpen)
                BuildStickerTray();
        }

        private void CloseStickerTray()
        {
            _stickerTrayOpen = false;
            stickerTray.SetActive(false);
        }

        private void BuildStickerTray()
        {
            if (_stickerButtons.Count > 0) return;

            var stickers = DialogueManager.Instance.stickerLibrary.playerStickers;
            foreach (var sticker in stickers)
            {
                bool isLocked = sticker.isBirthdaySticker &&
                                DialogueManager.Instance.IsBirthdayStickerLocked(_currentTabID);

                var btn = Instantiate(stickerButtonPrefab, stickerTrayContent);
                btn.Setup(sticker, OnStickerSelected, isLocked);
                _stickerButtons.Add(btn);
            }
        }

        private void OnStickerSelected(StickerSO sticker)
        {
            if (string.IsNullOrEmpty(_currentTabID)) return;
            DialogueManager.Instance.HandleStickerSent(_currentTabID, sticker);
            CloseStickerTray();
        }

        // ── Event Handlers ─────────────────────────────────────────────

        private void HandleMessagesUpdated(string tabID)
        {
            if (!_isOpen) return;

            if (tabID == _currentTabID)
            {
                // Append only the lines we haven't rendered yet
                var lines    = DialogueManager.Instance.GetChatLines(tabID);
                int newStart = _bubbles.Count;

                for (int i = newStart; i < lines.Count; i++)
                    SpawnBubble(lines[i]);

                if (lines.Count > newStart)
                    ScrollToBottom();

                DialogueManager.Instance.MarkTabRead(tabID);
            }
            else if (_currentTabID == null)
            {
                // Tab list is showing — refresh the previews
                RefreshTabList();
            }
        }

        private void HandleUnreadChanged(string tabID)
        {
            // Only matters when tab list is visible
            if (!_isOpen || _currentTabID != null) return;

            var row = _tabRows.Find(r => r.TabID == tabID);
            row?.RefreshUnreadDot();
        }
    }
}