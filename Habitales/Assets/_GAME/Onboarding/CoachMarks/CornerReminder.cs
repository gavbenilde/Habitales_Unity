using TMPro;
using UnityEngine;

namespace Habitales.Onboarding
{
    /// <summary>
    /// Persistent screen-corner text reminder (e.g. "Select / Reselect", "Select multiple").
    /// Unlike the other coach-marks it ignores the world target — it lives where its prefab
    /// is anchored and just shows <see cref="CoachMarkWidget.LabelText"/> until a hide request
    /// for <see cref="CoachMarkKind.CornerReminder"/> arrives. Optional LeanTween fade-in.
    /// </summary>
    public class CornerReminder : CoachMarkWidget
    {
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private CanvasGroup canvasGroup;   // optional — for fade
        [SerializeField] private float fadeSeconds = 0.25f;

        protected override void DoShow()
        {
            if (label == null)
            {
                Debug.LogError($"{name}: CornerReminder has no label assigned — wire a TextMeshProUGUI in the Inspector.", this);
                return;
            }
            label.text = LabelText;

            if (canvasGroup != null)
            {
                LeanTween.cancel(canvasGroup.gameObject);
                canvasGroup.alpha = 0f;
                LeanTween.alphaCanvas(canvasGroup, 1f, fadeSeconds).setIgnoreTimeScale(true);
            }
        }
    }
}
