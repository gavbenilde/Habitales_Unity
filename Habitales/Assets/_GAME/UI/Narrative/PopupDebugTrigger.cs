using System;
using UnityEngine;
using Habitales.Dialogue;

namespace Habitales.UI
{
    /// <summary>
    /// DEV-ONLY: fires each of the four narrative-popup flavours on demand so all four
    /// layouts — the 2×2 of intrusiveness × showPortrait — can be eyeballed in one sitting
    /// without engineering in-game situations that trigger them.
    ///
    /// Content is authored as <see cref="PopupSO"/> — the SAME asset type the real
    /// catalog/TriggerManager pipeline fires (Assets ▶ Create ▶ Habitales ▶ Popup),
    /// not a ConversationSO (that's the chat-append asset, a different surface).
    ///
    ///   • intrusiveness comes from each PopupSO's own field — set it per slot.
    ///   • showPortrait is NOT a caller-side toggle for PopupSO: the portrait box is
    ///     driven per-LINE by whether ResolveLines produces a non-null portrait
    ///     (Azi/Bob speaker → profile portrait; Custom + portraitOverride → that
    ///     sprite; Custom with no override → null → no portrait box). To preview the
    ///     "no portrait" flavours, author those two PopupSOs with Custom speaker and
    ///     leave portraitOverride empty.
    ///
    /// So the four slots below are really "author one PopupSO per corner of the grid":
    ///   • dialogPopup    — intrusiveness=Intrusive,    lines w/ Azi/Bob or portraitOverride
    ///   • eventPopup     — intrusiveness=Intrusive,    lines w/ Custom + no portraitOverride
    ///   • characterPopup — intrusiveness=NonIntrusive, lines w/ Azi/Bob or portraitOverride
    ///   • textPopup      — intrusiveness=NonIntrusive, lines w/ Custom + no portraitOverride
    ///
    /// Routes through <see cref="UIManager.ShowPopup"/> directly (NOT
    /// PopupController.Show(PopupSO, ...) — that overload is unused in production and
    /// bypasses the hub's pause/modal-state management, so an Intrusive PopupSO fired
    /// through it would show without pausing the sim or blocking the action bar,
    /// unlike every real popup in-game). This tool builds the PopupRequest itself
    /// (mirroring TriggerManager.FirePopup) so intrusive popups dim, block input and
    /// pause the sim exactly as they do for real triggers.
    ///
    /// WORKS IN BUILDS (unlike EndGameScreenDebugTrigger's editor-only hotkey):
    ///   • On-screen IMGUI panel with one button per flavour + "Fire All". Toggle it with
    ///     <c>panelToggleKey</c> (default F8) or hide it at start via <c>panelVisibleOnStart</c>.
    ///   • Per-flavour hotkeys (defaults F1–F4) are NOT #if UNITY_EDITOR-gated.
    ///   • Context-menu entries on this component cover the zero-wiring editor case.
    ///
    /// "Fire All" chains the four flavours via onConfirm (dismiss one, the next appears),
    /// so a single sitting walks every layout back-to-back. Null/empty slots are skipped
    /// with a warning rather than breaking the chain.
    /// </summary>
    public class PopupDebugTrigger : MonoBehaviour
    {
        [Header("Content — one PopupSO per popup flavour (see class doc for authoring rules)")]
        [Tooltip("Set intrusiveness=Intrusive on this asset. Use Azi/Bob speaker (or Custom + portraitOverride) so the portrait box renders.")]
        [SerializeField] private PopupSO dialogPopup;
        [Tooltip("Set intrusiveness=Intrusive on this asset. Use Custom speaker with NO portraitOverride so the portrait box stays hidden.")]
        [SerializeField] private PopupSO eventPopup;
        [Tooltip("Set intrusiveness=NonIntrusive on this asset. Use Azi/Bob speaker (or Custom + portraitOverride) so the portrait box renders.")]
        [SerializeField] private PopupSO characterPopup;
        [Tooltip("Set intrusiveness=NonIntrusive on this asset. Use Custom speaker with NO portraitOverride so the portrait box stays hidden.")]
        [SerializeField] private PopupSO textPopup;

        [Header("Identity resolver")]
        [Tooltip("Required to resolve Azi/Bob display name + portrait (same asset wired into PopupController/TriggerManager). " +
                 "Unwired falls back to literal 'Azi'/'Bob' names with null portraits (PopupSO.ResolveLines' tolerant path).")]
        [SerializeField] private DialogueRegistry registry;

        [Header("Side-bubble knobs (NonIntrusive slots only — intrusive ignores these)")]
        [SerializeField] private ScreenAnchor sideAnchor = ScreenAnchor.BottomLeft;
        [Tooltip("Seconds before a side bubble auto-advances to its next line. 0 = wait for a tap.")]
        [SerializeField] private float sideAutoAdvanceSeconds = 0f;

        [Header("Hotkeys (work in builds too — set to None to disable)")]
        [SerializeField] private KeyCode panelToggleKey = KeyCode.F8;
        [SerializeField] private KeyCode dialogKey      = KeyCode.F1;
        [SerializeField] private KeyCode eventKey       = KeyCode.F2;
        [SerializeField] private KeyCode characterKey   = KeyCode.F3;
        [SerializeField] private KeyCode textKey        = KeyCode.F4;
        [SerializeField] private KeyCode fireAllKey     = KeyCode.F5;

        [Header("On-screen panel")]
        [Tooltip("Show the IMGUI button panel from scene load. Toggle at runtime with panelToggleKey.")]
        [SerializeField] private bool panelVisibleOnStart = true;

        private bool _panelVisible;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            _panelVisible = panelVisibleOnStart;
        }

        private void Update()
        {
            if (panelToggleKey != KeyCode.None && Input.GetKeyDown(panelToggleKey))
                _panelVisible = !_panelVisible;

            if (dialogKey    != KeyCode.None && Input.GetKeyDown(dialogKey))    FireDialog();
            if (eventKey     != KeyCode.None && Input.GetKeyDown(eventKey))     FireEvent();
            if (characterKey != KeyCode.None && Input.GetKeyDown(characterKey)) FireCharacter();
            if (textKey      != KeyCode.None && Input.GetKeyDown(textKey))      FireText();
            if (fireAllKey   != KeyCode.None && Input.GetKeyDown(fireAllKey))   FireAll();
        }

        // ── Public fire points (context menu / buttons / hotkeys) ────────────

        [ContextMenu("Fire Dialog Popup (intrusive + portrait)")]
        public void FireDialog() => Fire("Dialog", dialogPopup);

        [ContextMenu("Fire Event Popup (intrusive, no portrait)")]
        public void FireEvent() => Fire("Event", eventPopup);

        [ContextMenu("Fire Character Popup (side + portrait)")]
        public void FireCharacter() => Fire("Character", characterPopup);

        [ContextMenu("Fire Text Popup (side, no portrait)")]
        public void FireText() => Fire("Text", textPopup);

        /// <summary>All four flavours back-to-back — each fires when the previous is dismissed.</summary>
        [ContextMenu("Fire All (chained)")]
        public void FireAll()
        {
            Fire("Dialog", dialogPopup, () =>
                Fire("Event", eventPopup, () =>
                    Fire("Character", characterPopup, () =>
                        Fire("Text", textPopup))));
        }

        // ── Core ──────────────────────────────────────────────────────────────

        private void Fire(string label, PopupSO popup, Action onComplete = null)
        {
            var ui = UIManager.Instance;
            if (ui == null)
            {
                Debug.LogError($"[PopupDebugTrigger] UIManager.Instance is null — is the UIManager GameObject in the scene?", this);
                onComplete?.Invoke();
                return;
            }
            if (popup == null)
            {
                Debug.LogWarning($"[PopupDebugTrigger] {label} slot has no PopupSO assigned — skipping.", this);
                onComplete?.Invoke();
                return;
            }
            if (popup.lines == null || popup.lines.Count == 0)
            {
                Debug.LogWarning($"[PopupDebugTrigger] {label} PopupSO '{popup.name}' has no lines — skipping.", this);
                onComplete?.Invoke();
                return;
            }
            if (registry == null)
                Debug.LogWarning($"[PopupDebugTrigger] registry is not assigned — Azi/Bob names/portraits will fall back to literals.", this);

            var request = new PopupRequest
            {
                intrusiveness      = popup.intrusiveness,
                lines              = popup.ResolveLines(registry),
                confirmLabel       = "OK",
                onConfirm          = () =>
                {
                    Debug.Log($"[PopupDebugTrigger] {label} popup dismissed.");
                    onComplete?.Invoke();
                },
                autoDismissSeconds = sideAutoAdvanceSeconds,
                anchor             = sideAnchor,
                position           = new Vector2(popup.posX, popup.posY)   // used only when intrusiveness == Positioned
            };

            Debug.Log($"[PopupDebugTrigger] Firing {label} popup ('{popup.name}', {popup.intrusiveness}).", this);
            ui.ShowPopup(request, manageSimState: true);
        }

        // ── On-screen panel (IMGUI so it needs zero scene wiring + works in builds) ──

        private void OnGUI()
        {
            if (!_panelVisible) return;

            const float width = 210f;
            GUILayout.BeginArea(new Rect(10f, 10f, width, 240f), GUI.skin.box);
            GUILayout.Label("Popup Debug", GUI.skin.label);

            if (GUILayout.Button(ButtonLabel("Dialog (modal+portrait)", dialogKey)))  FireDialog();
            if (GUILayout.Button(ButtonLabel("Event (modal headline)", eventKey)))    FireEvent();
            if (GUILayout.Button(ButtonLabel("Character (side+portrait)", characterKey))) FireCharacter();
            if (GUILayout.Button(ButtonLabel("Text (side, bare)", textKey)))          FireText();
            if (GUILayout.Button(ButtonLabel("Fire All — chained", fireAllKey)))      FireAll();

            GUILayout.Label($"[{panelToggleKey}] hides this panel");
            GUILayout.EndArea();
        }

        private static string ButtonLabel(string label, KeyCode key)
            => key == KeyCode.None ? label : $"{label}  [{key}]";
    }
}
