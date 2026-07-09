using System.Collections.Generic;
using UnityEngine;
using Habitales.Dialogue;   // DialogueManager (namespaced)
using Habitales.Entities;   // EntityRegistry
using Habitales.UI;         // UIManager

// GameBootstrap — single MonoBehaviour that confirms the core world is wired BEFORE any
// gameplay Start runs (arch §4). [DefaultExecutionOrder(-1000)] puts THIS component's
// Awake/Start ahead of every normal-order component.
//
// WHY VALIDATION RUNS IN Start, NOT Awake:
//   The core singletons assign their own `Instance` inside THEIR Awake. Because this
//   component's Awake runs FIRST (-1000), every sibling `Instance` is still null during our
//   Awake — validating there would false-fail on everything. Unity runs ALL Awakes before
//   ANY Start, so by our Start (still the first Start to run, thanks to -1000) every Instance
//   is populated and we are the first to confirm the world. Hence: presence-validate in Start.
//
// PHASE 0c (done): presence validation + loud-fail (Law 3). Catches the #1 boot bug — a manager
//   missing from the scene or an unwired reference — and surfaces it as red console text that
//   highlights this object, instead of a silent NullReference three systems away.
//
// PHASE 3 (this pass): the validation pass is now explicitly ORDERED to match the live
//   [DefaultExecutionOrder] pins (arch §4 init order: −200 ResourceManager/WeatherManager/
//   TileManager → −150 RegionManager → −100 ActionManager/TriggerManager/DialogueManager/
//   RunManager → UIManager), and reports as ONE consolidated ordered block instead of scattered
//   per-system lines. After validation passes, GameBootstrap invokes the optional
//   IBootstrapInit seam (below) on every present singleton, in that same order. This is
//   ADDITIVE ONLY: no [DefaultExecutionOrder] attribute and no manager's own Awake/Start init
//   were touched, and zero managers implement IBootstrapInit yet — the seam is the Phase-3
//   deliverable, not a retrofit. The game behaves identically with or without a GameBootstrap
//   in the scene, exactly as before.
namespace Habitales.Core
{
    /// <summary>
    /// Optional ordered-init seam (arch §4 Phase 3). A manager MAY implement this to receive an
    /// explicit "the whole boot-order singleton set is present" callback, invoked by GameBootstrap
    /// after validation succeeds, in the same dependency order the report below uses. Purely
    /// additive — nothing implements it yet, and nothing is required to. A manager that never
    /// implements it keeps initializing itself in its own Awake/Start exactly as today.
    /// </summary>
    public interface IBootstrapInit
    {
        void OnBootstrapInit();
    }

    [DefaultExecutionOrder(-1000)]
    public class GameBootstrap : MonoBehaviour
    {
        [Header("Project assets (wire when available — Law 3)")]
        [Tooltip("The sole entityId → TileEntitySO registry. Leave empty until the SO-entity " +
                 "system is wired into the scene (Phase 4); an empty field warns, it does not fail.")]
        [SerializeField] private EntityRegistry entityRegistry;

        /// <summary>True once boot validation passed with zero failures. Future systems may gate on this.</summary>
        public static bool BootSucceeded { get; private set; }

        // One row per required singleton, in dependency order (arch §4 init order — matches the
        // live [DefaultExecutionOrder] pins: −200 group, then −150, then −100 group, then UI).
        // MonoBehaviour is the common base every Instance is checked against; ordering is a plain
        // list (not a dictionary) because order IS the point.
        private struct BootEntry
        {
            public string Name;
            public MonoBehaviour Instance; // null if missing
        }

        void Start()
        {
            if (GameLog.Core)
                Debug.Log("[GameBootstrap] Validating core systems…", this);

            var entries = new List<BootEntry>
            {
                // ── −200: base data/service singletons ──────────────────────────────────
                new BootEntry { Name = "ResourceManager", Instance = ResourceManager.Instance },
                new BootEntry { Name = "WeatherManager",  Instance = WeatherManager.Instance },
                new BootEntry { Name = "TileManager",     Instance = TileManager.Instance },
                // ── −150: depends on the −200 group ─────────────────────────────────────
                new BootEntry { Name = "RegionManager",   Instance = RegionManager.Instance },
                // ── −100: orchestration layer, depends on everything above ──────────────
                new BootEntry { Name = "ActionManager",   Instance = ActionManager.Instance },
                new BootEntry { Name = "TriggerManager",  Instance = Habitales.Triggers.TriggerManager.Instance },
                new BootEntry { Name = "DialogueManager", Instance = DialogueManager.Instance },
                new BootEntry { Name = "RunManager",      Instance = RunManager.Instance },
                // ── default order: UI hub, depends on the whole sim being present ───────
                new BootEntry { Name = "UIManager",       Instance = UIManager.Instance },
            };

            // ── One consolidated, ordered report (replaces the old scattered per-line checks). ──
            int failures = 0;
            var report = new System.Text.StringBuilder();
            report.AppendLine("[GameBootstrap] Ordered boot report (dependency order):");
            foreach (var entry in entries)
            {
                bool present = entry.Instance != null;
                report.AppendLine($"  [{(present ? "OK  " : "MISSING")}] {entry.Name}");
                if (!present) failures++;
            }
            if (GameLog.Core || failures > 0)
                Debug.Log(report.ToString(), this);

            foreach (var entry in entries)
            {
                if (entry.Instance == null)
                    Debug.LogError($"[GameBootstrap] Missing core system: {entry.Name}. " +
                                   "No live instance found in the scene — add/wire it before play.", this);
            }

            // ── Project-asset references. Only validated when assigned: the SO-entity system is
            //    not wired into the live scene until Phase 4, so an empty field warns (expected),
            //    it does not fail the boot. ──────────────────────────────────────────────────────
            if (entityRegistry != null)
            {
                if (!entityRegistry.ValidateAll())
                {
                    Debug.LogError("[GameBootstrap] EntityRegistry failed validation — see the errors above.", entityRegistry);
                    failures++;
                }
            }
            else if (GameLog.Core)
            {
                Debug.LogWarning("[GameBootstrap] No EntityRegistry wired — skipping entity validation (expected until Phase 4).", this);
            }

            BootSucceeded = failures == 0;

            if (!BootSucceeded)
            {
                Debug.LogError($"[GameBootstrap] BOOT FAILED — {failures} core system(s) missing or invalid. " +
                               "Fix the errors above before playing; gameplay will behave unpredictably otherwise.", this);
                return; // Do not run ordered init on top of an incomplete boot.
            }

            if (GameLog.Core)
                Debug.Log("[GameBootstrap] Boot OK — all core systems present.", this);

            RunOrderedInit(entries);
        }

        // Phase-3 ordered-init pass (arch §4): invokes the optional IBootstrapInit seam on every
        // present singleton, in the same dependency order validation just confirmed. No-op today
        // — zero managers implement IBootstrapInit — but the seam is now live for any manager that
        // wants an explicit "everything is present" hook instead of relying on Awake/Start order.
        private void RunOrderedInit(List<BootEntry> entries)
        {
            foreach (var entry in entries)
            {
                if (entry.Instance is IBootstrapInit initable)
                {
                    if (GameLog.Core)
                        Debug.Log($"[GameBootstrap] Ordered init → {entry.Name}.OnBootstrapInit()", this);
                    initable.OnBootstrapInit();
                }
            }
        }
    }
}
