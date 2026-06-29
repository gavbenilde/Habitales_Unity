using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Habitales.UI
{
    // ─────────────────────────────────────────────────────────────────────────
    // PopupController.cs — U2: the single presentation surface for all four
    // event-popup variants (intrusive/non-intrusive × portrait/no-portrait).
    //
    // CONTRACT (see UI_ARCHITECTURE_HANDOFF §5.3 + §3):
    //   Downward: UIManager calls Show(in PopupRequest) / Dismiss(PopupHandle).
    //   Upward  : OnPopupDismissed raised on every dismissal — UIManager listens
    //             to un-pause the sim and clear modal state (Intrusive path).
    //
    // LAYOUTS (wire in Inspector):
    //   _intrusivePortrait    — modal card, blocker, confirm button, portrait box visible.
    //   _intrusiveNoPortrait  — same card without portrait box.
    //   _sidePortrait         — SideNarrativeBubble with portrait box visible (reused).
    //   _sideNoPortrait       — SideNarrativeBubble, portrait box hidden.
    //
    // NOTE: the four concrete slots are the TWO Intrusive views (the modal card,
    // toggled by showPortrait) and the single SideNarrativeBubble (portrait
    // toggled on/off). This gives the required 2×2 reachability while minimising
    // prefab count. See wiring steps at the end of this file.
    //
    // DOES NOT touch RunManager.PauseForEvent — UIManager owns the pause gate
    // (ShowPopup routing). PopupController is pure presentation.
    //
    // S2: popup-presentation logic lives ONLY here. NarrativePopupManager is the
    // existing facade that routes EventManager calls into this controller (or the
    // legacy EventPopupUI) — do not duplicate logic across both.
    // ─────────────────────────────────────────────────────────────────────────

    public class PopupController : MonoBehaviour, IUISubsystem
    {
        // ── IUISubsystem ──────────────────────────────────────────────────────

        public string SubsystemId => "popups";

        public bool IsVisible => _root != null && _root.activeSelf;

        public void SetVisible(bool visible)
        {
            if (_root != null) _root.SetActive(visible);
        }

        // ── Serialized views ──────────────────────────────────────────────────

        [Header("Root")]
        [Tooltip("Parent that contains all popup layouts. Toggle this for master hide/show.")]
        [SerializeField] private GameObject _root;

        [Header("Intrusive modal layouts")]
        [Tooltip("LAYOUT A — Intrusive + portrait. Wire the modal card that shows a speaker portrait.")]
        [SerializeField] private IntrusivePopupView _intrusivePortrait;

        [Tooltip("LAYOUT B — Intrusive + no-portrait. Wire the modal card without a portrait box.")]
        [SerializeField] private IntrusivePopupView _intrusiveNoPortrait;

        [Header("Non-intrusive side-bubble layout")]
        [Tooltip("LAYOUT C/D — SideNarrativeBubble (portrait box toggled by showPortrait).")]
        [SerializeField] private SidePopupView _sideBubble;

        // ── Dismissal event (upward contract) ─────────────────────────────────

        /// <summary>
        /// Raised whenever a popup is dismissed (button, auto-dismiss, or Dismiss call).
        /// UIManager subscribes and un-pauses the sim for Intrusive dismissals.
        /// </summary>
        public event Action<PopupHandle, PopupIntrusiveness> OnPopupDismissed;

        // ── Handle counter ────────────────────────────────────────────────────

        private int _nextId;
        private PopupHandle _activeIntrusive = PopupHandle.None;   // at most one intrusive at a time
        private readonly List<PopupHandle> _activeSide = new();    // multiple side bubbles queue

        // ── Coroutine tracking for auto-dismiss ───────────────────────────────

        private Coroutine _sideAutoDismiss;

        // ─── Lifecycle ────────────────────────────────────────────────────────

        void Awake()
        {
            ValidateRefs();
        }

        private void ValidateRefs()
        {
            bool ok = true;

            if (_root == null)
            {
                Debug.LogError($"{name}: _root is not wired — assign the root GameObject in the Inspector.", this);
                ok = false;
            }
            if (_intrusivePortrait == null)
            {
                Debug.LogError($"{name}: _intrusivePortrait view is not wired — wire Layout A (intrusive + portrait) in the Inspector.", this);
                ok = false;
            }
            if (_intrusiveNoPortrait == null)
            {
                Debug.LogError($"{name}: _intrusiveNoPortrait view is not wired — wire Layout B (intrusive + no-portrait) in the Inspector.", this);
                ok = false;
            }
            if (_sideBubble == null)
            {
                Debug.LogError($"{name}: _sideBubble view is not wired — wire the SidePopupView (Layout C/D) in the Inspector.", this);
                ok = false;
            }

            if (!ok) { enabled = false; return; }

            // Hide all layouts on startup.
            _intrusivePortrait.Hide();
            _intrusiveNoPortrait.Hide();
            _sideBubble.Hide();
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Show a popup described by <paramref name="request"/> and return a handle.
        /// Called by UIManager.ShowPopup (hub routes all callers through here).
        /// </summary>
        public PopupHandle Show(in PopupRequest request)
        {
            if (!enabled) return PopupHandle.None;

            var handle = new PopupHandle(_nextId++, request.intrusiveness);

            if (request.intrusiveness == PopupIntrusiveness.Intrusive)
            {
                ShowIntrusive(request, handle);
            }
            else
            {
                ShowSide(request, handle);
            }

            return handle;
        }

        /// <summary>
        /// Dismiss a specific popup by handle. Safe to call with <see cref="PopupHandle.None"/>
        /// or a handle that has already been dismissed.
        /// </summary>
        public void Dismiss(PopupHandle handle)
        {
            if (!handle.IsValid) return;

            if (handle.intrusiveness == PopupIntrusiveness.Intrusive)
            {
                if (_activeIntrusive.id == handle.id)
                    DismissIntrusive(handle);
            }
            else
            {
                if (_activeSide.Contains(handle))
                    DismissSide(handle);
            }
        }

        // ─── Intrusive path ───────────────────────────────────────────────────

        private void ShowIntrusive(in PopupRequest req, PopupHandle handle)
        {
            // Only one intrusive at a time — dismiss any active one first.
            if (_activeIntrusive.IsValid)
            {
                DismissIntrusive(_activeIntrusive);
            }
            _activeIntrusive = handle;

            IntrusivePopupView view = req.showPortrait ? _intrusivePortrait : _intrusiveNoPortrait;

            // Hide whichever view is NOT being used (keeps the canvas tidy).
            IntrusivePopupView other = req.showPortrait ? _intrusiveNoPortrait : _intrusivePortrait;
            other.Hide();

            Action onConfirmCopy = req.onConfirm;    // capture outside the lambda

            view.Show(
                body:         req.body,
                speakerName:  req.speakerName,
                portrait:     req.portrait,
                confirmLabel: string.IsNullOrEmpty(req.confirmLabel) ? "OK" : req.confirmLabel,
                onConfirm: () =>
                {
                    onConfirmCopy?.Invoke();
                    DismissIntrusive(handle);
                }
            );
        }

        private void DismissIntrusive(PopupHandle handle)
        {
            if (_activeIntrusive.id != handle.id) return;

            _intrusivePortrait.Hide();
            _intrusiveNoPortrait.Hide();
            _activeIntrusive = PopupHandle.None;

            OnPopupDismissed?.Invoke(handle, PopupIntrusiveness.Intrusive);
        }

        // ─── Non-intrusive (side bubble) path ─────────────────────────────────

        private void ShowSide(in PopupRequest req, PopupHandle handle)
        {
            _activeSide.Add(handle);

            if (_sideAutoDismiss != null)
            {
                StopCoroutine(_sideAutoDismiss);
                _sideAutoDismiss = null;
            }

            Action onConfirmCopy = req.onConfirm;

            _sideBubble.Show(
                body:          req.body,
                speakerName:   req.speakerName,
                portrait:      req.portrait,
                showPortrait:  req.showPortrait,
                anchor:        req.anchor,
                onTap: () =>
                {
                    onConfirmCopy?.Invoke();
                    DismissSide(handle);
                }
            );

            if (req.autoDismissSeconds > 0f)
            {
                _sideAutoDismiss = StartCoroutine(AutoDismissSide(handle, req.autoDismissSeconds, onConfirmCopy));
            }
        }

        private void DismissSide(PopupHandle handle)
        {
            if (!_activeSide.Contains(handle)) return;
            _activeSide.Remove(handle);

            if (_sideAutoDismiss != null)
            {
                StopCoroutine(_sideAutoDismiss);
                _sideAutoDismiss = null;
            }

            _sideBubble.Hide();

            OnPopupDismissed?.Invoke(handle, PopupIntrusiveness.NonIntrusive);
        }

        private IEnumerator AutoDismissSide(PopupHandle handle, float seconds, Action onConfirm)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (_activeSide.Contains(handle))
            {
                onConfirm?.Invoke();
                DismissSide(handle);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IntrusivePopupView — the modal card (layouts A and B).
    // A self-contained component; wire it on any GameObject inside the popup canvas.
    // ─────────────────────────────────────────────────────────────────────────

    [Serializable]
    public class IntrusivePopupView : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private GameObject _overlayBlocker;   // full-screen dim + input block
        [SerializeField] private GameObject _card;             // centred dialogue card

        [Header("Speaker (optional)")]
        [SerializeField] private GameObject        _portraitBox;   // hide for no-portrait variant
        [SerializeField] private Image             _portrait;
        [SerializeField] private TextMeshProUGUI   _speakerLabel;

        [Header("Body")]
        [SerializeField] private TextMeshProUGUI _bodyText;

        [Header("Confirm")]
        [SerializeField] private Button          _confirmButton;
        [SerializeField] private TextMeshProUGUI _confirmLabel;

        private bool _refsOk;

        void Awake()
        {
            _refsOk = _overlayBlocker && _card && _bodyText && _confirmButton && _confirmLabel;
            if (!_refsOk)
            {
                Debug.LogError(
                    $"{name}: IntrusivePopupView is missing serialized refs — " +
                    "wire _overlayBlocker / _card / _bodyText / _confirmButton / _confirmLabel " +
                    "in the Inspector.", this);
                enabled = false;
            }
            Hide(); // ensure hidden at startup
        }

        /// <summary>
        /// Populate and show this modal layout.
        /// <paramref name="onConfirm"/> is wired to the confirm button for this showing
        /// only; previous listeners are cleared first.
        /// </summary>
        public void Show(string body, string speakerName, Sprite portrait,
                         string confirmLabel, Action onConfirm)
        {
            if (!_refsOk) { onConfirm?.Invoke(); return; }

            _bodyText.text    = body ?? string.Empty;
            _confirmLabel.text = confirmLabel;

            bool hasSpeaker = !string.IsNullOrEmpty(speakerName);
            if (_portraitBox) _portraitBox.SetActive(hasSpeaker && portrait != null);
            if (_speakerLabel) _speakerLabel.text = hasSpeaker ? speakerName : string.Empty;
            if (_portrait && portrait != null) _portrait.sprite = portrait;

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

    // ─────────────────────────────────────────────────────────────────────────
    // SidePopupView — the non-intrusive side bubble (layouts C and D).
    // Wraps (and reuses) the existing SideNarrativeBubble positioning logic by
    // directly driving the same fields. Wire this on the same or a sibling
    // GameObject as the SideNarrativeBubble (or replace it once fully routed).
    // ─────────────────────────────────────────────────────────────────────────

    [Serializable]
    public class SidePopupView : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private RectTransform   _root;          // bubble container
        [SerializeField] private CanvasGroup     _canvasGroup;   // optional — fade-in juice

        [Header("Speaker (optional)")]
        [SerializeField] private GameObject      _portraitBox;
        [SerializeField] private Image           _portrait;
        [SerializeField] private TextMeshProUGUI _speakerLabel;

        [Header("Body")]
        [SerializeField] private TextMeshProUGUI _bodyText;

        [Header("Tap target")]
        [SerializeField] private Button _tapTarget;             // tap = advance/dismiss

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
                    $"{name}: SidePopupView is missing serialized refs — " +
                    "wire _root / _bodyText / _tapTarget in the Inspector.", this);
                enabled = false;
            }
            Hide();
        }

        /// <summary>
        /// Populate and show the side bubble.
        /// <paramref name="onTap"/> is called when the player taps the bubble.
        /// </summary>
        public void Show(string body, string speakerName, Sprite portrait,
                         bool showPortrait, ScreenAnchor anchor, Action onTap)
        {
            if (!_refsOk) { onTap?.Invoke(); return; }

            // Self-heal own deactivation (not parent — that requires manual scene fix).
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

            _bodyText.text = body ?? string.Empty;
            if (_speakerLabel) _speakerLabel.text = speakerName ?? string.Empty;

            bool drawPortrait = showPortrait && portrait != null;
            if (_portraitBox) _portraitBox.SetActive(drawPortrait);
            if (drawPortrait && _portrait) _portrait.sprite = portrait;

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
                case ScreenAnchor.BottomRight:  a = new Vector2(1f, 0f); break;
                case ScreenAnchor.TopLeft:      a = new Vector2(0f, 1f); break;
                case ScreenAnchor.TopRight:     a = new Vector2(1f, 1f); break;
                case ScreenAnchor.BottomCenter: a = new Vector2(0.5f, 0f); break;
                default:                        a = new Vector2(0f, 0f); break; // BottomLeft
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

// ─────────────────────────────────────────────────────────────────────────────
// WIRING STEPS (human — Inspector only, no code edits needed)
// ─────────────────────────────────────────────────────────────────────────────
//
// 1. POPUP PREFAB VARIANTS
//    Build four GameObjects (or two reused with portrait-box toggling). The
//    minimum working setup is:
//
//    Layout A — Intrusive + Portrait
//      GameObject "PopupCard_IntrusivePortrait"
//        ├─ Image "OverlayBlocker"       (full-screen, dark semi-transparent)
//        └─ GameObject "Card"
//             ├─ GameObject "PortraitBox"
//             │    ├─ Image "Portrait"
//             │    └─ TextMeshPro "SpeakerLabel"
//             ├─ TextMeshPro "BodyText"
//             └─ Button "ConfirmButton"
//                  └─ TextMeshPro "ConfirmLabel"
//      Add component: IntrusivePopupView. Wire every field.
//
//    Layout B — Intrusive + No-Portrait
//      Same structure; omit / leave empty the PortraitBox.
//      Add component: IntrusivePopupView. Wire every field.
//
//    Layout C/D — Side Bubble (portrait toggled at runtime)
//      GameObject "SideBubble"
//        ├─ RectTransform "Root"         (this is also the SidePopupView root)
//        │    ├─ CanvasGroup             (optional — for fade juice)
//        │    ├─ GameObject "PortraitBox"
//        │    │    ├─ Image "Portrait"
//        │    │    └─ TextMeshPro "SpeakerLabel"
//        │    ├─ TextMeshPro "BodyText"
//        │    └─ Button "TapTarget"      (invisible overlay over the whole bubble)
//      Add component: SidePopupView. Wire every field. Set margin / animateIn as desired.
//      (Portrait box visibility is driven at runtime by PopupController.ShowSide via
//       SidePopupView.Show — no separate C/D prefab needed.)
//
// 2. POPUP CONTROLLER SLOTS
//    On the PopupController component in the scene:
//      _root               ← the parent GameObject that contains all layouts
//      _intrusivePortrait  ← drag the IntrusivePopupView component from Layout A
//      _intrusiveNoPortrait← drag the IntrusivePopupView component from Layout B
//      _sideBubble         ← drag the SidePopupView component from Layout C/D
//
// 3. UI MANAGER REGISTRATION
//    On the UIManager component in the scene:
//      subsystems list     ← add the PopupController MonoBehaviour
//      popups slot         ← drag the same PopupController component
//
// ─────────────────────────────────────────────────────────────────────────────
// CONVERGENCE NOTE — NarrativePopupManager / EventPopupUI façade
// ─────────────────────────────────────────────────────────────────────────────
//
// The existing EventManager already routes through NarrativePopupManager
// (facade.ShowHeadline → EventPopupUI.Show) with a fallback directly to
// EventPopupUI. PopupController is the NEW single surface; it is NOT yet wired
// into that path. To converge:
//
//   Step 1 (now — no code change): leave NarrativePopupManager in place.
//          PopupController and NarrativePopupManager coexist. New callers
//          (OnboardingDirector rework, direct UIManager.ShowPopup calls) go
//          through UIManager → PopupController.
//
//   Step 2 (next sprint): add a bridge method on NarrativePopupManager or add a
//          one-liner in EventManager.FireEvent that calls
//          UIManager.Instance.ShowPopup(...) instead of facade.ShowHeadline.
//          The bridge wraps GameEventSO fields into a PopupRequest and passes
//          the ResumeAfterEvent callback as onConfirm. Once wired, the
//          NarrativePopupManager.ShowHeadline path becomes dead code.
//
//   Step 3 (cleanup): once all callers route through UIManager → PopupController,
//          delete NarrativePopupManager.ShowHeadline and EventPopupUI (the
//          legacy one-shot). The intrusive path in PopupController's
//          IntrusivePopupView replaces them. NarrativePopupManager's PlayThread/
//          PlayLines/Say API can be replaced by PopupController.Show with
//          appropriate PopupRequest fields, or retained as a thin adapter that
//          builds PopupRequests.
//
// DO NOT delete NarrativePopupManager or EventPopupUI until Step 2 is complete
// and EventManager no longer references them — the live build event system
// depends on them being present.
// ─────────────────────────────────────────────────────────────────────────────
