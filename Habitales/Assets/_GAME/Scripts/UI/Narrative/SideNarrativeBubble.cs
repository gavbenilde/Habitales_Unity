using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Habitales.Dialogue;

namespace Habitales.UI
{
    /// <summary>
    /// The non-intrusive narrative bubble: a small text box anchored to a screen corner
    /// that never dims the world, never blocks input, and never pauses the sim. With a
    /// portrait box it is the "Character Popup" (Azi nudges); with the portrait hidden it
    /// is the bare "Text Popup". Plays a thread of lines, advancing on tap or after an
    /// optional auto-advance delay.
    ///
    /// Generalises the prototype AziSpeechBubbleUI (self-heal + loud-fail kept). Driven by
    /// <see cref="NarrativePopupManager"/>; wire one into the HUD canvas and assign it there.
    /// </summary>
    public class SideNarrativeBubble : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private RectTransform root;          // the bubble container that gets anchored
        [SerializeField] private CanvasGroup canvasGroup;     // optional — for fade-in juice

        [Header("Speaker")]
        [SerializeField] private GameObject portraitBox;      // hidden for Text style / portrait-less lines
        [SerializeField] private Image portrait;
        [SerializeField] private TextMeshProUGUI speakerLabel;

        [Header("Body")]
        [SerializeField] private TextMeshProUGUI bodyText;

        [Header("Advance")]
        [SerializeField] private Button tapTarget;            // a button over the bubble — click = next line

        [Header("Anchoring")]
        [SerializeField] private Vector2 margin = new Vector2(32f, 32f);

        [Header("Entrance juice (LeanTween)")]
        [SerializeField] private bool animateIn = true;
        [SerializeField] private float slideDistance = 40f;
        [SerializeField] private float animTime = 0.22f;

        private readonly List<ResolvedLine> _lines = new List<ResolvedLine>();
        private int _index;
        private bool _showPortrait;
        private float _autoAdvance;
        private Action _onComplete;
        private bool _refsOk;

        void Awake()
        {
            if (root == null) root = transform as RectTransform;
            _refsOk = root && bodyText && tapTarget;
            if (!_refsOk)
            {
                Debug.LogError($"{name}: SideNarrativeBubble is missing serialized refs — wire root / bodyText / tapTarget in the Inspector.", this);
                enabled = false;
                return;
            }
            tapTarget.onClick.AddListener(Advance);
            Hide();
        }

        /// <summary>Begin playing the supplied lines as a side bubble. Never pauses the sim.</summary>
        public void Play(IList<ResolvedLine> lines, in PopupStyle style, Action onComplete)
        {
            if (!_refsOk) { onComplete?.Invoke(); return; }
            if (lines == null || lines.Count == 0) { onComplete?.Invoke(); return; }

            // Self-heal: a designer may have toggled the bubble off in-scene. We can recover our
            // own GameObject, but not a disabled PARENT — that needs a manual scene fix.
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            if (!gameObject.activeInHierarchy)
            {
                Debug.LogError($"{name}: a PARENT is inactive — bubble can't show. Firing onComplete so the caller isn't softlocked. Enable the parent in the scene.", this);
                onComplete?.Invoke();
                return;
            }

            _lines.Clear();
            _lines.AddRange(lines);
            _index = 0;
            _showPortrait = style.showPortrait;
            _autoAdvance = style.autoAdvanceSeconds;
            _onComplete = onComplete;

            ApplyAnchor(style.anchor);
            gameObject.SetActive(true);
            RenderCurrent();
            PlayEntrance(style.anchor);
        }

        private void RenderCurrent()
        {
            CancelInvoke(nameof(Advance));

            ResolvedLine line = _lines[_index];
            bodyText.text = line.body;
            if (speakerLabel) speakerLabel.text = line.displayName;

            bool drawPortrait = _showPortrait && line.portrait != null;
            if (portraitBox) portraitBox.SetActive(drawPortrait);
            if (drawPortrait && portrait) portrait.sprite = line.portrait;

            if (_autoAdvance > 0f) Invoke(nameof(Advance), _autoAdvance);
        }

        private void Advance()
        {
            CancelInvoke(nameof(Advance));
            _index++;
            if (_index >= _lines.Count)
            {
                Finish();
                return;
            }
            RenderCurrent();
        }

        private void Finish()
        {
            Hide();
            Action cb = _onComplete;
            _onComplete = null;
            cb?.Invoke();
        }

        public void Hide()
        {
            CancelInvoke(nameof(Advance));
            if (root) root.gameObject.SetActive(false);
        }

        // ── Layout & juice ─────────────────────────────────────────────

        private void ApplyAnchor(ScreenAnchor anchor)
        {
            Vector2 a; // anchor (== pivot) per corner
            switch (anchor)
            {
                case ScreenAnchor.BottomRight:  a = new Vector2(1f, 0f); break;
                case ScreenAnchor.TopLeft:      a = new Vector2(0f, 1f); break;
                case ScreenAnchor.TopRight:     a = new Vector2(1f, 1f); break;
                case ScreenAnchor.BottomCenter: a = new Vector2(0.5f, 0f); break;
                default:                        a = new Vector2(0f, 0f); break; // BottomLeft
            }
            root.anchorMin = a;
            root.anchorMax = a;
            root.pivot = a;

            float mx = a.x <= 0.5f ? margin.x : -margin.x;
            float my = a.y < 0.5f ? margin.y : -margin.y;
            if (a.x == 0.5f) mx = 0f;
            root.anchoredPosition = new Vector2(mx, my);
        }

        private void PlayEntrance(ScreenAnchor anchor)
        {
            if (root) root.gameObject.SetActive(true);
            if (!animateIn) { if (canvasGroup) canvasGroup.alpha = 1f; return; }

            Vector2 target = root.anchoredPosition;
            float dir = anchor == ScreenAnchor.BottomRight || anchor == ScreenAnchor.TopRight ? 1f : -1f;
            root.anchoredPosition = target + new Vector2(dir * slideDistance, 0f);

            LeanTween.cancel(root.gameObject);
            LeanTween.move(root, target, animTime).setEaseOutCubic().setIgnoreTimeScale(true);
            if (canvasGroup)
            {
                canvasGroup.alpha = 0f;
                LeanTween.alphaCanvas(canvasGroup, 1f, animTime).setIgnoreTimeScale(true);
            }
        }
    }
}
