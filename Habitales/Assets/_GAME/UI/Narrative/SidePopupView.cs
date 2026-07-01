using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Habitales.Dialogue;

namespace Habitales.UI
{
    // ─────────────────────────────────────────────────────────────────────────
    // SidePopupView — the non-intrusive side bubble (Layout B).
    //
    // Lives on its OWN GameObject and is dragged into PopupController._sideBubble.
    // (Must be its own file so Unity exposes it in Add Component — a MonoBehaviour
    //  is only addable when its class name matches the file name.)
    //
    // Show accepts a single ResolvedLine (per-line portrait/speaker); the
    // PopupController feeds it one line at a time.
    // ─────────────────────────────────────────────────────────────────────────

    public class SidePopupView : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private RectTransform _root;
        [SerializeField] private CanvasGroup   _canvasGroup;

        [Header("Speaker (optional)")]
        [SerializeField] private GameObject      _portraitBox;
        [SerializeField] private Image           _portrait;
        [SerializeField] private TextMeshProUGUI _speakerLabel;

        [Header("Body")]
        [SerializeField] private TextMeshProUGUI _bodyText;

        [Header("Tap target")]
        [SerializeField] private Button _tapTarget;

        [Header("Anchoring")]
        [SerializeField] private Vector2 _margin = new Vector2(32f, 32f);

        [Header("Entrance animation (LeanTween)")]
        [SerializeField] private bool  _animateIn     = true;
        [SerializeField] private float _slideDistance = 40f;
        [SerializeField] private float _animTime      = 0.22f;

        private bool   _refsOk;
        private Action _onTap;

        void Awake()
        {
            if (_root == null) _root = transform as RectTransform;
            _refsOk = _root && _bodyText && _tapTarget;
            if (!_refsOk)
            {
                Debug.LogError(
                    $"{name}: SidePopupView is missing required serialized refs — " +
                    "wire _root / _bodyText / _tapTarget in the Inspector.", this);
                enabled = false;
            }
            Hide();
        }

        /// <summary>
        /// Show this side bubble for <paramref name="line"/>.
        /// <paramref name="onTap"/> is called when the player taps the bubble
        /// (PopupController advances to the next line or dismisses).
        /// Portrait visibility is driven per-line by <c>line.portrait</c>.
        /// </summary>
        public void Show(ResolvedLine line, ScreenAnchor anchor, Action onTap)
        {
            if (!_refsOk) { onTap?.Invoke(); return; }

            if (!gameObject.activeSelf) gameObject.SetActive(true);
            if (!gameObject.activeInHierarchy)
            {
                Debug.LogError(
                    $"{name}: a PARENT is inactive — SidePopupView can't show. " +
                    "Enable the parent GameObject in the scene.", this);
                onTap?.Invoke();
                return;
            }

            _onTap = onTap;

            _bodyText.text = line.body ?? string.Empty;
            if (_speakerLabel) _speakerLabel.text = line.displayName ?? string.Empty;

            bool drawPortrait = line.portrait != null;
            if (_portraitBox) _portraitBox.SetActive(drawPortrait);
            if (drawPortrait && _portrait) _portrait.sprite = line.portrait;

            _tapTarget.onClick.RemoveAllListeners();
            _tapTarget.onClick.AddListener(() => _onTap?.Invoke());

            ApplyAnchor(anchor);
            if (_root) _root.gameObject.SetActive(true);
            PlayEntrance(anchor);
        }

        /// <summary>Hide the side bubble.</summary>
        public void Hide()
        {
            _onTap = null;
            if (_tapTarget) _tapTarget.onClick.RemoveAllListeners();
            if (_root) _root.gameObject.SetActive(false);
        }

        // ── Layout & juice (mirrors SideNarrativeBubble) ─────────────────────

        private void ApplyAnchor(ScreenAnchor anchor)
        {
            Vector2 a;
            switch (anchor)
            {
                case ScreenAnchor.BottomRight:  a = new Vector2(1f, 0f);    break;
                case ScreenAnchor.TopLeft:      a = new Vector2(0f, 1f);    break;
                case ScreenAnchor.TopRight:     a = new Vector2(1f, 1f);    break;
                case ScreenAnchor.BottomCenter: a = new Vector2(0.5f, 0f);  break;
                default:                        a = new Vector2(0f, 0f);    break; // BottomLeft
            }
            _root.anchorMin = a;
            _root.anchorMax = a;
            _root.pivot     = a;

            float mx = a.x <= 0.5f ? _margin.x : -_margin.x;
            float my = a.y < 0.5f  ? _margin.y : -_margin.y;
            if (a.x == 0.5f) mx = 0f;
            _root.anchoredPosition = new Vector2(mx, my);
        }

        private void PlayEntrance(ScreenAnchor anchor)
        {
            if (_root) _root.gameObject.SetActive(true);
            if (!_animateIn) { if (_canvasGroup) _canvasGroup.alpha = 1f; return; }

            Vector2 target = _root.anchoredPosition;
            float dir = (anchor == ScreenAnchor.BottomRight || anchor == ScreenAnchor.TopRight) ? 1f : -1f;
            _root.anchoredPosition = target + new Vector2(dir * _slideDistance, 0f);

            LeanTween.cancel(_root.gameObject);
            LeanTween.move(_root, target, _animTime).setEaseOutCubic().setIgnoreTimeScale(true);
            if (_canvasGroup)
            {
                _canvasGroup.alpha = 0f;
                LeanTween.alphaCanvas(_canvasGroup, 1f, _animTime).setIgnoreTimeScale(true);
            }
        }
    }
}
