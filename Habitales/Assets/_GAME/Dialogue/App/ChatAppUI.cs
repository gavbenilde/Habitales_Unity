using System.Collections.Generic;
using System.Linq;
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
    public class ChatAppUI : MonoBehaviour, IUISubsystem
    {
        // ── IUISubsystem ──────────────────────────────────────────────
        //
        // Root toggle choice: chatPanel (the same GameObject ToggleChatApp/_isOpen
        // already drive), NOT a bypass of _conversationLocked. SetVisible is the
        // hub's passive hide-ALL-UI path (screenshot hide-all) — it directly
        // SetActive()s chatPanel without touching _isOpen/_conversationLocked
        // state or firing the close affordances. This is safe specifically
        // because hide-all is a transient snapshot/restore pair (UIManager.
        // SetAllUIVisible -> RestoreUIVisibility): IsVisible reports chatPanel's
        // actual activeSelf, so if the app was closed (or locked-open) when hidden,
        // restore re-applies that same state — it can never leave a locked
        // conversation open when it wasn't, nor silently closed when it was open.
        // A real close still must go through ToggleChatApp(), which still refuses
        // while _conversationLocked is true.

        public string SubsystemId => "chatApp";
        public bool   IsVisible   => chatPanel != null && chatPanel.activeSelf;
        public void   SetVisible(bool visible)
        {
            if (chatPanel != null) chatPanel.SetActive(visible);
        }

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

        [Header("Choice Row")]
        [SerializeField] private GameObject     choiceRow;          // container, toggled active
        [SerializeField] private Transform      choiceRowContent;   // parent for the option buttons
        [SerializeField] private ChoiceButtonUI choiceButtonPrefab;

        // ── Runtime State ──────────────────────────────────────────────

        private bool _isOpen;
        private string _currentTabID;
        private bool _stickerTrayOpen;

        // True while the player is mid-way through a conversation they've started
        // answering (responded, but the thread still has a pending choice). While locked
        // they can't exit the messaging app — close / back / tab-switch are gated here, and
        // the HUD blocker behind the app keeps clicks off everything else (so no action can
        // advance the day mid-conversation). Cleared when the thread ends.
        private bool _conversationLocked;

        private readonly List<TabRowUI>       _tabRows        = new();
        private readonly List<ChatBubbleUI>   _bubbles        = new();
        private readonly List<StickerButtonUI> _stickerButtons = new();
        private readonly List<ChoiceButtonUI>  _choiceButtons  = new();

        // ── Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            chatPanel.SetActive(false);
            stickerTray.SetActive(false);

            // Loud-fail unwired choice-row refs (Law 3) — a silent no-op would make
            // authored player choices simply never appear.
            if (choiceRow == null || choiceRowContent == null || choiceButtonPrefab == null)
                Debug.LogError($"{name}: Choice Row is not fully wired (choiceRow / choiceRowContent / choiceButtonPrefab). " +
                               "Player choice nodes will not render. Assign all three in the Inspector.", this);
            else
                choiceRow.SetActive(false);
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
            if (_conversationLocked) return; // can't close while mid-conversation

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
            if (_conversationLocked) return; // can't leave the thread while mid-conversation

            _currentTabID = null;
            threadView.SetActive(false);
            tabListView.SetActive(true);
            CloseStickerTray();
            ClearChoiceRow();
            if (choiceRow != null) choiceRow.SetActive(false);
            RefreshTabList();
        }

        private void RefreshTabList()
        {
            foreach (var row in _tabRows)
                Destroy(row.gameObject);
            _tabRows.Clear();

            var previews = DialogueManager.Instance.GetTabPreviews()
                .Where(p => !string.IsNullOrEmpty(p.lastMessageBody))
                .ToList();

            foreach (var preview in previews)
            {
                var row = Instantiate(tabRowPrefab, tabListContent);
                row.Setup(preview, OpenThread);
                _tabRows.Add(row);
            }
        }

        // ── Thread View ────────────────────────────────────────────────

        public void OpenThread(string tabID)
        {
            if (_conversationLocked) return; // can't switch tabs while mid-conversation

            _currentTabID = tabID;

            DialogueManager.Instance?.IncrementChatOpen(tabID);
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

            RefreshChoiceRow();
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

        // ── Choice Row ─────────────────────────────────────────────────

        // Shows the option buttons when the current tab is paused on a player choice,
        // otherwise tears the row down. Rebuilt from scratch each call because the
        // pending options change as the player advances through the thread.
        private void RefreshChoiceRow()
        {
            if (choiceRow == null || choiceRowContent == null || choiceButtonPrefab == null)
                return; // already loud-failed in Awake

            ClearChoiceRow();

            if (string.IsNullOrEmpty(_currentTabID))
            {
                choiceRow.SetActive(false);
                SetStickerToggleAvailable(true);
                return;
            }

            var pending = DialogueManager.Instance.GetPendingChoice(_currentTabID);
            if (pending == null || pending.options.Count == 0)
            {
                choiceRow.SetActive(false);
                SetStickerToggleAvailable(true);
                return;
            }

            for (int i = 0; i < pending.options.Count; i++)
            {
                var btn = Instantiate(choiceButtonPrefab, choiceRowContent);
                btn.Setup(pending.options[i], i, OnChoiceSelected);
                _choiceButtons.Add(btn);
            }

            choiceRow.SetActive(true);

            // A pending choice IS the input — block stickers (even before the player
            // responds) so a sticker can't be appended behind the tab's halt and vanish.
            SetStickerToggleAvailable(false);
            CloseStickerTray();
        }

        // Enables/disables the sticker tray toggle. Owned here (not by the lock) because a
        // pending choice should suppress stickers regardless of whether the player has
        // responded yet. Re-enabled the moment no choice is pending.
        private void SetStickerToggleAvailable(bool available)
        {
            if (stickerToggleButton != null) stickerToggleButton.interactable = available;
        }

        private void ClearChoiceRow()
        {
            foreach (var btn in _choiceButtons)
                Destroy(btn.gameObject);
            _choiceButtons.Clear();
        }

        // Player tap → runtime selection. The walker records the pick and re-pushes
        // the tab; HandleMessagesUpdated then appends the chosen reply + follow-up and
        // refreshes (or hides) this row.
        private void OnChoiceSelected(int optionIndex)
        {
            if (string.IsNullOrEmpty(_currentTabID)) return;

            DialogueManager.Instance.SelectChoice(_currentTabID, optionIndex);

            // Responding commits the player to finishing the thread. SelectChoice has
            // already re-rendered synchronously (OnMessagesUpdated), so if a pending choice
            // remains we lock until it's answered; if the thread ended, we release.
            SetConversationLocked(DialogueManager.Instance.GetPendingChoice(_currentTabID) != null);
        }

        // Gates the in-app exit affordances while a started conversation is unfinished.
        // Sticker suppression is handled by RefreshChoiceRow (a pending choice already
        // disables it), which runs synchronously just before this on every response.
        private void SetConversationLocked(bool locked)
        {
            _conversationLocked = locked;
            backButton.interactable = !locked;
            // ToggleChatApp() refuses to close while locked; the HUD blocker behind the app
            // catches every click outside it, so no action can advance the day mid-conversation.
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

                // The pending choice may have changed (advanced to the next one, or
                // cleared) even when the line count did not — always refresh the row.
                RefreshChoiceRow();

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