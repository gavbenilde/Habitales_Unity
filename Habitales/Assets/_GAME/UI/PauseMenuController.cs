using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// PauseMenuController — U4.5 In-game pause overlay (UI Architecture §5.6).
// Opened by Escape (or a dedicated pause button). Hosts Resume / Settings / Gallery /
// Exit-to-Menu / Exit-to-Desktop. Gallery is surfaced here as a panel, not a top-level
// surface — callers should NOT open GalleryController directly; use OpenGallery() instead.
//
// WIRING (human, Inspector) — see detailed steps at the bottom of this file.

namespace Habitales.UI
{
    /// <summary>
    /// In-run pause overlay subsystem. Registered in UIManager's subsystems list as any
    /// other <see cref="IUISubsystem"/>. No typed fast-path on UIManager is required;
    /// the hub drives it through the generic registry.
    /// </summary>
    public class PauseMenuController : MonoBehaviour, IUISubsystem
    {
        // ─── IUISubsystem ─────────────────────────────────────────────────────

        public string SubsystemId => "pausemenu";

        public bool IsVisible
        {
            get
            {
                if (_overlayRoot == null) return false;
                if (_overlayCanvas != null) return _overlayCanvas.enabled;
                return _overlayRoot.gameObject.activeSelf;
            }
        }

        /// <summary>
        /// Shows or hides the entire pause overlay (used by the hub's master-visibility
        /// sweep). For gameplay toggle, call <see cref="Open"/> / <see cref="Resume"/>.
        /// </summary>
        public void SetVisible(bool visible)
        {
            SetOverlayShown(visible);
        }

        // ─── Serialized refs (Law 3 — loud-fail) ──────────────────────────────

        [Header("Overlay Root")]
        [Tooltip("Root RectTransform (or GameObject) of the full pause overlay. " +
                 "SetVisible / Open / Resume toggle its active state.")]
        [SerializeField] private RectTransform _overlayRoot;

        [Header("Pause-menu Buttons")]
        [Tooltip("Button that resumes gameplay and closes the pause overlay.")]
        [SerializeField] private Button _resumeButton;

        [Tooltip("Button that opens the settings stub panel.")]
        [SerializeField] private Button _settingsButton;

        [Tooltip("Button that opens the screenshot gallery panel.")]
        [SerializeField] private Button _galleryButton;

        [Tooltip("Button that exits to the main-menu scene.")]
        [SerializeField] private Button _exitToMenuButton;

        [Tooltip("Button that quits the application (no-op in the editor).")]
        [SerializeField] private Button _exitToDesktopButton;

        [Header("Sub-panels")]
        [Tooltip("Settings stub panel (child of the overlay). Shown by OpenSettings(); " +
                 "hide it to return to the button row. Add a Back button inside it that " +
                 "calls CloseSubPanels() (or wire to the same GameObject's SetActive(false)).")]
        [SerializeField] private GameObject _settingsPanel;

        [Tooltip("GalleryController reference. Must be wired; fall-back lookup is intentionally " +
                 "NOT provided (Law 3). Opened as a panel via OpenGallery().")]
        [SerializeField] private GalleryController _gallery;

        [Header("Pause Behaviour")]
        [Tooltip("Keyboard key used to toggle the pause overlay open/closed.")]
        [SerializeField] private KeyCode toggleKey = KeyCode.Escape;

        [Tooltip("If true, sets Time.timeScale = 0 while the pause overlay is open so cosmetic " +
                 "tweens / camera moves freeze. Menus that use unscaled time (e.g. DOTween " +
                 "SetUpdate(true)) are unaffected. Set to false if any in-pause animation breaks.")]
        [SerializeField] private bool freezeTimeScaleWhileOpen = true;

        // ─── Private state ────────────────────────────────────────────────────

        // Time.timeScale value cached before we set it to 0, so we can restore it on Resume.
        private float _cachedTimeScale = 1f;

        // When the overlay root is (or contains) this controller's own GameObject,
        // SetActive(false) would disable this component and Update() would stop polling
        // the toggle key. In that case we hide by disabling the root's Canvas (and
        // GraphicRaycaster) instead, keeping the GameObject — and this script — alive.
        private Canvas _overlayCanvas;
        private UnityEngine.UI.GraphicRaycaster _overlayRaycaster;

        // ─── Lifecycle ────────────────────────────────────────────────────────

        private void Awake()
        {
            ValidateRefs();
            HookButtons();

            if (_overlayRoot != null &&
                (transform == _overlayRoot || transform.IsChildOf(_overlayRoot)))
            {
                _overlayCanvas = _overlayRoot.GetComponent<Canvas>();
                _overlayRaycaster = _overlayRoot.GetComponent<UnityEngine.UI.GraphicRaycaster>();

                if (_overlayCanvas == null)
                {
                    Debug.LogError($"{name}: _overlayRoot is this controller's own GameObject " +
                                   "(or an ancestor of it) but has no Canvas component to toggle. " +
                                   "Either add a Canvas to the overlay root, or move " +
                                   "PauseMenuController outside the overlay root.", this);
                    enabled = false;
                    return;
                }
            }

            // Overlay starts hidden.
            SetOverlayShown(false);

            // Sub-panels start hidden.
            HideSubPanels();
        }

        private void Update()
        {
            // Toggle open/close on the configured key (Escape by default).
            if (Input.GetKeyDown(toggleKey))
            {
                if (IsVisible)
                    Resume();
                else
                    Open();
            }
        }

        // ─── Validation (Law 3) ───────────────────────────────────────────────

        private void ValidateRefs()
        {
            bool ok = true;

            if (_overlayRoot == null)
            {
                Debug.LogError($"{name}: _overlayRoot is not wired — " +
                               "assign the pause overlay root RectTransform in the Inspector.", this);
                ok = false;
            }
            if (_resumeButton == null)
            {
                Debug.LogError($"{name}: _resumeButton is not wired — " +
                               "assign the Resume Button in the Inspector.", this);
                ok = false;
            }
            if (_settingsButton == null)
            {
                Debug.LogError($"{name}: _settingsButton is not wired — " +
                               "assign the Settings Button in the Inspector.", this);
                ok = false;
            }
            // if (_galleryButton == null)
            // {
            //     Debug.LogError($"{name}: _galleryButton is not wired — " +
            //                    "assign the Gallery Button in the Inspector.", this);
            //     ok = false;
            // }
            if (_exitToMenuButton == null)
            {
                Debug.LogError($"{name}: _exitToMenuButton is not wired — " +
                               "assign the Exit-to-Menu Button in the Inspector.", this);
                ok = false;
            }
            if (_exitToDesktopButton == null)
            {
                Debug.LogError($"{name}: _exitToDesktopButton is not wired — " +
                               "assign the Exit-to-Desktop Button in the Inspector.", this);
                ok = false;
            }
            if (_settingsPanel == null)
            {
                Debug.LogError($"{name}: _settingsPanel is not wired — " +
                               "assign the settings stub panel GameObject in the Inspector.", this);
                ok = false;
            }
            // if (_gallery == null)
            // {
            //     Debug.LogError($"{name}: _gallery is not wired — " +
            //                    "drag the GalleryController component into this field in the Inspector. " +
            //                    "A UIManager lookup fallback is NOT provided by design (Law 3).", this);
            //     ok = false;
            // }

            if (!ok) enabled = false;
        }

        // ─── Button wiring (Awake) ────────────────────────────────────────────

        private void HookButtons()
        {
            // Guard: if refs are missing, ValidateRefs already logged; skip to avoid NPE.
            _resumeButton?.onClick.AddListener(Resume);
            _settingsButton?.onClick.AddListener(OpenSettings);
            // _galleryButton?.onClick.AddListener(OpenGallery);
            _exitToMenuButton?.onClick.AddListener(ExitToMainMenu);
            _exitToDesktopButton?.onClick.AddListener(ExitToDesktop);
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Opens the pause overlay. Sets <see cref="UIMode.Paused"/>, blocks action-bar input,
        /// and optionally freezes <c>Time.timeScale</c>. Sub-panels start hidden.
        /// </summary>
        public void Open()
        {
            if (_overlayRoot == null) return;

            // Show main button row; hide sub-panels so we start clean.
            SetOverlayShown(true);
            HideSubPanels();

            // Tell the hub we are in Paused mode.
            UIManager.Instance.SetMode(UIMode.Paused);

            // Block gameplay input through the action bar.
            UIManager.Instance.ActionBar?.SetInteractable(false);

            // Optionally freeze cosmetic time.
            if (freezeTimeScaleWhileOpen)
            {
                _cachedTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }
        }

        /// <summary>
        /// Closes the pause overlay. Restores <c>Time.timeScale</c>, unblocks action-bar input,
        /// and returns the UI mode to <see cref="UIMode.Gameplay"/>.
        /// </summary>
        public void Resume()
        {
            HideSubPanels();
            SetOverlayShown(false);

            // Restore timescale before anything else to avoid a zero-scale frame.
            if (freezeTimeScaleWhileOpen)
                Time.timeScale = _cachedTimeScale;

            // Unblock gameplay input.
            UIManager.Instance.ActionBar?.SetInteractable(true);

            // Return to normal gameplay mode.
            UIManager.Instance.SetMode(UIMode.Gameplay);
        }

        /// <summary>
        /// Hides the button row panels and shows the screenshot gallery via
        /// <see cref="GalleryController.SetVisible"/>. GalleryController.SetVisible(true)
        /// automatically calls Refresh() so newly-taken screenshots appear immediately.
        ///
        /// <para>This is the canonical entry point for the gallery. Do NOT open
        /// <see cref="GalleryController"/> directly from other systems.</para>
        /// </summary>
        // public void OpenGallery()
        // {
        //     if (_gallery == null) return;
        //
        //     // Hide settings sub-panel if open.
        //     if (_settingsPanel != null)
        //         _settingsPanel.SetActive(false);
        //
        //     // SetVisible(true) on GalleryController calls Refresh() internally (§5.5).
        //     _gallery.SetVisible(true);
        // }

        /// <summary>
        /// Closes the gallery sub-panel and returns focus to the main button row.
        /// Wire a "Back" button inside the gallery panel to this method, or call it
        /// from Resume() (which hides everything).
        /// </summary>
        // public void CloseGallery()
        // {
        //     _gallery?.SetVisible(false);
        // }

        /// <summary>
        /// Shows the settings stub panel. Audio sliders and other content go here in a
        /// future order; for now it is an empty panel with a Back button.
        /// </summary>
        public void OpenSettings()
        {
            // Hide gallery sub-panel if open.
            // _gallery?.SetVisible(false);

            if (_settingsPanel != null)
                _settingsPanel.SetActive(true);

            // TODO: settings content — wire audio sliders, graphics toggles, etc. here.
        }

        /// <summary>Closes the settings sub-panel and returns to the button row.</summary>
        public void CloseSettings()
        {
            if (_settingsPanel != null)
                _settingsPanel.SetActive(false);
        }

        /// <summary>
        /// Restores <c>Time.timeScale</c> to 1 and loads the main-menu scene.
        /// Uses the same <c>SceneManager.LoadScene</c> call as <c>EndGameScreenUI</c> and
        /// <c>MainMenu</c> — does NOT reference the <c>MainMenu</c> class.
        /// </summary>
        public void ExitToMainMenu()
        {
            // Always restore timescale before scene load to avoid zero-scale in the new scene.
            Time.timeScale = 1f;
            SceneManager.LoadScene("Main Menu");
        }

        /// <summary>
        /// Restarts the current run in place (same scene, from the top). Mirrors
        /// <see cref="ExitToMainMenu"/>'s teardown — closes the pause overlay and any sub-panels
        /// so nothing from this menu survives into the reloaded scene — then hands off to
        /// <see cref="Habitales.Core.RunRestart.RestartCurrentRun"/>, which resets the handful of
        /// cross-scene-surviving statics, restores <c>Time.timeScale</c>, cancels all LeanTween
        /// tweens, and reloads the active scene. Wireable to a future "Restart" button; nothing
        /// calls this yet.
        /// </summary>
        public void RestartRun()
        {
            // Tear down the overlay the same way Resume() does, minus the timescale restore —
            // RunRestart.RestartCurrentRun() sets Time.timeScale = 1f itself, and the scene
            // reload is about to destroy this GameObject anyway.
            HideSubPanels();
            SetOverlayShown(false);

            Habitales.Core.RunRestart.RestartCurrentRun();
        }

        /// <summary>
        /// Quits the application. No-op inside the Unity Editor (by design).
        /// </summary>
        public void ExitToDesktop()
        {
            Application.Quit();
        }

        // ─── Private helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Shows/hides the overlay. Uses Canvas + GraphicRaycaster enabling when the
        /// controller lives on (or under) the overlay root — SetActive(false) there would
        /// disable this component and Escape would stop working. Falls back to
        /// GameObject.SetActive when the controller is outside the overlay root.
        /// </summary>
        private void SetOverlayShown(bool shown)
        {
            if (_overlayRoot == null) return;

            if (_overlayCanvas != null)
            {
                _overlayCanvas.enabled = shown;
                if (_overlayRaycaster != null)
                    _overlayRaycaster.enabled = shown;
            }
            else
            {
                _overlayRoot.gameObject.SetActive(shown);
            }
        }

        private void HideSubPanels()
        {
            if (_settingsPanel != null)
                _settingsPanel.SetActive(false);

            // _gallery?.SetVisible(false);
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// WIRING STEPS (human, Inspector) — U4.5 Pause Menu
// ═══════════════════════════════════════════════════════════════════════════
//
// A. BUILD THE PAUSE OVERLAY PREFAB / HIERARCHY
// ──────────────────────────────────────────────
//  1. Under the UIManager Canvas, create a new GameObject "PauseMenuOverlay".
//       • Add a full-screen RectTransform (Anchor: stretch-stretch, offsets 0).
//       • Optionally add a semi-transparent background Image.
//
//  2. Add PauseMenuController component to "PauseMenuOverlay".
//
//  3. Inside "PauseMenuOverlay" add a button row panel (e.g. "ButtonRow") with
//     FIVE Button children:
//       • "ResumeButton"        — label: Resume
//       • "SettingsButton"      — label: Settings
//       • "GalleryButton"       — label: Gallery
//       • "ExitToMenuButton"    — label: Exit to Main Menu
//       • "ExitToDesktopButton" — label: Exit to Desktop
//     (Do NOT wire their onClick in the Inspector — Awake() does it in code.)
//
//  4. Inside "PauseMenuOverlay" add a stub settings panel (e.g. "SettingsPanel"):
//       • Starts inactive. Add a "Back" button; wire its onClick to
//         PauseMenuController.CloseSettings() via the Inspector (or via a
//         UnityEvent on the panel).
//       • Add a TODO comment / placeholder text for future audio sliders.
//
//  5. The GalleryController panel (built in U4) lives ELSEWHERE in the hierarchy
//     (typically a sibling of PauseMenuOverlay under the UIManager Canvas) and is
//     already registered as an IUISubsystem. Do NOT nest it inside PauseMenuOverlay.
//
// B. WIRE SERIALIZED SLOTS ON PauseMenuController
// ─────────────────────────────────────────────────
//  In the Inspector on the PauseMenuOverlay GameObject:
//
//   _overlayRoot         → drag "PauseMenuOverlay" RectTransform (self-ref is fine —
//                          the overlay root then needs a Canvas component, which is
//                          toggled instead of SetActive so this script keeps running)
//   _resumeButton        → drag "ResumeButton" Button component
//   _settingsButton      → drag "SettingsButton" Button component
//   _galleryButton       → drag "GalleryButton" Button component
//   _exitToMenuButton    → drag "ExitToMenuButton" Button component
//   _exitToDesktopButton → drag "ExitToDesktopButton" Button component
//   _settingsPanel       → drag "SettingsPanel" GameObject
//   _gallery             → drag the GalleryController component from the scene
//
//   toggleKey            — defaults to Escape; change if needed
//   freezeTimeScaleWhileOpen — defaults to true; set false if pause-menu animations break
//
// C. REGISTER WITH UIManager
// ───────────────────────────
//  On the UIManager GameObject, in the `subsystems` list, add a new entry and drag
//  in the PauseMenuController component (or its GameObject). No typed fast-path field
//  is required on UIManager for PauseMenuController.
//
// D. IMPORTANT — GALLERY ACCESS POLICY
// ──────────────────────────────────────
//  GalleryController should NO LONGER be opened directly from buttons, hotkeys, or
//  other code. It is surfaced exclusively via PauseMenuController.OpenGallery().
//  Any previous direct bindings to GalleryController.SetVisible(true) should be
//  removed or redirected through the pause menu.
//
// E. OPTIONAL — PAUSE BUTTON IN HUD
// ────────────────────────────────────
//  If a HUD pause button is desired, wire its onClick to
//  PauseMenuController.Open() (obtain the ref via the subsystems list, or
//  expose a serialized PauseMenuController ref on HudController).
