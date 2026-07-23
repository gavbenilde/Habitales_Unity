using UnityEngine;
using UnityEngine.UI;

namespace Habitales.UI
{
    /// <summary>
    /// Owns the "Selected Info Panel" (added 2026-07-22): ONE shared sliding panel plus TWO
    /// left-side buttons (Region / Tile) that show the health + 6 substats of the currently
    /// selected tile OR its parent region. The <see cref="RegionHealthUI"/> header and the
    /// <see cref="InspectPanelUI"/> body stay SEPARATE mode-aware scripts parented under the
    /// shared panel root — this controller drives the mode state machine, the LeanTween slide,
    /// the header colour per mode, and button visibility.
    ///
    /// panelMode: 0 = closed, 1 = Region, 2 = Tile.
    ///
    /// SELECTION GATES BUTTONS (Law 2 — react on meaning): subscribes TileSelector. Nothing
    /// selected → the panel is forced closed and both buttons tween out; a tile selected → the
    /// buttons tween in. Every button handler no-ops without a current selection (defensive
    /// against an off-screen deselect landing before a button press).
    ///
    /// BUTTONS TOGGLE PANEL:
    ///   • pressed while closed → tween the panel in at that mode (0 → 1 or 0 → 2);
    ///   • the OTHER mode's button while open → swap content + header colour, NO tween (1 ↔ 2);
    ///   • the CURRENT mode's button while open → tween the panel out (→ 0).
    ///
    /// WIRING: place this on a PERSISTENT GameObject (e.g. the parent canvas), NOT on the moving
    /// panel root — it subscribes TileSelector in Awake / RunManager in Start so it keeps working
    /// regardless of the panel being tweened off-screen.
    /// </summary>
    public class SelectedInfoPanelController : MonoBehaviour
    {
        [Header("Shared Panel Root (the moving RectTransform)")]
        [SerializeField] private RectTransform panelRoot;
        [Tooltip("anchoredPosition when the panel is on-screen (open).")]
        [SerializeField] private Vector2 panelShownPosition;
        [Tooltip("anchoredPosition when the panel is off-screen (closed).")]
        [SerializeField] private Vector2 panelHiddenPosition;

        [Header("Left-Side Buttons container (the moving RectTransform)")]
        [SerializeField] private RectTransform buttonsRoot;
        [Tooltip("anchoredPosition when the buttons are on-screen (a tile is selected).")]
        [SerializeField] private Vector2 buttonsShownPosition;
        [Tooltip("anchoredPosition when the buttons are off-screen (nothing selected).")]
        [SerializeField] private Vector2 buttonsHiddenPosition;

        [Header("Buttons")]
        [SerializeField] private Button regionButton;
        [SerializeField] private Button tileButton;

        [Header("Content (both parented under the shared root)")]
        [SerializeField] private RegionHealthUI header;
        [SerializeField] private InspectPanelUI body;

        [Header("Header Background Colour per Mode")]
        [SerializeField] private Color regionHeaderColor = new Color(0.20f, 0.45f, 0.70f);
        [SerializeField] private Color tileHeaderColor   = new Color(0.35f, 0.55f, 0.20f);

        [Header("Tween")]
        [Tooltip("Slide duration in seconds (LeanTween — project standard).")]
        [SerializeField] private float tweenDuration = 0.3f;
        [SerializeField] private LeanTweenType tweenEase = LeanTweenType.easeOutCubic;

        [Header("Systems")]
        [SerializeField] private TileSelector tileSelector;

        // 0 = closed, 1 = Region, 2 = Tile.
        private int panelMode = 0;

        // The tile driving the panel; null == nothing selected == buttons hidden + panel closed.
        private Tile _currentTile;

        // ── Lifecycle ────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (tileSelector == null)
                tileSelector = FindObjectOfType<TileSelector>();

            if (panelRoot == null)   Debug.LogError($"{name}: SelectedInfoPanelController.panelRoot is unwired — the panel can't slide.", this);
            if (buttonsRoot == null)  Debug.LogWarning($"{name}: SelectedInfoPanelController.buttonsRoot is unwired — the Region/Tile buttons won't tween.", this);
            if (header == null)       Debug.LogWarning($"{name}: SelectedInfoPanelController.header (RegionHealthUI) is unwired.", this);
            if (body == null)         Debug.LogWarning($"{name}: SelectedInfoPanelController.body (InspectPanelUI) is unwired.", this);

            // Subscribe in Awake (not OnEnable): plain C# delegates keep firing on an inactive
            // GameObject, so selection still reaches us even if this controller's GameObject is
            // ever hidden — no deadlock where a hidden panel can never be re-shown.
            if (tileSelector != null)
            {
                tileSelector.OnTileSelected   += HandleTileSelected;
                tileSelector.OnTileDeselected += HandleTileDeselected;
            }
            else
            {
                Debug.LogError($"{name}: SelectedInfoPanelController found no TileSelector — the panel " +
                               "is selection-driven and its buttons will never appear. Wire the ref.", this);
            }

            if (regionButton != null) regionButton.onClick.AddListener(OnRegionButtonPressed);
            if (tileButton   != null) tileButton.onClick.AddListener(OnTileButtonPressed);

            // Start closed + buttons hidden, no tween.
            panelMode = 0;
            if (panelRoot != null)  panelRoot.anchoredPosition  = panelHiddenPosition;
            if (buttonsRoot != null) buttonsRoot.anchoredPosition = buttonsHiddenPosition;
        }

        private void Start()
        {
            // RunManager (-100) initialises before this default-order controller, so subscribe in
            // Start when its Instance is guaranteed set (mirrors RegionManager / RegionOutlineRenderer).
            if (RunManager.Instance != null)
                RunManager.Instance.OnDayResolved += HandleDayResolved;
        }

        private void OnDestroy()
        {
            if (tileSelector != null)
            {
                tileSelector.OnTileSelected   -= HandleTileSelected;
                tileSelector.OnTileDeselected -= HandleTileDeselected;
            }
            if (regionButton != null) regionButton.onClick.RemoveListener(OnRegionButtonPressed);
            if (tileButton   != null) tileButton.onClick.RemoveListener(OnTileButtonPressed);
            if (RunManager.Instance != null)
                RunManager.Instance.OnDayResolved -= HandleDayResolved;
        }

        // ── Selection gates ────────────────────────────────────────────────────────

        private void HandleTileSelected(Tile tile, Vector3 _)
        {
            _currentTile = tile;
            TweenButtons(true);

            // If the panel was already open (a previous selection), re-populate its content for
            // the new subject; the panel stays at whatever mode the player left it in.
            if (panelMode != 0) ApplyMode(panelMode);
        }

        private void HandleTileDeselected()
        {
            _currentTile = null;
            ClosePanel();           // force mode 0 + tween panel out
            TweenButtons(false);    // hide the buttons
        }

        // ── Button handlers (guarded: no-op without a live selection) ───────────────

        private void OnRegionButtonPressed()
        {
            if (_currentTile == null) return;
            TogglePanel(1);
        }

        private void OnTileButtonPressed()
        {
            if (_currentTile == null) return;
            TogglePanel(2);
        }

        /// <summary>
        /// Closed → open at <paramref name="targetMode"/> (tween in). Open at the SAME mode →
        /// close (tween out). Open at the OTHER mode → swap content + header colour, no tween.
        /// </summary>
        private void TogglePanel(int targetMode)
        {
            if (panelMode == 0)             OpenPanel(targetMode);
            else if (panelMode == targetMode) ClosePanel();
            else                            SwapMode(targetMode);
        }

        private void OpenPanel(int mode)
        {
            panelMode = mode;
            ApplyMode(mode);
            TweenPanel(true);
        }

        private void ClosePanel()
        {
            if (panelMode == 0) return;
            panelMode = 0;
            TweenPanel(false);
        }

        private void SwapMode(int mode)
        {
            panelMode = mode;
            ApplyMode(mode);   // content + header colour swap, no tween
        }

        // ── Mode content ────────────────────────────────────────────────────────────

        /// <summary>Populates header + body for the given mode from the current selection.</summary>
        private void ApplyMode(int mode)
        {
            if (_currentTile == null) return;

            if (mode == 1) // Region — the parent region of the selected tile
            {
                int regionID = _currentTile.regionID;
                RegionManager rm = RegionManager.Instance;
                float health = rm != null ? rm.GetRegionHealth(regionID) : 0f;
                float trend  = rm != null ? rm.GetRegionHealthTrend(regionID) : 0f;

                if (header != null)
                {
                    header.SetHeaderColor(regionHeaderColor);
                    // header.ShowRegion(regionID, health, trend);
                }
                if (body != null) body.PopulateRegion(regionID);
            }
            else if (mode == 2) // Tile — the selected tile itself
            {
                float health = _currentTile.CalculateHealth();
                float trend  = TileManager.Instance != null ? TileManager.Instance.GetTileHealthTrend(_currentTile) : 0f;

                if (header != null)
                {
                    header.SetHeaderColor(tileHeaderColor);
                    // header.ShowTile(TileDisplayName(_currentTile), health, trend);
                }

                if (body != null)
                {
                    body.Populate(_currentTile);
                    Debug.Log("Populated");
                }
            }
        }

        private static string TileDisplayName(Tile tile)
        {
            if (tile.entity != null && tile.entity.def != null && !string.IsNullOrEmpty(tile.entity.def.displayName))
                return tile.entity.def.displayName;
            return $"Tile ({tile.gridPosition.x}, {tile.gridPosition.y})";
        }

        // ── Daily live-refresh (mirrors RegionOutlineRenderer / HudController cadence) ──

        private void HandleDayResolved(int day)
        {
            if (panelMode != 0 && _currentTile != null)
                ApplyMode(panelMode);
        }

        // ── Tweens ───────────────────────────────────────────────────────────────────

        private void TweenPanel(bool show)
        {
            if (panelRoot == null) return;
            Vector2 target = show ? panelShownPosition : panelHiddenPosition;
            LeanTween.cancel(panelRoot.gameObject);
            LeanTween.move(panelRoot, (Vector3)target, tweenDuration)
                .setEase(tweenEase)
                .setIgnoreTimeScale(true); // UI must slide even while the sim is event-paused (timeScale 0)
        }

        private void TweenButtons(bool show)
        {
            if (buttonsRoot == null) return;
            Vector2 target = show ? buttonsShownPosition : buttonsHiddenPosition;
            LeanTween.cancel(buttonsRoot.gameObject);
            LeanTween.move(buttonsRoot, (Vector3)target, tweenDuration)
                .setEase(tweenEase)
                .setIgnoreTimeScale(true);
        }
    }
}
