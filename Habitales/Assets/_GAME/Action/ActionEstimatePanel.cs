using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Habitales.UI.Actions
{
    /// <summary>
    /// Passive view: owns the estimate panel (tile count, days, fatigue, armed action name) and
    /// the Confirm button. Raises OnConfirmClicked upward; the controller mediates any
    /// TileSelector/ActionManager writes — this view never touches them directly (Law 1).
    /// </summary>
    public class ActionEstimatePanel : MonoBehaviour
    {
        [Tooltip("The root panel toggled by the controller (old brushControls).")]
        [SerializeField] private GameObject panelRoot;
        [Tooltip("Brush-size slider — shown only while a FloodFill action has a live blob; mirrors Ctrl+Scroll.")]
        [SerializeField] private Slider brushSizeSlider;
        [SerializeField] private Button confirmButton;
        [SerializeField] private TMP_Text armedActionNameText;
        [SerializeField] private TMP_Text tileCountText;
        [SerializeField] private TMP_Text daysEstimateText;
        [SerializeField] private TMP_Text fatigueEstimateText;

        /// <summary>Raised when the player presses Confirm. Law-2: on the click.</summary>
        public event Action OnConfirmClicked;

        /// <summary>
        /// Raised when the player drags the brush-size slider (whole tile counts). The controller
        /// mediates the TileSelector write (Law 1). Programmatic syncs never fire this.
        /// </summary>
        public event Action<int> OnBrushSizeChanged;

        // Guards against onValueChanged firing while ShowBrush programmatically moves min/max/value.
        private bool _suppressBrushCallback;

        void Awake()
        {
            // Slider stays hidden until a FloodFill action has a live blob (ShowBrush).
            if (brushSizeSlider != null) brushSizeSlider.gameObject.SetActive(false);

            // Panel starts hidden; the controller shows it when an action is armed.
            SetVisible(false);
        }

        void OnEnable()
        {
            if (confirmButton != null)
                confirmButton.onClick.AddListener(() => OnConfirmClicked?.Invoke());

            if (brushSizeSlider != null)
                brushSizeSlider.onValueChanged.AddListener(HandleSliderChanged);
        }

        void OnDisable()
        {
            confirmButton?.onClick.RemoveAllListeners();

            if (brushSizeSlider != null)
                brushSizeSlider.onValueChanged.RemoveListener(HandleSliderChanged);
        }

        void HandleSliderChanged(float value)
        {
            if (_suppressBrushCallback) return;
            OnBrushSizeChanged?.Invoke(Mathf.RoundToInt(value));
        }

        // ─── Brush-size slider ────────────────────────────────────────────────

        /// <summary>
        /// Shows the brush-size slider bound to [min, max] at <paramref name="current"/>.
        /// Idempotent — the controller calls this on every TileSelector.OnBrushSizeChanged, so
        /// it doubles as the sync path when the blob is resized by drag or Ctrl+Scroll.
        /// Hides itself when there's nothing to size (max == min).
        /// </summary>
        public void ShowBrush(int min, int max, int current)
        {
            if (brushSizeSlider == null) return;

            _suppressBrushCallback = true;
            brushSizeSlider.wholeNumbers = true;
            brushSizeSlider.minValue = min;
            brushSizeSlider.maxValue = max;
            brushSizeSlider.SetValueWithoutNotify(current);
            _suppressBrushCallback = false;

            brushSizeSlider.gameObject.SetActive(max > min);
        }

        /// <summary>Hides the brush-size slider (disarm / non-FloodFill action).</summary>
        public void HideBrush()
        {
            if (brushSizeSlider != null) brushSizeSlider.gameObject.SetActive(false);
        }

        // ─── Visibility ───────────────────────────────────────────────────────

        /// <summary>Shows or hides the estimate panel root.</summary>
        public void SetVisible(bool visible)
        {
            if (panelRoot != null) panelRoot.SetActive(visible);
        }

        // ─── Confirm interactability ──────────────────────────────────────────

        /// <summary>Enables or disables the Confirm button's interactable state.</summary>
        public void SetConfirmInteractable(bool interactable)
        {
            if (confirmButton != null) confirmButton.interactable = interactable;
        }

        // ─── Data render ──────────────────────────────────────────────────────

        /// <summary>
        /// Writes the current estimates into the panel's text fields. All refs are null-guarded.
        /// </summary>
        public void Render(int tileCount, int days, int fatigue, string armedName)
        {
            if (armedActionNameText != null) armedActionNameText.text = armedName;
            if (tileCountText       != null) tileCountText.text       = $"{tileCount} tiles";
            if (daysEstimateText    != null) daysEstimateText.text    = $"{days} days";
            if (fatigueEstimateText != null) fatigueEstimateText.text = $"~{fatigue}";
        }

        // ─── Rect resolution ─────────────────────────────────────────────────

        /// <summary>
        /// Returns the Confirm button's RectTransform, or null if not wired. Read by the
        /// controller to satisfy the frozen GetConfirmButtonRect() surface.
        /// </summary>
        public RectTransform GetConfirmRect()
            => confirmButton != null ? confirmButton.transform as RectTransform : null;
    }
}
