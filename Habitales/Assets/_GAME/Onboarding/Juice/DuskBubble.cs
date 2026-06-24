using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// =============================================================================
//  DuskBubble — Work-Order B3
//  Namespace: Habitales.Onboarding
//
//  A single pooled "score juice" bubble: a small coloured card that spawns at a
//  world-space tile position, floats upward while fading out, then returns itself
//  to the DuskBubblePool.
//
//  Two modes:
//
//  PlayOneShot(worldPos, duration)
//    Free-floating world-space pop — used by delta tips and standalone effects.
//    Floats straight up, alpha fades to 0, then self-destroys (not pooled).
//
//  PlayPooled(worldPos, pool)
//    Standard pooled path (called by DuskBubblePool). On complete, returns
//    self to the pool via pool.Return(gameObject).
//
//  LaunchToCorner(fromWorldPos, targetRect)
//    Used by Beat1_3JuiceDirector for the score-bubble arc. Tweens from the
//    tile's screen position to a UI RectTransform corner target.
//
//  ARCHITECTURAL LAWS
//  ─────────────────────────────────────────────────────────────────────────────
//  Law 3 — Loud-fail on null serialized refs. Any null ref that would break
//           motion is logged at Debug.LogError level and the tween is skipped.
//
//  DOES NOT subscribe to any manager events directly — it is a dumb visual
//  widget, driven entirely by DuskBubblePool (Law 2 hook lives there).
// =============================================================================

namespace Habitales.Onboarding
{
    /// <summary>
    /// Pooled score-juice bubble. Configure visual refs in the Inspector on the
    /// prefab; DuskBubblePool handles instantiation and lifetime.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class DuskBubble : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Motion")]
        [Tooltip("World-units (or UI pixels) to float upward during the animation.")]
        [SerializeField] private float floatDistance = 1.4f;

        [Tooltip("Total animation duration in seconds.")]
        [SerializeField] private float animDuration  = 0.9f;

        [Tooltip("Delay before the fade-out begins (fraction of animDuration). " +
                 "e.g. 0.55 = hold opaque for 55% then fade the remaining 45%.")]
        [SerializeField] [Range(0f, 0.95f)] private float fadeDelay = 0.55f;

        [Header("Optional label")]
        [Tooltip("Optional TextMeshProUGUI child. If assigned, SetLabel() drives its text.")]
        [SerializeField] private TextMeshProUGUI label;

        [Header("Optional icon")]
        [Tooltip("Optional Image child for a small icon or sprite.")]
        [SerializeField] private Image icon;

        // ── Runtime refs ──────────────────────────────────────────────────────

        private CanvasGroup _canvasGroup;
        private DuskBubblePool _pool;          // non-null when in pooled mode
        private Camera _mainCamera;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _mainCamera  = Camera.main;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Sets the label text. Safe to call before Play* — no-op if label is null.
        /// </summary>
        public void SetLabel(string text)
        {
            if (label != null) label.text = text;
        }

        /// <summary>
        /// Sets the icon sprite. Safe to call before Play* — no-op if icon is null.
        /// </summary>
        public void SetIcon(Sprite sprite)
        {
            if (icon != null) icon.sprite = sprite;
        }

        /// <summary>
        /// Pooled path. Called by <see cref="DuskBubblePool.Spawn"/>.
        /// Positions the bubble at <paramref name="worldPos"/> and plays float-fade.
        /// On complete, returns self to the pool.
        /// </summary>
        public void PlayPooled(Vector3 worldPos, DuskBubblePool pool)
        {
            _pool = pool;
            ResetState();
            PositionAtWorld(worldPos);
            PlayFloatFade(OnPooledComplete);
        }

        /// <summary>
        /// One-shot path (not pooled). Spawns at <paramref name="worldPos"/>,
        /// plays float-fade for <paramref name="duration"/> seconds, then destroys
        /// this GameObject.
        /// </summary>
        public void PlayOneShot(Vector3 worldPos, float duration)
        {
            _pool = null;
            ResetState();
            PositionAtWorld(worldPos);
            PlayFloatFade(OnOneShotComplete, duration);
        }

        /// <summary>
        /// Arcs from the tile's world position to a UI RectTransform corner.
        /// Used for the Beat-1.3 score-bubble sequence. Not pooled — caller is
        /// responsible for lifetime (or the bubble self-destructs on complete).
        /// </summary>
        public void LaunchToCorner(Vector3 fromWorldPos, RectTransform target)
        {
            if (target == null)
            {
                Debug.LogError($"DuskBubble.LaunchToCorner: target RectTransform is null — " +
                               "score bubble will not arc. Assign scoreBubbleTarget in the Inspector.", this);
                Destroy(gameObject);
                return;
            }

            _pool = null;
            ResetState();
            PositionAtWorld(fromWorldPos);

            // Convert screen target to world position for the arc end-point.
            Canvas canvas = GetComponentInParent<Canvas>();
            Vector3 endPos;
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                // UI overlay — stay in screen-space (rect anchored position).
                endPos = target.position;
            }
            else
            {
                // World-space canvas or world-space fallback.
                endPos = target.position;
            }

            float arcDur = animDuration;

            // Arc tween: ease out quad position + simultaneous alpha hold then fade.
            LeanTween.move(gameObject, endPos, arcDur)
                .setEaseOutQuad();

            // Scale punch on arrival (feel).
            LeanTween.scale(gameObject, Vector3.one * 1.25f, arcDur * 0.2f)
                .setDelay(arcDur * 0.8f)
                .setEaseOutBack()
                .setOnComplete(() =>
                {
                    LeanTween.scale(gameObject, Vector3.one, arcDur * 0.15f).setEaseInQuad();
                });

            // Fade out near arrival.
            float fadeDur = arcDur * 0.35f;
            LeanTween.alphaCanvas(_canvasGroup, 0f, fadeDur)
                .setDelay(arcDur * 0.65f)
                .setOnComplete(() => Destroy(gameObject));
        }

        // ── Private helpers ────────────────────────────────────────────────────

        void ResetState()
        {
            LeanTween.cancel(gameObject);
            _canvasGroup.alpha = 1f;
            transform.localScale = Vector3.one;
        }

        void PositionAtWorld(Vector3 worldPos)
        {
            // If this bubble lives on a World-Space canvas or as a 3D object, use worldPos directly.
            // If it lives on a Screen-Space canvas, convert via camera.
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                Camera cam = _mainCamera != null ? _mainCamera : Camera.main;
                if (cam != null)
                {
                    Vector2 screenPoint = cam.WorldToScreenPoint(worldPos);
                    transform.position  = screenPoint;
                }
                return;
            }

            // World-space canvas or 3D — place directly.
            transform.position = worldPos;
        }

        void PlayFloatFade(Action onComplete, float? durationOverride = null)
        {
            float dur      = durationOverride.HasValue ? durationOverride.Value : animDuration;
            float fadeDur  = dur * (1f - fadeDelay);
            float fadeStart = dur * fadeDelay;

            Vector3 endPos = transform.position + new Vector3(0f, floatDistance, 0f);

            LeanTween.move(gameObject, endPos, dur)
                .setEaseOutQuad();

            LeanTween.alphaCanvas(_canvasGroup, 0f, fadeDur)
                .setDelay(fadeStart)
                .setOnComplete(onComplete);
        }

        void OnPooledComplete()
        {
            if (_pool != null)
                _pool.Return(gameObject);
            else
                gameObject.SetActive(false);
        }

        void OnOneShotComplete() => Destroy(gameObject);
    }
}
