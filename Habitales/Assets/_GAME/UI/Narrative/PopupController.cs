using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Habitales.Dialogue;

namespace Habitales.UI
{
    // ─────────────────────────────────────────────────────────────────────────
    // PopupController.cs — U2: thread-capable popup presenter (WO-1 revision).
    //
    // CONTRACT (see UI_ARCHITECTURE.md §5.3 + §3):
    //   Downward: UIManager calls Show(in PopupRequest) / Dismiss(PopupHandle).
    //             Convenience overload: Show(PopupSO, Action) resolves
    //             Azi/Bob identity from DialogueRegistry then delegates down.
    //   Upward  : OnPopupDismissed raised on every dismissal — UIManager listens
    //             to un-pause the sim and clear modal state (Intrusive path).
    //
    // PAGING MODEL:
    //   The controller owns the paging state (_lines / _index). On each advance:
    //     Intrusive  — button re-labels "Next" (intermediate) / confirmLabel (last);
    //                  pressing it on the last line dismisses.
    //     NonIntrusive — tap always advances; autoDismissSeconds > 0 auto-advances
    //                   per line via Invoke; final advance dismisses.
    //
    // LAYOUTS (wire in Inspector):
    //   _intrusiveView  — single modal card (portrait box toggled per-line by
    //                     IntrusivePopupView when line.portrait != null).
    //   _sideBubble     — SidePopupView (portrait toggled per-line at runtime).
    //
    //   NOTE: the two-slot _intrusivePortrait / _intrusiveNoPortrait that existed
    //   before WO-1 have been collapsed into a SINGLE _intrusiveView whose
    //   portrait box is shown/hidden per line. Human re-wiring is required
    //   (see HUMAN WIRING STEPS at the bottom of this file).
    //
    // DOES NOT touch RunManager.PauseForEvent — UIManager owns the pause gate.
    // PopupController is pure presentation.
    //
    // S2: popup-presentation logic lives ONLY here. NarrativePopupManager is the
    // existing facade that routes EventManager calls into the legacy EventPopupUI
    // path — do not duplicate logic between both.
    //
    // Edited 2026-06-30 (WO-1): thread paging, PopupSO resolver, collapsed view.
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

        // ── Serialized refs ───────────────────────────────────────────────────

        [Header("Root")]
        [Tooltip("Parent that contains all popup layouts. Toggle for master hide/show.")]
        [SerializeField] private GameObject _root;

        [Header("Intrusive modal layout")]
        [Tooltip("LAYOUT A — single modal card. Portrait box is toggled per-line at runtime.")]
        [SerializeField] private IntrusivePopupView _intrusiveView;

        [Header("Non-intrusive side-bubble layout")]
        [Tooltip("LAYOUT B — SideNarrativeBubble-style side bubble (portrait toggled per-line).")]
        [SerializeField] private SidePopupView _sideBubble;

        [Header("Character identity resolver")]
        [Tooltip("Required for Show(PopupSO). Provides Azi/Bob display name + portrait. " +
                 "Loud-fails in the resolver overload when null.")]
        [SerializeField] private DialogueRegistry _registry;

        // ── Dismissal event (upward contract) ─────────────────────────────────

        /// <summary>
        /// Raised whenever a popup is dismissed (button, auto-dismiss, or Dismiss call).
        /// UIManager subscribes and un-pauses the sim for Intrusive dismissals.
        /// </summary>
        public event Action<PopupHandle, PopupIntrusiveness> OnPopupDismissed;

        // ── Handle counter ────────────────────────────────────────────────────

        private int _nextId;
        private PopupHandle _activeIntrusive = PopupHandle.None;
        private readonly List<PopupHandle> _activeSide = new List<PopupHandle>();

        // ── Paging state — intrusive path ─────────────────────────────────────

        private readonly List<ResolvedLine> _intrusiveLines  = new List<ResolvedLine>();
        private int                          _intrusiveIndex;
        private string                       _intrusiveConfirmLabel;
        private Action                       _intrusiveOnConfirm;
        private PopupHandle                  _intrusivePendingHandle;

        // ── Paging state — side path ──────────────────────────────────────────

        private readonly List<ResolvedLine> _sideLines  = new List<ResolvedLine>();
        private int                          _sideIndex;
        private float                        _sideAutoDismissSecs;
        private Action                       _sideOnConfirm;
        private PopupHandle                  _sidePendingHandle;
        private Coroutine                    _sideAutoCoroutine;

        // ── Button labels ─────────────────────────────────────────────────────

        private const string NextWord = "Next";

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
            if (_intrusiveView == null)
            {
                Debug.LogError($"{name}: _intrusiveView is not wired — assign the IntrusivePopupView component (Layout A) in the Inspector.", this);
                ok = false;
            }
            if (_sideBubble == null)
            {
                Debug.LogError($"{name}: _sideBubble is not wired — assign the SidePopupView component (Layout B) in the Inspector.", this);
                ok = false;
            }
            // _registry is soft-optional here — it's validated at resolve-time.

            if (!ok) { enabled = false; return; }

            _intrusiveView.Hide();
            _sideBubble.Hide();
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Show a popup described by <paramref name="request"/> and return a handle.
        /// Called by UIManager.ShowPopup (hub routes all callers through here).
        /// Returns <see cref="PopupHandle.None"/> and logs an error when
        /// <c>request.lines</c> is null or empty.
        /// </summary>
        public PopupHandle Show(in PopupRequest request)
        {
            if (!enabled) return PopupHandle.None;

            if (request.lines == null || request.lines.Count == 0)
            {
                Debug.LogError($"{name}: PopupRequest.lines is null or empty — cannot show a popup with no content.", this);
                return PopupHandle.None;
            }

            var handle = new PopupHandle(_nextId++, request.intrusiveness);

            if (request.intrusiveness == PopupIntrusiveness.Intrusive)
                StartIntrusive(request, handle);
            else
                StartSide(request, handle);

            return handle;
        }

        /// <summary>
        /// Convenience overload: resolves a <see cref="PopupSO"/> asset into a
        /// <see cref="PopupRequest"/> (looking up Azi/Bob identity from
        /// <see cref="_registry"/>) then calls <see cref="Show(in PopupRequest)"/>.
        ///
        /// <para>
        /// Portrait resolution order per line:
        /// <list type="number">
        ///   <item><c>PopupLine.portraitOverride</c> (always wins if non-null)</item>
        ///   <item>Profile portrait from <c>DialogueRegistry</c> for Azi/Bob</item>
        ///   <item>null for Custom speaker</item>
        /// </list>
        /// </para>
        /// </summary>
        public PopupHandle Show(PopupSO seq, Action onConfirm = null)
        {
            if (!enabled) return PopupHandle.None;

            if (seq == null)
            {
                Debug.LogError($"{name}: PopupSO argument is null.", this);
                return PopupHandle.None;
            }

            if (seq.lines == null || seq.lines.Count == 0)
            {
                Debug.LogError($"{name}: PopupSO '{seq.name}' has no lines — cannot show.", this);
                return PopupHandle.None;
            }

            // Warn about missing registry but fall back gracefully (Law 3 + spec §5).
            if (_registry == null)
            {
                Debug.LogError($"{name}: _registry is not wired — Azi/Bob names will fall back to literals and portraits will be null. " +
                               "Assign a DialogueRegistry in the Inspector.", this);
            }

            // S2: resolution lives in PopupSO.ResolveLines — delegate, do not duplicate.
            var resolved = seq.ResolveLines(_registry);

            var request = new PopupRequest
            {
                intrusiveness      = seq.intrusiveness,
                lines              = resolved,
                confirmLabel       = "OK",
                onConfirm          = onConfirm,
                autoDismissSeconds = 0f,
                anchor             = ScreenAnchor.BottomLeft
            };

            return Show(in request);
        }

        /// <summary>
        /// Dismiss a specific popup by handle. Safe to call with
        /// <see cref="PopupHandle.None"/> or an already-dismissed handle.
        /// </summary>
        public void Dismiss(PopupHandle handle)
        {
            if (!handle.IsValid) return;

            if (handle.intrusiveness == PopupIntrusiveness.Intrusive)
            {
                if (_activeIntrusive.id == handle.id)
                    DismissIntrusive(handle, fireCallback: false);
            }
            else
            {
                if (_activeSide.Contains(handle))
                    DismissSide(handle, fireCallback: false);
            }
        }

        // ─── Intrusive path ───────────────────────────────────────────────────

        private void StartIntrusive(in PopupRequest req, PopupHandle handle)
        {
            // Only one intrusive at a time — dismiss any active one silently.
            if (_activeIntrusive.IsValid)
                DismissIntrusive(_activeIntrusive, fireCallback: false);

            _activeIntrusive        = handle;
            _intrusiveLines.Clear();
            _intrusiveLines.AddRange(req.lines);
            _intrusiveIndex         = 0;
            _intrusiveConfirmLabel  = string.IsNullOrEmpty(req.confirmLabel) ? "OK" : req.confirmLabel;
            _intrusiveOnConfirm     = req.onConfirm;
            _intrusivePendingHandle = handle;

            RenderIntrusiveCurrent();
        }

        private void RenderIntrusiveCurrent()
        {
            ResolvedLine line = _intrusiveLines[_intrusiveIndex];
            bool isLast = _intrusiveIndex >= _intrusiveLines.Count - 1;
            string btnLabel = isLast ? _intrusiveConfirmLabel : NextWord;

            _intrusiveView.Show(
                line:         line,
                confirmLabel: btnLabel,
                onConfirm:    OnIntrusiveButtonPressed
            );
        }

        private void OnIntrusiveButtonPressed()
        {
            _intrusiveIndex++;
            if (_intrusiveIndex < _intrusiveLines.Count)
            {
                RenderIntrusiveCurrent();
            }
            else
            {
                // All lines shown — fire callback then dismiss.
                Action cb = _intrusiveOnConfirm;
                _intrusiveOnConfirm = null;
                cb?.Invoke();
                DismissIntrusive(_intrusivePendingHandle, fireCallback: false);
            }
        }

        private void DismissIntrusive(PopupHandle handle, bool fireCallback)
        {
            if (_activeIntrusive.id != handle.id) return;

            _intrusiveView.Hide();
            _activeIntrusive = PopupHandle.None;

            if (fireCallback)
            {
                Action cb = _intrusiveOnConfirm;
                _intrusiveOnConfirm = null;
                cb?.Invoke();
            }

            OnPopupDismissed?.Invoke(handle, PopupIntrusiveness.Intrusive);
        }

        // ─── Non-intrusive (side bubble) path ─────────────────────────────────

        private void StartSide(in PopupRequest req, PopupHandle handle)
        {
            // Stop any current side sequence.
            StopSideAuto();

            _activeSide.Add(handle);
            _sideLines.Clear();
            _sideLines.AddRange(req.lines);
            _sideIndex          = 0;
            _sideAutoDismissSecs = req.autoDismissSeconds;
            _sideOnConfirm      = req.onConfirm;
            _sidePendingHandle  = handle;

            RenderSideCurrent(req.anchor);
        }

        private void RenderSideCurrent(ScreenAnchor anchor = ScreenAnchor.BottomLeft)
        {
            ResolvedLine line = _sideLines[_sideIndex];

            _sideBubble.Show(
                line:      line,
                anchor:    anchor,
                onTap:     OnSideTapped
            );

            if (_sideAutoDismissSecs > 0f)
                _sideAutoCoroutine = StartCoroutine(SideAutoAdvance(_sideAutoDismissSecs));
        }

        // Tap always advances immediately (cancelling any pending auto-advance).
        private void OnSideTapped()
        {
            StopSideAuto();
            AdvanceSide();
        }

        private void AdvanceSide()
        {
            _sideIndex++;
            if (_sideIndex < _sideLines.Count)
            {
                RenderSideCurrent();
            }
            else
            {
                // All lines shown.
                Action cb = _sideOnConfirm;
                _sideOnConfirm = null;
                cb?.Invoke();
                DismissSide(_sidePendingHandle, fireCallback: false);
            }
        }

        private IEnumerator SideAutoAdvance(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            _sideAutoCoroutine = null;
            AdvanceSide();
        }

        private void StopSideAuto()
        {
            if (_sideAutoCoroutine != null)
            {
                StopCoroutine(_sideAutoCoroutine);
                _sideAutoCoroutine = null;
            }
        }

        private void DismissSide(PopupHandle handle, bool fireCallback)
        {
            if (!_activeSide.Contains(handle)) return;
            _activeSide.Remove(handle);

            StopSideAuto();
            _sideBubble.Hide();

            if (fireCallback)
            {
                Action cb = _sideOnConfirm;
                _sideOnConfirm = null;
                cb?.Invoke();
            }

            OnPopupDismissed?.Invoke(handle, PopupIntrusiveness.NonIntrusive);
        }
    }

}

// ─────────────────────────────────────────────────────────────────────────────
// HUMAN WIRING STEPS (Inspector only, no code edits needed)
// ─────────────────────────────────────────────────────────────────────────────
//
// ── WO-1 BREAKING CHANGE — collapsed intrusive view ──────────────────────────
//
// The old PopupController had TWO intrusive-view slots:
//   _intrusivePortrait    (Layout A — modal card with portrait box)
//   _intrusiveNoPortrait  (Layout B — modal card without portrait box)
// These are NOW replaced by a SINGLE slot:
//   _intrusiveView        (Layout A — portrait box toggled per-line at runtime)
//
// Scene wiring to update on PopupController:
//   1. Remove the old _intrusivePortrait and _intrusiveNoPortrait references.
//   2. Wire the SINGLE IntrusivePopupView component into _intrusiveView.
//      (You can keep either of the old prefab variants; just ensure _portraitBox
//       is wired on IntrusivePopupView so it can toggle per line.)
//   3. Assign a DialogueRegistry asset to _registry (any scene instance that
//      already has the Azi/Bob CharacterProfileSOs assigned will do).
//
// ── COMPLETE WIRING GUIDE ─────────────────────────────────────────────────────
//
// 1. POPUP PREFAB VARIANTS
//    Build two GameObjects (one modal card, one side bubble).
//
//    Layout A — Intrusive Modal Card
//      GameObject "PopupCard_Intrusive"
//        ├─ Image "OverlayBlocker"       (full-screen, dark semi-transparent)
//        └─ GameObject "Card"
//             ├─ GameObject "PortraitBox"   ← toggled per-line by portrait != null
//             │    ├─ Image "Portrait"
//             │    └─ TextMeshPro "SpeakerLabel"
//             ├─ TextMeshPro "BodyText"
//             └─ Button "ConfirmButton"
//                  └─ TextMeshPro "ConfirmLabel"
//      Add component: IntrusivePopupView. Wire every field.
//
//    Layout B — Side Bubble
//      GameObject "SideBubble"
//        ├─ RectTransform "Root"
//        │    ├─ CanvasGroup            (optional — fade-in juice)
//        │    ├─ GameObject "PortraitBox"  ← toggled per-line
//        │    │    ├─ Image "Portrait"
//        │    │    └─ TextMeshPro "SpeakerLabel"
//        │    ├─ TextMeshPro "BodyText"
//        │    └─ Button "TapTarget"
//      Add component: SidePopupView. Wire every field.
//
// 2. POPUP CONTROLLER SLOTS (on the PopupController component in the scene)
//      _root          ← parent GameObject holding both layouts
//      _intrusiveView ← drag the IntrusivePopupView from Layout A
//      _sideBubble    ← drag the SidePopupView from Layout B
//      _registry      ← drag the DialogueRegistry ScriptableObject asset
//
// 3. UI MANAGER REGISTRATION
//      subsystems list ← add the PopupController MonoBehaviour
//      popups slot     ← drag the same PopupController component
//
// 4. AUTHOR A TEST PopupSO (Assets ▶ Create ▶ Habitales ▶ Popup)
//    Suggested three-line test covering Azi / Bob / Custom:
//      Line 0: speaker=Azi,    body="Hey! Ready to check on the field?"
//      Line 1: speaker=Bob,    body="I ran the numbers. Soil composite is at 72."
//      Line 2: speaker=Custom, customName="Field Log", body="Survey complete."
//    Set intrusiveness = Intrusive (or NonIntrusive to test the side path).
//
// 5. DIALOGUE REGISTRY SETUP (if not already done)
//    - Create a CharacterProfileSO for Azi  (Assets ▶ Create ▶ Habitales ▶ Dialogue ▶ Character Profile).
//      Set character=Azi, displayName="Azi", assign portrait sprite.
//    - Create a CharacterProfileSO for Bob.
//      Set character=Bob, displayName="Bob", assign portrait sprite.
//    - Open the DialogueRegistry asset. Drag the two profiles to aziProfile / bobProfile.
//
// ─────────────────────────────────────────────────────────────────────────────
