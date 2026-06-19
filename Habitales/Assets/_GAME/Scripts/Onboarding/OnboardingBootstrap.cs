using System.Collections;
using UnityEngine;

namespace Habitales.Onboarding
{
    /// <summary>
    /// Fires the opening "Priority Zero" beat at run start. This is the seam that answers
    /// "how does Priority Zero get on screen": the façade does NOT fire it — EventManager does,
    /// and EventManager routes the presentation through the façade (NarrativePopupManager).
    ///
    /// Flow:
    ///   OnboardingBootstrap.Start (run start)
    ///     → EventManager.FireEventByID("priority_zero")        // the trigger
    ///       → EventManager queues + pauses + resolves tokens
    ///         → NarrativePopupManager.ShowHeadline(...)        // the presentation (façade)
    ///           → EventPopupUI (the existing full-screen one-shot)
    ///
    /// Wiring (3 steps):
    ///   1. On the Priority-Zero GameEventSO set: eventID = "priority_zero",
    ///      triggerType = Manual, fireOnce = true (+ headline / bodyText / speaker / portrait).
    ///   2. Add that SO to the GameEventRegistry that EventManager references.
    ///   3. Put this component in the scene and assign the same SO to `openingEvent`.
    ///
    /// The OnboardingDirector (swarm work) will absorb this as its beat-0 step.
    /// </summary>
    public class OnboardingBootstrap : MonoBehaviour
    {
        [Tooltip("The Priority-Zero GameEventSO. Must also be in EventManager's GameEventRegistry, triggerType = Manual.")]
        [SerializeField] private GameEventSO openingEvent;

        [Tooltip("Frames to wait before firing, so all managers + UI have initialized.")]
        [SerializeField] private int warmupFrames = 1;

        [Tooltip("Fire only the first time this scene loads in a session.")]
        [SerializeField] private bool fireOnce = true;

        private static bool s_fired;

        private void Start()
        {
            if (openingEvent == null)
            {
                Debug.LogError($"{name}: OnboardingBootstrap has no openingEvent assigned — Priority Zero will not fire. Assign the Priority-Zero GameEventSO in the Inspector.", this);
                return;
            }
            StartCoroutine(FireAfterWarmup());
        }

        private IEnumerator FireAfterWarmup()
        {
            for (int i = 0; i < Mathf.Max(0, warmupFrames); i++)
                yield return null;

            if (fireOnce && s_fired) yield break;

            if (EventManager.Instance == null)
            {
                Debug.LogError($"{name}: EventManager.Instance is null — cannot fire '{openingEvent.eventID}'. Ensure an EventManager is in the scene.", this);
                yield break;
            }

            s_fired = true;
            EventManager.Instance.FireEventByID(openingEvent.eventID);
        }

        /// <summary>Test hook: re-fire Priority Zero (e.g. from a debug key).</summary>
        public void FireNow()
        {
            if (openingEvent != null) EventManager.Instance?.FireEventByID(openingEvent.eventID);
        }
    }
}
