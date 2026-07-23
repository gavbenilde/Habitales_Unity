using UnityEngine;
using UnityEngine.UI;

namespace Habitales.Onboarding
{
    // =========================================================================
    //  GhostMouseClick — CoachMarkKind.GhostMouseClick
    //
    //  A ghost cursor sprite that glides to the world-space target and then
    //  performs a single LMB-press animation (scale squish + sprite swap) before
    //  repeating the approach loop.
    //
    //  Animation loop:
    //    1. Fade/slide cursor in from a resting offset.
    //    2. Move to target position over moveTime.
    //    3. Swap to pressed sprite + scale-squish (pressTime).
    //    4. Swap back to idle sprite (releaseTime).
    //    5. Pause (holdTime), then restart.
    //
    //  Canvas: Screen Space – Overlay (same layer canvas).
    //
    //  Inspector wiring:
    //    cursorRect     — RectTransform of the cursor Image.
    //    cursorImage    — Image for the cursor.
    //    canvasRect     — Canvas root RectTransform.
    //    idleSprite     — default cursor sprite.
    //    pressedSprite  — sprite swapped in during the click (optional; falls back to idle).
    //    approachOffset — pixel offset from target where approach starts (default 80, 80).
    //    moveTime       — time to glide to target (default 0.4 s).
    //    pressTime      — time for squish animation (default 0.12 s).
    //    holdTime       — pause between click cycles (default 0.8 s).
    // =========================================================================

    /// <summary>
    /// Ghost cursor that glides to a world-space target and performs an LMB click animation.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class GhostMouseClick : CoachMarkWidget
    {
        [Header("Refs")]
        [SerializeField] private RectTransform cursorRect;
        [SerializeField] private Image         cursorImage;
        [SerializeField] private RectTransform canvasRect;

        [Header("Sprites")]
        [SerializeField] private Sprite idleSprite;
        [SerializeField] private Sprite pressedSprite;   // optional

        [Header("Tuning")]
        [Tooltip("Canvas-space pixel offset from target where the cursor appears before approaching.")]
        [SerializeField] private Vector2 approachOffset = new Vector2(80f, 80f);
        [Tooltip("Small nudge out of the live cursor before flying to the tile (the 'emerge' hop).")]
        [SerializeField] private Vector2 emergeOffsetPx = new Vector2(40f, 40f);
        [Tooltip("Time for the fade-in + emerge hop out of the cursor.")]
        [SerializeField] private float   emergeTime     = 0.15f;
        [SerializeField] private float   moveTime       = 0.4f;
        [SerializeField] private float   pressTime      = 0.12f;
        [SerializeField] private float   holdTime       = 0.8f;
        
        private bool   _running;
        private Canvas _canvas;

        // ── Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            _refsOk = cursorRect != null && cursorImage != null && canvasRect != null;
            if (!_refsOk)
            {
                Debug.LogError($"{name}: GhostMouseClick is missing serialized refs — " +
                               "wire cursorRect / cursorImage / canvasRect in the Inspector.", this);
                enabled = false;
                return;
            }

            _canvas = cursorRect.GetComponentInParent<Canvas>();
        }

        private void OnDisable()
        {
            _running = false;
        }

        // ── CoachMarkWidget ────────────────────────────────────────────────

        protected override void DoShow()
        {
            if (!_refsOk) return;
            _running = true;
            PlayCycle();
        }

        protected override void DoHide()
        {
            _running = false;
            // LeanTween.cancel(gameObject) called by base after DoHide.
        }

        // ── Animation sequence ─────────────────────────────────────────────

        private void PlayCycle()
        {
            if (!_running || !gameObject.activeSelf) return;
            if (ScreenCamera == null) return;

            // Work entirely in screen pixels (overlay canvas) so placement is anchor/parent
            // independent — anchoredPosition only lands right for a centered direct child.
            float sf = _canvas != null ? _canvas.scaleFactor : 1f;

            Vector2 targetScreen = CurrentScreenPos();

            // Phase 1: emerge AT the player's live cursor (fallback to a fixed offset if no mouse).
            Vector3 mouse = Input.mousePosition;
            Vector2 cursorScreen = mouse.sqrMagnitude > 0.01f
                ? (Vector2)mouse
                : targetScreen + approachOffset * sf;
            Vector2 nearScreen = cursorScreen + emergeOffsetPx * sf;   // pop out next to the cursor

            // Reset to idle sprite and place at the cursor.
            SetSprite(idleSprite);
            SetAlpha(0f);
            cursorRect.localScale = Vector3.one;
            SetScreenPos(cursorRect, cursorScreen);

            // Fade in at the cursor.
            LeanTween.value(gameObject, SetAlpha, 0f, 1f, emergeTime)
                .setEaseOutCubic()
                .setIgnoreTimeScale(true);

            // Phase 2: emerge out to a near-cursor spot, then Phase 3: glide to the tile.
            MoveToScreen(cursorRect, nearScreen, emergeTime)
                .setEaseOutCubic()
                .setIgnoreTimeScale(true)
                .setOnComplete(() =>
                {
                    if (!_running) return;
                    MoveToScreen(cursorRect, targetScreen, moveTime)
                        .setEaseInOutCubic()
                        .setIgnoreTimeScale(true)
                        .setOnComplete(OnArrivedAtTarget);
                });
        }

        private void OnArrivedAtTarget()
        {
            if (!_running) return;

            // Step 3: press squish + sprite swap.
            SetSprite(pressedSprite != null ? pressedSprite : idleSprite);
            LeanTween.scale(cursorRect, Vector3.one * 0.80f, pressTime)
                .setEaseInCubic()
                .setIgnoreTimeScale(true)
                .setOnComplete(OnPressComplete);
        }

        private void OnPressComplete()
        {
            if (!_running) return;

            // Step 4: release — scale back to 1, swap to idle.
            LeanTween.scale(cursorRect, Vector3.one, pressTime)
                .setEaseOutCubic()
                .setIgnoreTimeScale(true)
                .setOnComplete(OnReleaseComplete);
        }

        private void OnReleaseComplete()
        {
            if (!_running) return;
            SetSprite(idleSprite);

            // Step 5: fade out, then restart cycle.
            LeanTween.value(gameObject, SetAlpha, 1f, 0f, pressTime)
                .setEaseInCubic()
                .setIgnoreTimeScale(true)
                .setDelay(holdTime)
                .setOnComplete(() =>
                {
                    if (_running) PlayCycle();
                });
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private void SetAlpha(float a)
        {
            if (cursorImage != null)
            {
                Color c = cursorImage.color;
                c.a = a;
                cursorImage.color = c;
            }
        }

        private void SetSprite(Sprite s)
        {
            if (cursorImage != null && s != null)
                cursorImage.sprite = s;
        }
    }
}
