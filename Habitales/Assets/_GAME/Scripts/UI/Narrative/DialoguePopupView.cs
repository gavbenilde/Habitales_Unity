using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Habitales.Dialogue;

namespace Habitales.UI
{
    /// <summary>
    /// The intrusive, foreground "Dialog Popup UI": same dimmed/blocking feel as the
    /// one-shot EventPopupUI, but plays a THREAD of lines (visual-novel style) with a
    /// character portrait, advancing one line per Next press. Pauses the simulation
    /// while open (RunManager.PauseForEvent) and releases it on completion.
    ///
    /// Driven exclusively by <see cref="NarrativePopupManager"/> — do not call directly.
    /// Wire one of these into the Phone/Narrative canvas and assign it on the manager.
    /// </summary>
    public class DialoguePopupView : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private GameObject overlayBlocker;   // full-screen dim + input block
        [SerializeField] private GameObject card;             // the centred dialogue card

        [Header("Speaker")]
        [SerializeField] private GameObject portraitBox;      // parent — hidden when a line has no portrait
        [SerializeField] private Image portrait;
        [SerializeField] private TextMeshProUGUI speakerLabel;

        [Header("Body")]
        [SerializeField] private TextMeshProUGUI bodyText;

        [Header("Advance")]
        [SerializeField] private Button nextButton;
        [SerializeField] private TextMeshProUGUI nextLabel;
        [SerializeField] private string nextWord = "Next";
        [SerializeField] private string lastWord = "OK";

        private readonly List<ResolvedLine> _lines = new List<ResolvedLine>();
        private int _index;
        private bool _showPortrait;
        private Action _onComplete;
        private bool _refsOk;

        void Awake()
        {
            _refsOk = overlayBlocker && card && bodyText && nextButton;
            if (!_refsOk)
            {
                Debug.LogError($"{name}: DialoguePopupView is missing serialized refs — wire overlayBlocker / card / bodyText / nextButton in the Inspector.", this);
                enabled = false;
                return;
            }
            nextButton.onClick.AddListener(Advance);
            Hide();
        }

        /// <summary>Begin playing the supplied lines. Pauses the sim until the last line is dismissed.</summary>
        public void Play(IList<ResolvedLine> lines, bool showPortrait, Action onComplete)
        {
            if (!_refsOk) { onComplete?.Invoke(); return; }   // never softlock a caller waiting on us
            if (lines == null || lines.Count == 0) { onComplete?.Invoke(); return; }

            _lines.Clear();
            _lines.AddRange(lines);
            _index = 0;
            _showPortrait = showPortrait;
            _onComplete = onComplete;

            RunManager.Instance?.PauseForEvent();

            overlayBlocker.SetActive(true);
            card.SetActive(true);
            RenderCurrent();
        }

        private void RenderCurrent()
        {
            ResolvedLine line = _lines[_index];

            bodyText.text = line.body;
            if (speakerLabel) speakerLabel.text = line.displayName;

            bool drawPortrait = _showPortrait && line.portrait != null;
            if (portraitBox) portraitBox.SetActive(drawPortrait);
            if (drawPortrait && portrait) portrait.sprite = line.portrait;

            bool isLast = _index >= _lines.Count - 1;
            if (nextLabel) nextLabel.text = isLast ? lastWord : nextWord;
        }

        private void Advance()
        {
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
            RunManager.Instance?.ResumeFromEvent();
            Action cb = _onComplete;
            _onComplete = null;
            cb?.Invoke();
        }

        public void Hide()
        {
            if (overlayBlocker) overlayBlocker.SetActive(false);
            if (card) card.SetActive(false);
        }
    }
}
