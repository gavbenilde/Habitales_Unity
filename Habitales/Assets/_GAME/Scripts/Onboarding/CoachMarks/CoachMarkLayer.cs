using UnityEngine;

namespace Habitales.Onboarding
{
    /// <summary>
    /// Routes <c>OnboardingDirector.OnCoachMarkRequested</c> to the matching coach-mark widget.
    /// The director is dumb about widget lifetime — it just says "show a BlinkingTileMarker at
    /// this world point" or "hide the CornerReminder"; this layer owns the wiring and injects
    /// the camera each widget uses for world→screen conversion. Wire one instance per gameplay
    /// scene with the widgets assigned (leave any unused kind null — its requests are ignored).
    /// </summary>
    public class CoachMarkLayer : MonoBehaviour
    {
        [Header("Camera (world→screen for the widgets)")]
        [SerializeField] private Camera screenCamera;   // defaults to Camera.main if null

        [Header("Widgets (one per CoachMarkKind)")]
        [SerializeField] private BlinkingTileMarker blinkingTileMarker;
        [SerializeField] private FidgetArrow fidgetArrow;
        [SerializeField] private GhostMouseClick ghostMouseClick;
        [SerializeField] private GhostMouseDrag ghostMouseDrag;
        [SerializeField] private CornerReminder cornerReminder;

        void Start()
        {
            if (screenCamera == null) screenCamera = Camera.main;
            if (screenCamera == null)
                Debug.LogWarning($"{name}: no screenCamera and Camera.main is null — coach-marks can't convert world→screen until a camera exists.", this);

            InjectAndHide(blinkingTileMarker);
            InjectAndHide(fidgetArrow);
            InjectAndHide(ghostMouseClick);
            InjectAndHide(ghostMouseDrag);
            InjectAndHide(cornerReminder);

            if (OnboardingDirector.Instance != null)
                OnboardingDirector.Instance.OnCoachMarkRequested += HandleCoachMarkRequested;
            else
                Debug.LogWarning($"{name}: OnboardingDirector.Instance is null at Start — coach-marks will not be driven.", this);
        }

        void OnDestroy()
        {
            if (OnboardingDirector.Instance != null)
                OnboardingDirector.Instance.OnCoachMarkRequested -= HandleCoachMarkRequested;
        }

        private void InjectAndHide(CoachMarkWidget w)
        {
            if (w == null) return;
            w.InjectCamera(screenCamera);
            w.Hide();
        }

        private void HandleCoachMarkRequested(CoachMarkRequest request)
        {
            CoachMarkWidget widget = WidgetFor(request.kind);
            if (widget == null)
            {
                if (request.kind != CoachMarkKind.None)
                    Debug.LogWarning($"{name}: no widget wired for CoachMarkKind.{request.kind} — request ignored. Assign it on the CoachMarkLayer.", this);
                return;
            }

            if (request.hide) widget.Hide();
            else              widget.Show(request);
        }

        private CoachMarkWidget WidgetFor(CoachMarkKind kind)
        {
            switch (kind)
            {
                case CoachMarkKind.BlinkingTileMarker: return blinkingTileMarker;
                case CoachMarkKind.FidgetArrow:        return fidgetArrow;
                case CoachMarkKind.GhostMouseClick:    return ghostMouseClick;
                case CoachMarkKind.GhostMouseDrag:     return ghostMouseDrag;
                case CoachMarkKind.CornerReminder:     return cornerReminder;
                default:                               return null;
            }
        }
    }
}
