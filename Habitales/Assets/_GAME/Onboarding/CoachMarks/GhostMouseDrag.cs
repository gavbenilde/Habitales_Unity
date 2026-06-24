using UnityEngine;
using UnityEngine.UI;

namespace Habitales.Onboarding
{
    // =========================================================================
    //  GhostMouseDrag — CoachMarkKind.GhostMouseDrag
    //
    //  A ghost cursor that animates a hold-drag: press at worldTarget (start),
    //  drag to a derived endpoint, release, loop.
    //
    //  The drag end-point is computed from worldTarget plus a configurable
    //  screen-space drag vector — the director passes just worldTarget (the tile
    //  to start on). The drag direction/length is serialized on this component so
    //  the designer can tune without code.
    //
    //  NOTE: B4 (drag ghost inset) owns the corner-inset version with step-by-step
    //  tile ghost swaps. This component is the generic overlay drag — a straight
    //  line over real tiles, no tile-shape ghosts.
    //
    //  Animation loop:
    //    1. Appear at start position (canvas offset from worldTarget screen pos).
    //    2. Swap to pressed sprite + scale squish → hold for pressHold seconds.
    //    3. Glide from start to end over dragTime.
    //    4. Release sprite, scale back.
    //    5. Fade out → pause → restart.
    //
    //  Inspector wiring:
    //    cursorRect      — RectTransform of the cursor Image.
    //    cursorImage     — Image for the cursor.
    //    canvasRect      — Canvas root RectTransform.
    //    idleSprite      — default cursor sprite.
    //    pressedSprite   — sprite during drag (optional; falls back to idle).
    //    dragVectorPx    — canvas-space drag vector from start to end (default 120, 0).
    //    dragTime        — time to complete the drag (default 0.7 s).
    //    pressHoldTime   — pause after press before drag starts (default 0.15 s).
    //    releaseHoldTime — pause after release before fade (default 0.3 s).
    //    loopPause       — pause before cycle restarts (default 0.6 s).
    // =========================================================================

    /// <summary>
    /// Ghost cursor that performs a hold-drag animation over real world tiles.
    /// Direction and length are serialized; start is the director's worldTarget.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class GhostMouseDrag : CoachMarkWidget
    {
        [Header("Refs")]
        [SerializeField] private RectTransform cursorRect;
        [SerializeField] private Image         cursorImage;
        [SerializeField] private RectTransform canvasRect;

        [Header("Sprites")]
        [SerializeField] private Sprite idleSprite;
        [SerializeField] private Sprite pressedSprite;   // optional

        [Header("Drag tuning")]
        [Tooltip("Canvas-space pixel vector from drag start to drag end. Positive X = right; negative Y = down.")]
        [SerializeField] private Vector2 dragVectorPx    = new Vector2(120f, 0f);
        [Tooltip("Small nudge out of the live cursor before gliding to the tile (the 'emerge' hop).")]
        [SerializeField] private Vector2 emergeOffsetPx  = new Vector2(40f, 40f);
        [Tooltip("Time for the fade-in + emerge hop out of the cursor.")]
        [SerializeField] private float   emergeTime      = 0.15f;
        [Tooltip("Time to glide from the near-cursor spot to the drag-start tile.")]
        [SerializeField] private float   approachTime    = 0.4f;
        [SerializeField] private float   dragTime        = 0.7f;
        [SerializeField] private float   pressHoldTime   = 0.15f;
        [SerializeField] private float   releaseHoldTime = 0.3f;
        [SerializeField] private float   loopPause       = 0.6f;

        private bool   _refsOk;
        private bool   _running;
        private Canvas _canvas;

        // ── Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            _refsOk = cursorRect != null && cursorImage != null && canvasRect != null;
            if (!_refsOk)
            {
                Debug.LogError($"{name}: GhostMouseDrag is missing serialized refs — " +
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
        }

        // ── Animation sequence ─────────────────────────────────────────────

        private void PlayCycle()
        {
            if (!_running || !gameObject.activeSelf) return;
            if (ScreenCamera == null) return;

            // Work in screen pixels (overlay canvas) — anchor/parent independent placement.
            float sf = _canvas != null ? _canvas.scaleFactor : 1f;

            Vector2 startScreen = CurrentScreenPos();              // drag-start tile
            Vector2 endScreen   = startScreen + dragVectorPx * sf;

            // Phase 1: emerge at the player's live cursor (fall back to the tile if no mouse).
            Vector3 mouse = Input.mousePosition;
            Vector2 cursorStart = mouse.sqrMagnitude > 0.01f
                ? (Vector2)mouse
                : startScreen;
            Vector2 nearStart = cursorStart + emergeOffsetPx * sf;   // pop out next to the cursor

            // Reset.
            SetSprite(idleSprite);
            SetAlpha(0f);
            cursorRect.localScale = Vector3.one;
            SetScreenPos(cursorRect, cursorStart);

            // Fade in at the cursor.
            LeanTween.value(gameObject, SetAlpha, 0f, 1f, emergeTime)
                .setEaseOutCubic()
                .setIgnoreTimeScale(true);

            // Phase 2: emerge to a near-cursor spot, then Phase 3: glide to the drag-start tile.
            MoveToScreen(cursorRect, nearStart, emergeTime)
                .setEaseOutCubic()
                .setIgnoreTimeScale(true)
                .setOnComplete(() =>
                {
                    if (!_running) return;
                    MoveToScreen(cursorRect, startScreen, approachTime)
                        .setEaseInOutCubic()
                        .setIgnoreTimeScale(true)
                        .setOnComplete(() => OnApproached(startScreen, endScreen));
                });
        }

        private void OnApproached(Vector2 startScreen, Vector2 endScreen)
        {
            if (!_running) return;

            // Press: sprite swap + squish.
            SetSprite(pressedSprite != null ? pressedSprite : idleSprite);
            LeanTween.scale(cursorRect, Vector3.one * 0.85f, 0.1f)
                .setEaseInCubic()
                .setIgnoreTimeScale(true)
                .setDelay(pressHoldTime)
                .setOnComplete(() => OnPressHeld(startScreen, endScreen));
        }

        private void OnPressHeld(Vector2 startScreen, Vector2 endScreen)
        {
            if (!_running) return;

            // Drag glide.
            MoveToScreen(cursorRect, endScreen, dragTime)
                .setEaseInOutCubic()
                .setIgnoreTimeScale(true)
                .setOnComplete(OnDragComplete);
        }

        private void OnDragComplete()
        {
            if (!_running) return;

            // Release.
            SetSprite(idleSprite);
            LeanTween.scale(cursorRect, Vector3.one, 0.1f)
                .setEaseOutCubic()
                .setIgnoreTimeScale(true)
                .setOnComplete(() =>
                {
                    if (!_running) return;
                    // Fade out after releaseHoldTime, then loop.
                    LeanTween.value(gameObject, SetAlpha, 1f, 0f, 0.2f)
                        .setDelay(releaseHoldTime)
                        .setEaseInCubic()
                        .setIgnoreTimeScale(true)
                        .setOnComplete(() =>
                        {
                            if (_running)
                                LeanTween.delayedCall(gameObject, loopPause, PlayCycle)
                                    .setIgnoreTimeScale(true);
                        });
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
