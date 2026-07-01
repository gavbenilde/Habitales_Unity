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
// PHASE 0c (now): presence validation + loud-fail (Law 3). Catches the #1 boot bug — a manager
//   missing from the scene or an unwired reference — and surfaces it as red console text that
//   highlights this object, instead of a silent NullReference three systems away.
//
// PHASE 3 (later): turn this from a presence-validator into the ordered INITIALIZER (drive each
//   manager's init in the documented sequence) once TileManager/ActionManager are promoted to
//   singletons and the managers expose explicit init entry points.
namespace Habitales.Core
{
    [DefaultExecutionOrder(-1000)]
    public class GameBootstrap : MonoBehaviour
    {
        [Header("Project assets (wire when available — Law 3)")]
        [Tooltip("The sole entityId → TileEntitySO registry. Leave empty until the SO-entity " +
                 "system is wired into the scene (Phase 4); an empty field warns, it does not fail.")]
        [SerializeField] private EntityRegistry entityRegistry;

        /// <summary>True once boot validation passed with zero failures. Future systems may gate on this.</summary>
        public static bool BootSucceeded { get; private set; }

        void Start()
        {
            if (GameLog.Core)
                Debug.Log("[GameBootstrap] Validating core systems…", this);

            int failures = 0;

            // ── Core singletons — uniform .Instance checks (Law 1 / S4: one reference model). ──
            failures += Require(ResourceManager.Instance != null, "ResourceManager");
            failures += Require(WeatherManager.Instance  != null, "WeatherManager");
            failures += Require(RegionManager.Instance   != null, "RegionManager");
            failures += Require(TileManager.Instance     != null, "TileManager");
            failures += Require(ActionManager.Instance   != null, "ActionManager");
            failures += Require(EventManager.Instance    != null, "EventManager");
            failures += Require(Habitales.Triggers.TriggerManager.Instance != null, "TriggerManager");
            failures += Require(DialogueManager.Instance != null, "DialogueManager");
            failures += Require(RunManager.Instance      != null, "RunManager");
            failures += Require(UIManager.Instance      != null, "UIManager");

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
                Debug.LogError($"[GameBootstrap] BOOT FAILED — {failures} core system(s) missing or invalid. " +
                               "Fix the errors above before playing; gameplay will behave unpredictably otherwise.", this);
            else if (GameLog.Core)
                Debug.Log("[GameBootstrap] Boot OK — all core systems present.", this);
        }

        // Loud-fail one requirement (Law 3). The `this` context makes the console error highlight
        // this GameObject on click. Returns 1 on failure so the caller can tally.
        private int Require(bool present, string systemName)
        {
            if (present) return 0;
            Debug.LogError($"[GameBootstrap] Missing core system: {systemName}. " +
                           "No live instance found in the scene — add/wire it before play.", this);
            return 1;
        }
    }
}
