using UnityEngine;
using TMPro;

namespace Habitales.UI
{
    /// <summary>
    /// Passive view for a single Journal card. Mirrors <see cref="WeatherForecastEntryUI"/>: no
    /// singleton reads, no game-state access — the controller (<see cref="JournalAppUI"/>) owns
    /// the data push via <see cref="Render"/> (Law 1 / Law 2). Pooled by the controller; never
    /// destroyed/rebuilt per refresh.
    /// </summary>
    public class JournalEntryUI : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Species display name, e.g. \"Narra Tree\".")]
        [SerializeField] private TMP_Text speciesLabel;

        [Tooltip("Short context line, e.g. \"Died to drought — Day 42\".")]
        [SerializeField] private TMP_Text contextLabel;

        [Tooltip("The resolved tier4JournalEntry body text.")]
        [SerializeField] private TMP_Text bodyLabel;

        // ─── One-time warning guards (avoid log spam across a pooled, growing list) ──
        private bool _warnedSpecies;
        private bool _warnedContext;
        private bool _warnedBody;

        /// <summary>
        /// Pushes one journal entry's data into this card. Passive: sets text only, no reads of
        /// any manager. Null-guards each wire with a one-time warning so a missing Inspector ref
        /// degrades gracefully instead of throwing (Law 3).
        /// </summary>
        public void Render(JournalEntry entry)
        {
            if (entry == null) return;

            if (speciesLabel != null)
            {
                speciesLabel.text = entry.displayName;
            }
            else if (!_warnedSpecies)
            {
                Debug.LogWarning($"{name}: JournalEntryUI — speciesLabel is not wired. Drag the species TMP_Text into the Inspector.", this);
                _warnedSpecies = true;
            }

            if (contextLabel != null)
            {
                contextLabel.text = $"{CauseDisplay(entry.cause)} — Day {entry.day}";
            }
            else if (!_warnedContext)
            {
                Debug.LogWarning($"{name}: JournalEntryUI — contextLabel is not wired. Drag the context TMP_Text into the Inspector.", this);
                _warnedContext = true;
            }

            if (bodyLabel != null)
            {
                bodyLabel.text = entry.text;
            }
            else if (!_warnedBody)
            {
                Debug.LogWarning($"{name}: JournalEntryUI — bodyLabel is not wired. Drag the body TMP_Text into the Inspector.", this);
                _warnedBody = true;
            }
        }

        private static string CauseDisplay(string cause) => cause switch
        {
            "drought"     => "Died to drought",
            "flood"       => "Died to flooding",
            "environment" => "Died to poor soil",
            _             => "Died"
        };
    }
}
