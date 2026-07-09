using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Habitales.Core;

namespace Habitales.UI
{
    /// <summary>
    /// Main-menu season picker (added 2026-07-08). Pressing Play opens this panel instead of
    /// loading the run scene directly; the player steps a season count between
    /// <see cref="RunConfig.MinSeasons"/> and <see cref="RunConfig.MaxSeasons"/> with the two
    /// arrow buttons, Confirm writes <see cref="RunConfig.SelectedSeasons"/> (read by
    /// ResourceManager.Awake as seasons × 180 days) and hands control back to MainMenu to
    /// load the run scene.
    ///
    /// Stepper behavior (user spec): label reads "1 Season" / "2 Seasons" / …; the LEFT arrow
    /// disappears at 1 Season, the RIGHT arrow disappears at 10 Seasons; the subtext below
    /// shows the day count ("180 Days" … "1800 Days").
    ///
    /// WIRING (human):
    ///   1. Build the panel in the Main Menu scene: root panel → season label (TMP) with an
    ///      arrow Button on each side, a smaller day-count TMP right below, a Confirm button,
    ///      and (optional) a Back button.
    ///   2. Add this component (anywhere persistent in the menu canvas), wire every
    ///      [SerializeField] below — all loud-fail at Awake except the optional backButton.
    ///   3. Wire MainMenu.seasonSelectPanel to this component. The existing Play button keeps
    ///      calling MainMenu.StartRun() — no OnClick change needed.
    /// </summary>
    public class SeasonSelectPanelUI : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject panelRoot; // toggled by Open/Close

        [Header("Season Stepper")]
        [SerializeField] private Button leftArrowButton;
        [SerializeField] private Button rightArrowButton;
        [SerializeField] private TextMeshProUGUI seasonsText;
        [Tooltip("Small readable subtext right below the season label — \"180 Days\" … \"1800 Days\".")]
        [SerializeField] private TextMeshProUGUI daysSubtext;

        [Header("Buttons")]
        [SerializeField] private Button confirmButton;
        [Tooltip("OPTIONAL: closes the panel without starting a run.")]
        [SerializeField] private Button backButton;

        private int _seasons = RunConfig.MinSeasons;
        private Action _onConfirm;

        private void Awake()
        {
            ValidateRefs();
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void OnEnable()
        {
            if (leftArrowButton  != null) leftArrowButton.onClick.AddListener(HandleLeft);
            if (rightArrowButton != null) rightArrowButton.onClick.AddListener(HandleRight);
            if (confirmButton    != null) confirmButton.onClick.AddListener(HandleConfirm);
            if (backButton       != null) backButton.onClick.AddListener(Close);
        }

        private void OnDisable()
        {
            if (leftArrowButton  != null) leftArrowButton.onClick.RemoveListener(HandleLeft);
            if (rightArrowButton != null) rightArrowButton.onClick.RemoveListener(HandleRight);
            if (confirmButton    != null) confirmButton.onClick.RemoveListener(HandleConfirm);
            if (backButton       != null) backButton.onClick.RemoveListener(Close);
        }

        private void ValidateRefs()
        {
            bool ok = true;
            ok &= Require(panelRoot,        nameof(panelRoot));
            ok &= Require(leftArrowButton,  nameof(leftArrowButton));
            ok &= Require(rightArrowButton, nameof(rightArrowButton));
            ok &= Require(seasonsText,      nameof(seasonsText));
            ok &= Require(daysSubtext,      nameof(daysSubtext));
            ok &= Require(confirmButton,    nameof(confirmButton));
            // backButton is deliberately optional.

            if (!ok) enabled = false;
        }

        private bool Require(UnityEngine.Object field, string fieldName)
        {
            if (field != null) return true;
            Debug.LogError($"{name}: SeasonSelectPanelUI.{fieldName} missing — wire it in the Inspector.", this);
            return false;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Opens the picker. <paramref name="onConfirm"/> fires once when the player confirms,
        /// AFTER RunConfig.SelectedSeasons has been written — MainMenu loads the run scene there.
        /// </summary>
        public void Open(Action onConfirm)
        {
            if (!enabled)
            {
                Debug.LogError($"{name}: Open() called but component is disabled (missing refs) — starting run at the previous/default length instead.", this);
                onConfirm?.Invoke();
                return;
            }

            _onConfirm = onConfirm;

            // Re-opening in the same session starts from the last confirmed pick.
            _seasons = RunConfig.HasSelection
                ? Mathf.Clamp(RunConfig.SelectedSeasons, RunConfig.MinSeasons, RunConfig.MaxSeasons)
                : RunConfig.MinSeasons;

            Render();
            panelRoot.SetActive(true);
        }

        /// <summary>Closes without confirming (Back button / MainMenu escape hatch).</summary>
        public void Close()
        {
            _onConfirm = null;
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        // ── Handlers ──────────────────────────────────────────────────────────

        private void HandleLeft()
        {
            _seasons = Mathf.Max(RunConfig.MinSeasons, _seasons - 1);
            Render();
        }

        private void HandleRight()
        {
            _seasons = Mathf.Min(RunConfig.MaxSeasons, _seasons + 1);
            Render();
        }

        private void HandleConfirm()
        {
            RunConfig.SelectedSeasons = _seasons;

            var onConfirm = _onConfirm;
            _onConfirm = null;
            panelRoot.SetActive(false);
            onConfirm?.Invoke();
        }

        // ── View ──────────────────────────────────────────────────────────────

        private void Render()
        {
            seasonsText.text = _seasons == 1 ? "1 Season" : $"{_seasons} Seasons";
            daysSubtext.text = $"{RunConfig.RunLengthDaysFor(_seasons)} Days";

            // The bound arrow disappears entirely (not just disabled) per spec.
            leftArrowButton.gameObject.SetActive(_seasons > RunConfig.MinSeasons);
            rightArrowButton.gameObject.SetActive(_seasons < RunConfig.MaxSeasons);
        }
    }
}
