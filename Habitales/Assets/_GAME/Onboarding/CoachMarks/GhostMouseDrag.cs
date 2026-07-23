using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Habitales.Onboarding
{
    // =========================================================================
    //  GhostMouseDrag — CoachMarkKind.GhostMouseDrag
    //
    //  A ghost cursor that animates a hold-drag: press on the tile closest to the
    //  player's mouse, drag to a random existing tile nearby, release, loop.
    //
    //  Endpoints are resolved from the live tile grid each cycle (TileManager):
    //    • start = the existing tile whose screen position is closest to the mouse.
    //    • end   = a random OTHER existing tile within maxTileDistance grid steps.
    //  If there's no TileManager / no tiles (e.g. test scenes) it falls back to the
    //  legacy worldTarget + serialized dragVectorPx so it still animates.
    //
    //  NOTE: B4 (drag ghost inset) owns the corner-inset version with step-by-step
    //  tile ghost swaps. This component is the generic overlay drag — a straight
    //  line over real tiles, no tile-shape ghosts.
    //
    //  Animation loop:
    //    1. Emerge at the player's live cursor, glide to the drag-start tile.
    //    2. Swap to the Pressing sprite + scale squish → hold for pressHold seconds.
    //    3. Glide from the start tile to the end tile over dragTime.
    //    4. Swap back to the Regular sprite, scale back.
    //    5. Fade out → pause → restart.
    //
    //  Inspector wiring:
    //    cursorRect      — RectTransform of the cursor Image.
    //    cursorImage     — Image for the cursor.
    //    canvasRect      — Canvas root RectTransform.
    //    idleSprite      — Regular Mouse cursor sprite (not pressing).
    //    pressedSprite   — Pressing cursor sprite (shown while held/dragging; falls back to idle).
    //    maxTileDistance — how many grid steps away the random end tile may be (default 5).
    //    dragVectorPx    — FALLBACK drag vector, used only when no tiles are found (default 120, 0).
    //    dragTime        — time to complete the drag (default 0.7 s).
    //    pressHoldTime   — pause after press before drag starts (default 0.15 s).
    //    releaseHoldTime — pause after release before fade (default 0.3 s).
    //    loopPause       — pause before cycle restarts (default 0.6 s).
    // =========================================================================

    /// <summary>
    /// Ghost cursor that performs a hold-drag animation over real world tiles.
    /// Start = the tile closest to the player's mouse; end = a random existing
    /// tile within <see cref="maxTileDistance"/> grid steps of it.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class GhostMouseDrag : CoachMarkWidget
    {
        [Header("Refs")]
        [SerializeField] private RectTransform cursorRect;
        [SerializeField] private Image         cursorImage;
        [SerializeField] private RectTransform canvasRect;

        [Header("Sprites — Regular + Pressing")]
        [Tooltip("Regular Mouse cursor — shown while approaching the tile and after release.")]
        [SerializeField] private Sprite idleSprite;
        [Tooltip("Pressing cursor — shown for the whole held-drag so the press reads clearly. " +
                 "Falls back to the Regular sprite if left unset.")]
        [SerializeField] private Sprite pressedSprite;

        [Header("Tile targeting")]
        [Tooltip("The random drag-end tile is chosen from existing tiles within this many grid " +
                 "steps of the start tile (the tile closest to the mouse). Default 5.")]
        [SerializeField] private int     maxTileDistance = 5;

        [Header("Drag tuning")]
        [Tooltip("FALLBACK canvas-space drag vector, used only when there is no TileManager / no " +
                 "tiles to target. Positive X = right; negative Y = down.")]
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

            // Where is the player's mouse this cycle? It drives both the emerge point and the
            // "closest tile" pick. Fall back to the worldTarget screen pos if there's no mouse.
            Vector3 mouse       = Input.mousePosition;
            bool    haveMouse   = mouse.sqrMagnitude > 0.01f;
            Vector2 mouseScreen = haveMouse ? (Vector2)mouse : CurrentScreenPos();

            // Endpoints: start = the tile closest to the mouse, end = a random nearby existing tile.
            // Falls back to the legacy worldTarget + serialized drag vector in tile-less scenes.
            if (!TryResolveTileDrag(mouseScreen, out Vector2 startScreen, out Vector2 endScreen))
            {
                startScreen = CurrentScreenPos();
                endScreen   = startScreen + dragVectorPx * sf;
            }

            // Phase 1: emerge at the player's live cursor (fall back to the start tile if no mouse).
            Vector2 cursorStart = haveMouse ? (Vector2)mouse : startScreen;
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

        // ── Tile targeting ──────────────────────────────────────────────────

        /// <summary>
        /// Resolves the drag endpoints from the live tile grid:
        ///   • start = the existing tile whose screen position is closest to <paramref name="mouseScreen"/>.
        ///   • end   = a random OTHER existing tile within <see cref="maxTileDistance"/> grid steps of it.
        /// Both are returned as screen-pixel positions. Returns false (outputs untouched) when there's
        /// no <see cref="TileManager"/>, no camera, no tiles, or nothing but the start tile in range —
        /// callers then fall back to the serialized <see cref="dragVectorPx"/>.
        /// </summary>
        private bool TryResolveTileDrag(Vector2 mouseScreen, out Vector2 startScreen, out Vector2 endScreen)
        {
            startScreen = default;
            endScreen   = default;

            TileManager tm = TileManager.Instance;
            if (tm == null || ScreenCamera == null) return false;

            List<Tile> all = tm.GetAllTiles();
            if (all == null || all.Count == 0) return false;

            // Closest existing tile to the mouse, measured in screen space.
            Tile  closest = null;
            float bestSqr = float.PositiveInfinity;
            foreach (Tile t in all)
            {
                if (t == null) continue;
                Vector2 s = ScreenCamera.WorldToScreenPoint(tm.GridToWorldPosition(t.gridPosition));
                float d = (s - mouseScreen).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; closest = t; }
            }
            if (closest == null) return false;

            // A random existing tile within maxTileDistance grid steps, excluding the start tile.
            List<Tile> near = tm.GetTilesInRadius(closest.gridPosition, Mathf.Max(1, maxTileDistance));
            if (near != null) near.RemoveAll(t => t == null || t == closest);
            if (near == null || near.Count == 0) return false;   // degenerate grid — fall back.

            Tile end = near[Random.Range(0, near.Count)];

            startScreen = ScreenCamera.WorldToScreenPoint(tm.GridToWorldPosition(closest.gridPosition));
            endScreen   = ScreenCamera.WorldToScreenPoint(tm.GridToWorldPosition(end.gridPosition));
            return true;
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
