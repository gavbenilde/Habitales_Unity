using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Habitales.Dialogue;

namespace Habitales.UI
{
    public class HandbookPopupView : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private GameObject _overlayBlocker;
        [SerializeField] private GameObject _card;

        [Header("Speaker (optional)")]
        [SerializeField] private GameObject      _portraitBox;
        [SerializeField] private Image           _portrait;
        [SerializeField] private TextMeshProUGUI _speakerLabel; // could be used for the Title Text

        [Header("Body")]
        [SerializeField] private string _titleText;
        [SerializeField] private TextMeshProUGUI _bodyText;

        [Header("Confirm / Next button")]
        [SerializeField] private Button          _nextButton;
        [SerializeField] private TextMeshProUGUI _nextButtonText;
        
        private bool _refsOk;

        void Awake()
        {
            _refsOk = _overlayBlocker && _card && _bodyText && _nextButton && _nextButtonText;
            if (!_refsOk)
            {
                Debug.LogError(
                    $"{name}: HandbookPopupView is missing required serialized refs — " +
                    "wire _overlayBlocker / _card / _bodyText / _confirmButton / _confirmLabel " +
                    "in the Inspector.", this);
                enabled = false;
            }
            Hide();
        }

        public void Show(ResolvedLine line, string confirmLabel, Action onConfirm)
        {
            if (!_refsOk) { onConfirm?.Invoke(); return; }
            
            
            _bodyText.text     = line.body ?? string.Empty;
            _nextButtonText.text = confirmLabel;

            bool hasPortrait = line.portrait != null;
            bool hasSpeaker  = !string.IsNullOrEmpty(line.displayName);

            if (_portraitBox) _portraitBox.SetActive(hasPortrait);
            if (_portrait && hasPortrait) _portrait.sprite = line.portrait;
            if (_speakerLabel) _speakerLabel.text = hasSpeaker ? line.displayName : string.Empty;

            _nextButton.onClick.RemoveAllListeners();
            _nextButton.onClick.AddListener(() => onConfirm?.Invoke());

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