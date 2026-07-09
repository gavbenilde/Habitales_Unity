using System.Collections;
using UnityEngine;
using Habitales.Triggers;

namespace Habitales.Onboarding
{
    /// <summary>
    /// Fires the opening "Priority Zero" beat at run start. This is the seam that answers
    /// "how does Priority Zero get on screen": the façade does NOT fire it — TriggerManager does,
    /// and TriggerManager routes the presentation through the popup pipeline.
    ///
    /// Flow:
    ///   OnboardingBootstrap.Start (run start)
    ///     → TriggerManager.Fire("priority_zero")                // the trigger
    ///       → PopupCatalogSO.GetById("priority_zero") → PopupSO
    ///         → UIManager.ShowPopup → PopupController           // the presentation
    ///
    /// Wiring (3 steps):
    ///   1. Author the Priority-Zero PopupSO with eventName = "priority_zero"
    ///      (copy source: OnboardingContent's Beat-0 contract lines).
    ///   2. Add that SO to the PopupCatalogSO that TriggerManager references.
    ///   3. Put this component in the scene — openingEventId already defaults to "priority_zero".
    ///
    /// The OnboardingDirector (swarm work) will absorb this as its beat-0 step.
    /// </summary>
    public class OnboardingBootstrap : MonoBehaviour
    {
        [Tooltip("Catalog id of the Priority-Zero PopupSO. Must exist in TriggerManager's PopupCatalogSO.")]
        [SerializeField] private string openingEventId = "priority_zero";

        [Tooltip("Frames to wait before firing, so all managers + UI have initialized.")]
        [SerializeField] private int warmupFrames = 1;

        [Tooltip("Fire only the first time this scene loads in a session.")]
        [SerializeField] private bool fireOnce = true;

        private static bool s_fired;

        private void Start()
        {
            if (string.IsNullOrWhiteSpace(openingEventId))
            {
                Debug.LogError($"{name}: OnboardingBootstrap has no openingEventId set — Priority Zero will not fire. Set the catalog id in the Inspector.", this);
                return;
            }
            StartCoroutine(FireAfterWarmup());
        }

        private IEnumerator FireAfterWarmup()
        {
            for (int i = 0; i < Mathf.Max(0, warmupFrames); i++)
                yield return null;

            if (fireOnce && s_fired) yield break;

            if (TriggerManager.Instance == null)
            {
                Debug.LogError($"{name}: TriggerManager.Instance is null — cannot fire '{openingEventId}'. Ensure a TriggerManager is in the scene.", this);
                yield break;
            }

            s_fired = true;
            TriggerManager.Instance.Fire(openingEventId);
        }

        /// <summary>Test hook: re-fire Priority Zero (e.g. from a debug key).</summary>
        public void FireNow()
        {
            if (!string.IsNullOrWhiteSpace(openingEventId)) TriggerManager.Instance?.Fire(openingEventId);
        }
    }
}
