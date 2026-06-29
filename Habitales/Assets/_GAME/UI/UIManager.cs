using System;
using System.Collections.Generic;
using UnityEngine;
using Habitales.UI.Actions;   // ActionBarUI

// UIManager — the UI-domain owner (UI Architecture §2).
// One hub for every subsystem; other game systems call in here, never directly
// into a subsystem controller. Singleton; [DefaultExecutionOrder(-500)] puts it
// after core managers (-1000) but before all normal MonoBehaviours.
//
// WIRING (human, Inspector):
//   1. Add UIManager component to a dedicated "UIManager" GameObject in the scene.
//   2. Drag every IUISubsystem MonoBehaviour into the `subsystems` list.
//   3. Drag the typed fast-path refs (actionBar, popups, tooltip, hud, gallery).
//   See the work-order wiring steps at the bottom of the file.

namespace Habitales.UI
{
    /// <summary>UI-wide mode. SetMode fires OnModeChanged when the mode actually changes.</summary>
    public enum UIMode { Boot, Menu, Gameplay, ModalPopup, Paused }

    [DefaultExecutionOrder(-500)]
    public class UIManager : MonoBehaviour
    {
        // ─── Singleton ────────────────────────────────────────────────────────

        public static UIManager Instance { get; private set; }

        // ─── Subsystem registry ───────────────────────────────────────────────

        /// <summary>
        /// Every subsystem root. Each entry must implement <see cref="IUISubsystem"/> —
        /// validated in Awake (Law 3).
        /// </summary>
        [SerializeField] private List<MonoBehaviour> subsystems = new();

        // Typed fast-paths for subsystems with rich public APIs.
        // Sibling controllers (PopupController, TooltipController, HudController, GalleryController)
        // are created in parallel wave U1–U4; references are expected to compile only after the wave merges.
        [SerializeField] private ActionBarUI        actionBar;
        [SerializeField] private PopupController    popups;
        [SerializeField] private TooltipController  tooltip;
        [SerializeField] private HudController      hud;
        [SerializeField] private GalleryController  gallery;

        // Law-1 getters — typed subsystem fast-paths.
        public ActionBarUI       ActionBar => actionBar;
        public PopupController   Popups    => popups;
        public TooltipController Tooltip   => tooltip;
        public HudController     Hud       => hud;

        // ─── Read-anywhere state (Law 1) ──────────────────────────────────────

        /// <summary>Current UI-wide mode. Read anywhere; write only via SetMode.</summary>
        public UIMode CurrentMode    { get; private set; }

        /// <summary>True while an intrusive (modal) popup is displayed.</summary>
        public bool   IsModalActive  { get; private set; }

        /// <summary>True while SetAllUIVisible(false) is in effect (screenshot hide-all).</summary>
        public bool   IsUIHidden     { get; private set; }

        // ─── Meaning-events (Law 2) ───────────────────────────────────────────

        /// <summary>Fired when CurrentMode changes to a new value.</summary>
        public event Action<UIMode> OnModeChanged;

        // ─── Visibility snapshot (for SetAllUIVisible / RestoreUIVisibility) ──

        // Key: subsystem index in the `subsystems` list; Value: IsVisible at snapshot time.
        private readonly Dictionary<int, bool> _visibilitySnapshot = new();

        // ─── Lifecycle ────────────────────────────────────────────────────────

        void Awake()
        {
            // Singleton guard — loud-fail on a second instance (Law 3).
            if (Instance != null && Instance != this)
            {
                Debug.LogError($"{name}: A second UIManager instance was created. " +
                               "Only one UIManager is allowed in the scene. Destroying this duplicate.", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;

            ValidateSubsystems();
            ValidateTypedRefs();
        }

        void OnEnable()
        {
            // Subscribe to popup-dismissed event so intrusive dismiss can restore game state.
            if (popups != null)
                popups.OnPopupDismissed += HandlePopupDismissed;
        }

        void OnDisable()
        {
            if (popups != null)
                popups.OnPopupDismissed -= HandlePopupDismissed;
        }

        // ─── Validation helpers (Law 3) ───────────────────────────────────────

        private void ValidateSubsystems()
        {
            for (int i = 0; i < subsystems.Count; i++)
            {
                var entry = subsystems[i];
                if (entry == null)
                {
                    Debug.LogError($"{name}: subsystems[{i}] is null — wire a MonoBehaviour that implements IUISubsystem.", this);
                    continue;
                }
                if (entry is not IUISubsystem)
                {
                    Debug.LogError($"{name}: subsystems[{i}] ({entry.name} / {entry.GetType().Name}) does not implement IUISubsystem. " +
                                   "Remove it or make the component implement the interface.", this);
                }
            }
        }

        private void ValidateTypedRefs()
        {
            // Every typed ref is critical — UIManager cannot function without them.
            bool ok = true;

            if (actionBar == null)
            {
                Debug.LogError($"{name}: actionBar is not wired — assign the ActionBarUI component in the Inspector.", this);
                ok = false;
            }
            if (popups == null)
            {
                Debug.LogError($"{name}: popups is not wired — assign the PopupController component in the Inspector.", this);
                ok = false;
            }
            if (tooltip == null)
            {
                Debug.LogError($"{name}: tooltip is not wired — assign the TooltipController component in the Inspector.", this);
                ok = false;
            }
            if (hud == null)
            {
                Debug.LogError($"{name}: hud is not wired — assign the HudController component in the Inspector.", this);
                ok = false;
            }
            if (gallery == null)
            {
                Debug.LogError($"{name}: gallery is not wired — assign the GalleryController component in the Inspector.", this);
                ok = false;
            }

            if (!ok) enabled = false;
        }

        // ─── Mode ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Transitions to <paramref name="mode"/>. Fires <see cref="OnModeChanged"/> only when
        /// the mode actually changes (Law 2 — events fire on meaning, not on field mutation).
        /// </summary>
        public void SetMode(UIMode mode)
        {
            if (CurrentMode == mode) return;
            CurrentMode = mode;
            OnModeChanged?.Invoke(mode);
        }

        // ─── Popup routing ────────────────────────────────────────────────────

        /// <summary>
        /// Routes a popup request through the hub. If intrusive: sets modal state, blocks the
        /// action bar, pauses the sim, then delegates to PopupController. Non-intrusive requests
        /// are passed straight through without touching game state.
        /// </summary>
        public PopupHandle ShowPopup(in PopupRequest request)
        {
            if (request.intrusiveness == PopupIntrusiveness.Intrusive)
            {
                IsModalActive = true;
                actionBar?.SetInteractable(false);
                RunManager.Instance.PauseForEvent();
            }

            return popups.Show(request);
        }

        /// <summary>
        /// Called by PopupController's OnPopupDismissed event. Clears modal state for intrusive
        /// dismissals and resumes the sim.
        /// </summary>
        private void HandlePopupDismissed(PopupHandle handle, PopupIntrusiveness intrusiveness)
        {
            if (intrusiveness == PopupIntrusiveness.Intrusive)
            {
                IsModalActive = false;
                actionBar?.SetInteractable(true);
                RunManager.Instance.ResumeFromEvent();
            }
        }

        // ─── Tooltip routing ──────────────────────────────────────────────────

        /// <summary>Delegates to TooltipController. Position math lives only there (S2).</summary>
        public void ShowTooltip(RectTransform target, string text,
                                TooltipAnchor anchor = TooltipAnchor.Top)
            => tooltip?.Show(target, text, anchor);

        /// <summary>Hides the shared tooltip.</summary>
        public void HideTooltip()
            => tooltip?.Hide();

        // ─── Master visibility ────────────────────────────────────────────────

        /// <summary>
        /// Snapshots each registered subsystem's <see cref="IUISubsystem.IsVisible"/>, then
        /// sets them all to <paramref name="visible"/> — except any listed in
        /// <paramref name="except"/>. Sets <see cref="IsUIHidden"/> when hiding.
        /// Use <see cref="RestoreUIVisibility"/> to re-apply the snapshot.
        /// </summary>
        public void SetAllUIVisible(bool visible, params IUISubsystem[] except)
        {
            _visibilitySnapshot.Clear();

            var exceptSet = new HashSet<IUISubsystem>(except);

            for (int i = 0; i < subsystems.Count; i++)
            {
                if (subsystems[i] is not IUISubsystem sub) continue;

                _visibilitySnapshot[i] = sub.IsVisible;

                if (!exceptSet.Contains(sub))
                    sub.SetVisible(visible);
            }

            IsUIHidden = !visible;
        }

        /// <summary>
        /// Re-applies the visibility snapshot taken by the most recent
        /// <see cref="SetAllUIVisible"/> call.
        /// </summary>
        public void RestoreUIVisibility()
        {
            for (int i = 0; i < subsystems.Count; i++)
            {
                if (subsystems[i] is not IUISubsystem sub) continue;
                if (_visibilitySnapshot.TryGetValue(i, out bool wasVisible))
                    sub.SetVisible(wasVisible);
            }

            IsUIHidden = false;
        }
    }
}
