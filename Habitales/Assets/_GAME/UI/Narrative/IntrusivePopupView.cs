using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Habitales.Dialogue;

namespace Habitales.UI
{
    // ─────────────────────────────────────────────────────────────────────────
    // IntrusivePopupView — the modal card (Layout A).
    //
    // Lives on its OWN GameObject and is dragged into PopupController._intrusiveView.
    // (Must be its own file so Unity exposes it in Add Component — a MonoBehaviour
    //  is only addable when its class name matches the file name.)
    //
    // Portrait/no-portrait are a SINGLE view: the portrait box is shown when
    // line.portrait != null and hidden otherwise.
    // ─────────────────────────────────────────────────────────────────────────

    public class IntrusivePopupView : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private GameObject _overlayBlocker;
        [SerializeField] private GameObject _card;

        [Header("Speaker (optional)")]
        [SerializeField] private GameObject      _portraitBox;
        [SerializeField] private Image           _portrait;
        [SerializeField] private TextMeshProUGUI _speakerLabel;

        [Header("Body")]
        [SerializeField] private TextMeshProUGUI _bodyText;

        [Header("Confirm / Next button")]
        [SerializeField] private Button          _confirmButton;

        private bool _refsOk;

        void Awake()
        {
            _refsOk = _overlayBlocker && _card && _bodyText && _confirmButton;
            if (!_refsOk)
            {
                Debug.LogError(
                    $"{name}: IntrusivePopupView is missing required serialized refs — " +
                    "wire _overlayBlocker / _card / _bodyText / _confirmButton " +
                    "in the Inspector.", this);
                enabled = false;
            }
            Hide();
        }

        /// <summary>
        /// Populate and show this modal layout for <paramref name="line"/>.
        /// The confirm/next button is re-wired to <paramref name="onConfirm"/> for
        /// this showing only; previous listeners are cleared. The button has no
        /// label (it covers the full screen), so <paramref name="confirmLabel"/>
        /// is accepted but ignored — kept so callers don't need to change.
        /// Portrait box visibility is driven by whether <c>line.portrait</c> is
        /// non-null — no separate portrait/no-portrait prefab needed.
        /// </summary>
        public void Show(ResolvedLine line, string confirmLabel, Action onConfirm)
        {
            if (!_refsOk) { onConfirm?.Invoke(); return; }

            _bodyText.text = line.body ?? string.Empty;

            bool hasPortrait = line.portrait != null;
            bool hasSpeaker  = !string.IsNullOrEmpty(line.displayName);

            if (_portraitBox) _portraitBox.SetActive(hasPortrait);
            if (_portrait && hasPortrait) _portrait.sprite = line.portrait;
            if (_speakerLabel) _speakerLabel.text = hasSpeaker ? line.displayName : string.Empty;

            _confirmButton.onClick.RemoveAllListeners();
            _confirmButton.onClick.AddListener(() => onConfirm?.Invoke());

            _overlayBlocker.SetActive(true);
            _card.SetActive(true);
        }

        /// <summary>Hide this layout (blocker + card).</summary>
        public void Hide()
        {
            if (_overlayBlocker) _overlayBlocker.SetActive(false);
            if (_card)           _card.SetActive(false);
        }
    }
}
