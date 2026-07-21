using System.Collections.Generic;
using UnityEngine;

namespace Habitales.UI
{
    /// <summary>
    /// One T4 journal card — a species' resolved <c>tier4JournalEntry</c> text plus the death
    /// context that logged it.
    /// </summary>
    public class JournalEntry
    {
        public string entityId;
        public string displayName;
        public string text;
        public string cause; // "drought" / "flood" / "environment"
        public int day;
    }

    /// <summary>
    /// Per-run Journal store — logged from TriggerManager's T3 handler, gated by the
    /// window-scoped <c>T4:{entityId}</c> dedup key it owns. Entries are newest-first.
    /// No persistence anywhere: like TriggerManager's own run-scoped state, this resets simply by
    /// being a fresh MonoBehaviour instance after a scene reload (RunRestart is a full scene
    /// reload — see CheckInScheduler's identical note), so there is no explicit "new run" hook.
    ///
    /// Also owns the unread-count Law-1 surface the icon badge reads (mirrors
    /// <c>DialogueManager.UnreadCount</c> / <c>OnUnreadChanged</c> — S2: unread state lives with
    /// the store that owns the entries, not the badge view).
    ///
    /// WIRING (human): add this component to a persistent scene object (alongside the other
    /// managers). No Inspector refs to wire — it's a pure data store.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class JournalStore : MonoBehaviour
    {
        public static JournalStore Instance { get; private set; }

        // Newest-first (new entries are inserted at index 0).
        private readonly List<JournalEntry> _entries = new List<JournalEntry>();

        /// <summary>Read-only, newest-first. Law 1 — mutate only via <see cref="LogEntry"/>.</summary>
        public IReadOnlyList<JournalEntry> Entries => _entries;

        /// <summary>Unread journal-entry count. Law 1 — write only via LogEntry/MarkAllRead.</summary>
        public int UnreadCount { get; private set; }

        /// <summary>Fired whenever a new entry is logged (newest entry as the arg). Drives the
        /// panel refresh + the icon's shake juice.</summary>
        public event System.Action<JournalEntry> OnEntryLogged;

        /// <summary>Fired whenever <see cref="UnreadCount"/> changes. Drives the badge pip.</summary>
        public event System.Action<int> OnUnreadChanged;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[JournalStore] Duplicate instance on '{name}' — destroying it. Only one JournalStore should exist per scene.", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Logs a new T4 entry (newest-first) and bumps the unread badge. Called by
        /// TriggerManager's T3 handler — already gated by the T4:{entityId} window key there,
        /// so this method does no dedup of its own (S2 — dedup ownership stays in TriggerManager).</summary>
        public void LogEntry(JournalEntry entry)
        {
            if (entry == null) return;
            _entries.Insert(0, entry);

            UnreadCount++;
            OnUnreadChanged?.Invoke(UnreadCount);
            OnEntryLogged?.Invoke(entry);
        }

        /// <summary>Clears the unread badge — called when the Journal app opens.</summary>
        public void MarkAllRead()
        {
            if (UnreadCount == 0) return;
            UnreadCount = 0;
            OnUnreadChanged?.Invoke(UnreadCount);
        }
    }
}
