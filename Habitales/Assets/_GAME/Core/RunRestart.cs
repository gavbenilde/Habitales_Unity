using UnityEngine;
using UnityEngine.SceneManagement;

// RunRestart — static helper for a scene-reload-based run restart. This is deliberately the
// CHEAP path: reload the active scene rather than hand-reset every manager's instance state.
// Every MonoBehaviour singleton (RunManager, ActionManager, TileManager, ResourceManager,
// WeatherManager, RegionManager, DialogueManager, TriggerManager, UIManager, ...) dies with the
// scene and re-Awakes clean on reload — their instance fields need no help here. This file exists
// ONLY to handle the handful of things that are NOT scene-scoped: true `static` fields that
// survive a scene reload, plus a couple of engine-global settings a paused menu may have left
// in a bad state.
//
// ── STATIC-STATE AUDIT (2026-07-08) — every mutable static found under _GAME/ and _UTILITIES/,
//    and the keep/reset call for each. Read this before adding a new static anywhere in the
//    project; if it's run-scoped, it belongs in the RESET block below. ──────────────────────────
//
//   RESET (run-scoped, must not leak into the next run):
//     • TimeFlowSignal.SpeedFactor / SmoothedSpeedFactor / WeatherTime — atmosphere FX time-lapse
//       signal. SpeedFactor/SmoothedSpeedFactor default to 1 (idle); WeatherTime is an
//       accumulated clock with no natural reset method, so we re-zero it by construction here
//       (see ResetTimeFlowSignal). Left stale, a restarted run's cloud/rain scroll would jump.
//     • DayNightCycleHandler.IsIdle — static gate that ResourceManager.AdvanceTimeStepped waits
//       on. It free-runs true→false→true within a single day-night cycle and is expected to be
//       true at rest, but if a restart happens mid-cycle (action running, popup interrupt, etc.)
//       it could be caught false with nothing left to flip it back — the NEXT run's first
//       AdvanceTimeStepped would then hang forever on WaitUntil(IsIdle). Force it true.
//     • SunSignal.Daylight / Tint (added 2026-07-28) — the day-night light term unlit art (Spine
//       walkers, sprite rigs) multiplies over itself, written every frame by DayNightCycleHandler.
//       Same shape of problem as TimeFlowSignal: a restart landing at night leaves it dark, and the
//       new scene's walkers would tint themselves midnight-blue on Awake, before the reloaded
//       handler's first Update writes the real value. Reset to full daylight.
//     • EventContext — static global/override token dictionaries + camera focus target. Has its
//       own ResetForNewRun() already (unused by any caller today); call it here so stale tokens
//       ("current_day", "world_health", ...) and any leftover focus-target don't leak into the
//       restarted run's first popup.
//
//   LEAVE ALONE (deliberately once-per-process, not run-scoped):
//     • OnboardingBootstrap.s_fired — "fire Priority Zero once per session" is the documented
//       intent (see its own doc comment). A restart is still the same session, so a returning
//       player should NOT see the opening beat again. Do not reset.
//     • GameLog.* (Core/Action/Cascade/Dialogue/RegionGen/Entity bools) — developer log-category
//       toggles, not run state. Resetting them would undo whatever the developer set at boot.
//     • RunConfig.SelectedSeasons (added 2026-07-08) — the main-menu season pick. A restarted
//       run is the SAME run setup, so the chosen length must survive the reload
//       (ResourceManager.Awake re-reads it). Overwritten only by the next menu confirm.
//
//   NOT APPLICABLE (dies with the scene already, nothing to do here):
//     • Every manager's `public static X Instance { get; private set; }` — instance-level
//       singleton state. SceneManager.LoadScene destroys the old GameObjects (Instance goes
//       stale but is immediately overwritten by the new Awake), so these need no explicit clear.
//     • WorkerFactory.portraitPool — static field, but nothing in the codebase currently assigns
//       it (dead field); even if it were wired from a scene asset, a fresh scene load would
//       re-assign it on the new Awake. No action needed.
//
// ── WHY NOT ResetFinalStretchVisuals() ────────────────────────────────────────────────────────
// TimeRemainingUI.ResetFinalStretchVisuals() exists as a restart hook, but this path does not
// call it: a scene reload destroys and rebuilds the whole HUD, including TimeRemainingUI's
// serialized color/scale state, so there is nothing stale to clean up. That method remains the
// correct hook for a FUTURE in-place restart (no scene reload) — do not delete it.
namespace Habitales.Core
{
    public static class RunRestart
    {
        /// <summary>
        /// Restarts the current run by reloading the active scene. Cross-scene-surviving static
        /// state is reset first (see the audit above), engine-global settings a pause menu may
        /// have left dirty are restored, then the scene reloads — every scene-scoped singleton
        /// re-Awakes clean. Safe to call from UI (PauseMenuController.RestartRun).
        /// </summary>
        public static void RestartCurrentRun()
        {
            ResetCrossSceneStatics();

            // The pause menu may have zeroed this while open; a reload with timeScale still at 0
            // would boot the new scene frozen.
            Time.timeScale = 1f;

            // Cancel every in-flight tween so nothing from the old scene's UI/juice fires a
            // callback against destroyed objects during/after the reload.
            LeanTween.cancelAll();

            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // The RESET half of the audit above, gathered in one place.
        private static void ResetCrossSceneStatics()
        {
            TimeFlowSignal.SpeedFactor         = 1f;
            TimeFlowSignal.SmoothedSpeedFactor = 1f;
            ResetTimeFlowWeatherTime();

            DayNightCycleHandler.ForceIdle();
            SunSignal.Reset();

            EventContext.ResetForNewRun();
        }

        // TimeFlowSignal.WeatherTime only exposes Accumulate(delta), not a setter — reset it by
        // accumulating the negative of its current value rather than adding a setter that every
        // other caller would then be tempted to (mis)use mid-run.
        private static void ResetTimeFlowWeatherTime()
        {
            float current = TimeFlowSignal.WeatherTime;
            if (current != 0f)
                TimeFlowSignal.Accumulate(-current);
        }
    }
}
