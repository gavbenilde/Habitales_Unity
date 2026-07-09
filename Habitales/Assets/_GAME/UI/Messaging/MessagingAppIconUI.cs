using TMPro;
using UnityEngine;
using Habitales.Dialogue;

namespace Habitales.UI
{
    /// <summary>
    /// Discord-style badge + aggressive shake on the messaging app icon.
    ///
    /// Inspector refs:
    ///   badgeRoot        — the pip GameObject (Image + background); hidden at UnreadCount == 0.
    ///   badgeCountText   — TextMeshProUGUI inside the pip showing the number.
    ///   iconRect         — RectTransform of the icon to shake (usually the same object
    ///                      this component sits on, or the phone button image).
    ///
    /// Subscribes to DialogueManager.OnUnreadChanged  → refresh badge.
    ///             DialogueManager.OnMessagesUpdated   → play shake + refresh badge.
    /// </summary>
    public class MessagingAppIconUI : MonoBehaviour, IUISubsystem
    {
        // ── IUISubsystem ──────────────────────────────────────────────
        //
        // Simple case: toggles this component's own GameObject (the icon root).
        // No internal show/hide state machine to fight — badge/shake logic just
        // stops mattering while the icon GameObject is inactive.

        public string SubsystemId => "messagingIcon";
        public bool   IsVisible   => gameObject.activeSelf;
        public void   SetVisible(bool visible) => gameObject.SetActive(visible);

        [Header("Badge")]
        [SerializeField] private GameObject          badgeRoot;
        [SerializeField] private TextMeshProUGUI     badgeCountText;

        [Header("Shake target")]
        [SerializeField] private RectTransform       iconRect;

        [Header("Shake tuning")]
        [SerializeField] private float shakeDuration  = 0.45f;
        [SerializeField] private float shakeAngle     = 18f;   // max rotation deg each side
        [SerializeField] private float shakeDistance  = 6f;    // max positional jitter px

        // ── Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            bool ok = true;

            if (badgeRoot == null)
            {
                Debug.LogError($"{name}: MessagingAppIconUI — badgeRoot is not wired. Drag the badge pip GameObject in.", this);
                ok = false;
            }
            if (badgeCountText == null)
            {
                Debug.LogError($"{name}: MessagingAppIconUI — badgeCountText is not wired. Drag the TMP label inside the pip.", this);
                ok = false;
            }
            if (iconRect == null)
            {
                Debug.LogError($"{name}: MessagingAppIconUI — iconRect is not wired. Drag the icon RectTransform to shake.", this);
                ok = false;
            }

            if (!ok) enabled = false;
        }

        private void OnEnable()
        {
            if (DialogueManager.Instance == null) return;
            DialogueManager.Instance.OnUnreadChanged   -= HandleUnreadChanged;
            DialogueManager.Instance.OnUnreadChanged   += HandleUnreadChanged;
            DialogueManager.Instance.OnMessagesUpdated -= HandleMessagesUpdated;
            DialogueManager.Instance.OnMessagesUpdated += HandleMessagesUpdated;

            RefreshBadge();
        }

        private void OnDisable()
        {
            if (DialogueManager.Instance == null) return;
            DialogueManager.Instance.OnUnreadChanged   -= HandleUnreadChanged;
            DialogueManager.Instance.OnMessagesUpdated -= HandleMessagesUpdated;
        }

        // ── Event Handlers ─────────────────────────────────────────────

        private void HandleUnreadChanged(string _) => RefreshBadge();

        private void HandleMessagesUpdated(string _)
        {
            RefreshBadge();
            PlayShake();
        }

        // ── Badge ──────────────────────────────────────────────────────

        private void RefreshBadge()
        {
            if (badgeRoot == null || badgeCountText == null) return;

            int count = DialogueManager.Instance != null
                ? DialogueManager.Instance.UnreadCount
                : 0;

            bool show = count > 0;
            badgeRoot.SetActive(show);

            if (show)
                badgeCountText.text = count > 99 ? "99+" : count.ToString();
        }

        // ── Shake ──────────────────────────────────────────────────────

        private void PlayShake()
        {
            if (iconRect == null) return;

            // Cancel any in-flight tweens on this target so they don't stack.
            LeanTween.cancel(iconRect.gameObject);

            // Reset to origin first so each shake starts clean.
            iconRect.localRotation = Quaternion.identity;
            iconRect.localPosition = Vector3.zero;

            // Rotation jitter: rock left–right–left–right → settle.
            float a = shakeAngle;
            LeanTween.sequence()
                .append(LeanTween.rotateZ(iconRect.gameObject,  a,     shakeDuration * 0.12f).setEase(LeanTweenType.easeOutQuad))
                .append(LeanTween.rotateZ(iconRect.gameObject, -a,     shakeDuration * 0.20f).setEase(LeanTweenType.easeInOutQuad))
                .append(LeanTween.rotateZ(iconRect.gameObject,  a * 0.6f, shakeDuration * 0.18f).setEase(LeanTweenType.easeInOutQuad))
                .append(LeanTween.rotateZ(iconRect.gameObject, -a * 0.4f, shakeDuration * 0.18f).setEase(LeanTweenType.easeInOutQuad))
                .append(LeanTween.rotateZ(iconRect.gameObject,  0f,    shakeDuration * 0.32f).setEase(LeanTweenType.easeOutElastic));

            // Positional jitter: small random-direction nudge that settles back.
            Vector3 jitter = new Vector3(
                Random.Range(-shakeDistance, shakeDistance),
                Random.Range(-shakeDistance, shakeDistance),
                0f);

            LeanTween.moveLocal(iconRect.gameObject, jitter, shakeDuration * 0.15f)
                .setEase(LeanTweenType.easeOutQuad)
                .setOnComplete(() =>
                    LeanTween.moveLocal(iconRect.gameObject, Vector3.zero, shakeDuration * 0.45f)
                        .setEase(LeanTweenType.easeOutElastic));
        }
    }
}
