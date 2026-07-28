using System.Collections.Generic;
using UnityEngine;

namespace Habitales.UI
{
    /// <summary>
    /// In-game tablet journal app — lists <see cref="JournalStore"/>'s entries newest-first.
    /// Copies <see cref="WeatherAppUI"/>'s panel pattern: subscribes
    /// to a meaning-event and refreshes passive pooled views — no per-frame polling (Law 2).
    /// Reads JournalStore only via its public getters, never writes its state directly (Law 1).
    ///
    /// Unlike the weather strip (a fixed 21-day window), the Journal grows without bound over a
    /// run, so the pool here GROWS on demand instead of being pre-sized once.
    ///
    /// Clears the unread badge on open (<c>OnEnable</c> → <c>JournalStore.MarkAllRead()</c>) —
    /// mirrors <see cref="MessagingAppIconUI"/>'s "clears on app open" contract.
    ///
    /// <para><b>REQUIRED SCENE WIRING (human, in the Unity editor):</b></para>
    /// <para>
    /// 1. Under the tablet canvas, create a panel GameObject "JournalApp" (this component lives
    ///    on it), alongside the other tablet apps (Chat, Weather). Start it inactive/hidden per
    ///    however the tablet app-switcher shows/hides apps elsewhere in the project.
    /// </para>
    /// <para>
    /// 2. Add a scrollable list container — a child GameObject with a <c>VerticalLayoutGroup</c>
    ///    + <c>ContentSizeFitter</c>, inside a <c>ScrollRect</c> so a long run's journal can be
    ///    scrolled. Wire this Transform to <see cref="entryContainer"/>.
    /// </para>
    /// <para>
    /// 3. Build the entry prefab: a GameObject with <see cref="JournalEntryUI"/> attached, with
    ///    three TMP_Text children wired to its speciesLabel/contextLabel/bodyLabel. Save it as a
    ///    prefab and wire it to <see cref="entryPrefab"/>. Do NOT hand-place entries in the scene
    ///    — JournalAppUI pools/grows them at runtime from this single prefab.
    /// </para>
    /// <para>
    /// 4. Optionally wire <see cref="emptyStateLabel"/> to a TMP_Text shown when no entries exist
    ///    yet (e.g. "Nothing to report — your plants are doing fine.").
    /// </para>
    /// </summary>
    public class JournalAppUI : MonoBehaviour
    {
        [Header("Entry List")]
        [Tooltip("Prefab for a single journal card (JournalEntryUI). Pooled — grows on demand, never destroyed/rebuilt.")]
        [SerializeField] private JournalEntryUI entryPrefab;

        [Tooltip("Parent Transform for pooled journal cards (VerticalLayoutGroup, ideally inside a ScrollRect).")]
        [SerializeField] private Transform entryContainer;

        [Header("Empty State (optional)")]
        [Tooltip("Shown when JournalStore has zero entries yet. Optional — leave null to skip.")]
        [SerializeField] private GameObject emptyStateLabel;

        private readonly List<JournalEntryUI> _pooledEntries = new List<JournalEntryUI>();
        private bool _warnedNoStore;

        private void OnEnable()
        {
            if (!JournalStore.FeatureEnabled)
            {
                // Feature parked (JournalStore.FeatureEnabled = false) — close the app panel
                // rather than showing an empty list. OnEnable, not Awake: this also catches
                // anything that re-opens the panel later.
                gameObject.SetActive(false);
                return;
            }

            if (JournalStore.Instance != null)
            {
                JournalStore.Instance.OnEntryLogged -= HandleEntryLogged;
                JournalStore.Instance.OnEntryLogged += HandleEntryLogged;
                JournalStore.Instance.MarkAllRead(); // "clears on app open" (badge contract)
            }
            else if (!_warnedNoStore)
            {
                Debug.LogWarning("JournalAppUI: JournalStore not present — app will stay empty.", this);
                _warnedNoStore = true;
            }

            Refresh();
        }

        private void OnDisable()
        {
            if (JournalStore.Instance != null)
                JournalStore.Instance.OnEntryLogged -= HandleEntryLogged;
        }

        private void HandleEntryLogged(JournalEntry _) => Refresh();

        /// <summary>
        /// Rebuilds the card list from JournalStore's current entries (newest-first, already
        /// ordered by the store). Grows the pool as needed; extra pooled cards beyond the current
        /// entry count are deactivated rather than destroyed.
        /// </summary>
        private void Refresh()
        {
            if (entryPrefab == null || entryContainer == null)
            {
                Debug.LogWarning("JournalAppUI: entryPrefab or entryContainer is not wired — journal list will stay empty.", this);
                return;
            }

            IReadOnlyList<JournalEntry> entries = JournalStore.Instance != null
                ? JournalStore.Instance.Entries
                : System.Array.Empty<JournalEntry>();

            EnsurePool(entries.Count);

            for (int i = 0; i < _pooledEntries.Count; i++)
            {
                bool active = i < entries.Count;
                _pooledEntries[i].gameObject.SetActive(active);
                if (active)
                    _pooledEntries[i].Render(entries[i]);
            }

            if (emptyStateLabel != null)
                emptyStateLabel.SetActive(entries.Count == 0);
        }

        /// <summary>Grows the pool to at least <paramref name="count"/> entries. Never shrinks —
        /// extra cards are deactivated by <see cref="Refresh"/>, not destroyed, so the Journal
        /// never re-instantiates on every death.</summary>
        private void EnsurePool(int count)
        {
            for (int i = _pooledEntries.Count; i < count; i++)
            {
                JournalEntryUI entry = Instantiate(entryPrefab, entryContainer);
                _pooledEntries.Add(entry);
            }
        }
    }
}
