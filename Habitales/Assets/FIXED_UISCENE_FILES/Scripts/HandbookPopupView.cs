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
        [SerializeField] private Animator        _portraitAnimator;
        [SerializeField] private TextMeshProUGUI _speakerLabel; // could be used for the Title Text

        [Header("Body")]
        [SerializeField] private TextMeshProUGUI _title;
        [SerializeField] private TextMeshProUGUI _body;

        [Header("Buttons")]
        [SerializeField] private Button          _nextButton;
        [SerializeField] private TextMeshProUGUI _nextButtonText;
        [SerializeField] private Button          _backButton;
        [SerializeField] private TextMeshProUGUI _backButtonText;
        
        private bool _refsOk;

        void Awake()
        {
            _refsOk = _overlayBlocker && _card && _body && _nextButton && _nextButtonText;
            if (!_refsOk)
            {
                Debug.LogError(
                    $"{name}: HandbookPopupView is missing required serialized refs — " +
                    "wire _overlayBlocker / _card / _body / _confirmButton / _confirmLabel " +
                    "in the Inspector.", this);
                // enabled = false;
            }
            Hide();
        }

        public void Show(ResolvedLine line, string confirmLabel, Action onConfirm, Action onPrevious)
        {
            if (!_refsOk) { onConfirm?.Invoke(); return; }
            
            _body.text     = line.body ?? string.Empty;
            _nextButtonText.text = confirmLabel;

            bool hasSpeaker  = !string.IsNullOrEmpty(line.displayName);
            
            if (_portraitBox) 
                _portraitBox.SetActive(true);
            if (_portrait)
            {
                _portrait.sprite = line.portrait;
                _portrait.color = line.portrait != null 
                    ? Color.white 
                    : new Color32(255, 252, 214, 255);
            }
            if (_speakerLabel) 
                _speakerLabel.text = hasSpeaker ? line.displayName : string.Empty;

            if (_portraitAnimator)
            {
                _portraitAnimator.enabled = line.portraitAnimator != null;

                if (line.portraitAnimator != null)
                {
                    _portraitAnimator.runtimeAnimatorController = line.portraitAnimator;
                    _portraitAnimator.Rebind();
                    _portraitAnimator.Update(0f);
                }
                else
                {
                    _portraitAnimator.runtimeAnimatorController = null;
                }
            }
            
            _title.text = line.title;

            _nextButton.onClick.RemoveAllListeners();
            _nextButton.onClick.AddListener(() => onConfirm?.Invoke());
            
            _backButton.onClick.RemoveAllListeners();
            _backButton.onClick.AddListener(() => onPrevious?.Invoke());

            _overlayBlocker.SetActive(true);
            _card.SetActive(true);
        }

        /// <summary>Hide this layout (blocker + card).</summary>
        public void Hide()
        {
            if (_overlayBlocker) _overlayBlocker.SetActive(false);
            if (_card)           _card.SetActive(false);
        }
        
        // Dirty function to disable Button's interactable
        public void SetBackInteractable(bool interactable)
        {
            _backButton.interactable = interactable;
        }
    }
}