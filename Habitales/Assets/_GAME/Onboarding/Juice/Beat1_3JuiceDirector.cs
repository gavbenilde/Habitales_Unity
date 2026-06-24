using System;
using System.Collections;
using UnityEngine;
using Habitales.Onboarding;

// =============================================================================
//  Beat1_3JuiceDirector — Work-Order B3
//  Namespace: Habitales.Onboarding
//
//  WHAT IT DOES
//  ─────────────────────────────────────────────────────────────────────────────
//  On OnboardingDirector.OnBeatEntered(Beat_1_3_CommitTime), arms a one-shot
//  listener on TileManager.OnEntitySpawned. The FIRST entity spawned after that
//  fires a three-signal causal chain:
//
//    1. Tile shifts colour (warm "commit" flash via TileVisualizer material tween)
//    2. Delta tip pops at the tile world position (world-space TextMesh or Image)
//    3. Score bubble launches to the screen corner (UI Image arc-tween)
//
//  DURING ONBOARDING  (OnboardingDirector.Instance.IsActive == true):
//    Signals are staggered ~250 ms apart so the player reads them as a sentence.
//
//  AFTER ONBOARDING:
//    All three fire at once (no stagger). The same code path is reused after
//    graduation — no branching class, just delay = 0.
//
//  ARCHITECTURAL LAWS
//  ─────────────────────────────────────────────────────────────────────────────
//  Law 1 — Getters not setters: reads OnboardingDirector.IsActive; never writes it.
//  Law 2 — Hooks fire on meaning: subscribes to OnBeatEntered + OnEntitySpawned;
//           never polls per-tick.
//  Law 3 — Loud-fail on null serialized refs: every [SerializeField] null is a
//           Debug.LogError(…, this) followed by enabled = false.
//
//  INSPECTOR WIRING REQUIRED (see bottom of file)
// =============================================================================

namespace Habitales.Onboarding
{
    /// <summary>
    /// Beat-1.3 staggered teaching sequence. Attach to a GameObject alongside
    /// <see cref="OnboardingDirector"/>. Requires three sub-widgets to be assigned
    /// in the Inspector (see bottom of file).
    /// </summary>
    [DefaultExecutionOrder(60)] // after OnboardingDirector (50)
    public class Beat1_3JuiceDirector : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Tile Flash")]
        [Tooltip("Colour the tile briefly flashes to signal 'committed time'.")]
        [SerializeField] private Color commitFlashColor = new Color(1f, 0.85f, 0.3f);  // warm gold
        [SerializeField] private float flashDuration    = 0.35f;

        [Header("Delta Tip")]
        [Tooltip("Prefab that shows a small '+Δ' or stat-delta label at the tile. " +
                 "Must have a DuskBubble or a simple CanvasGroup + Text component on root.")]
        [SerializeField] private GameObject deltaTipPrefab;

        [Tooltip("World-space offset above the tile for the delta tip.")]
        [SerializeField] private Vector3 deltaTipOffset = new Vector3(0f, 1.2f, 0f);

        [Tooltip("How long the delta tip is visible before it fades out.")]
        [SerializeField] private float deltaTipDuration = 1.2f;

        [Header("Score Bubble")]
        [Tooltip("The score bubble widget that will be told to fire. " +
                 "Must have a DuskBubble component — its LaunchToCorner() will be called.")]
        [SerializeField] private DuskBubble scoreBubbleTemplate;

        [Tooltip("Screen-space RectTransform target (e.g. the score counter corner).")]
        [SerializeField] private RectTransform scoreBubbleTarget;

        [Header("Timing")]
        [Tooltip("Gap between each signal during onboarding (seconds). " +
                 "Post-onboarding the gap collapses to zero.")]
        [SerializeField] private float staggerDelay = 0.25f;

        // ── Runtime state ─────────────────────────────────────────────────────

        private bool _armed     = false;   // waiting for a plant to fire the chain
        private bool _firedOnce = false;   // one-shot during the beat

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void Start()
        {
            // Law 3 — loud-fail every required ref.
            bool ok = true;

            if (deltaTipPrefab == null)
            {
                Debug.LogError($"{name}: deltaTipPrefab is not assigned — delta tip will not show. " +
                               "Assign a prefab with a label/CanvasGroup in the Inspector.", this);
                ok = false;
            }

            if (scoreBubbleTemplate == null)
            {
                Debug.LogError($"{name}: scoreBubbleTemplate is not assigned — score bubble will not fire. " +
                               "Assign a DuskBubble in the Inspector.", this);
                ok = false;
            }

            if (scoreBubbleTarget == null)
            {
                Debug.LogError($"{name}: scoreBubbleTarget is not assigned — score bubble has no destination. " +
                               "Assign the corner score RectTransform in the Inspector.", this);
                ok = false;
            }

            if (!ok) { enabled = false; return; }

            // Subscribe to meaning-events (Law 2).
            if (OnboardingDirector.Instance != null)
            {
                OnboardingDirector.Instance.OnBeatEntered     += HandleBeatEntered;
                OnboardingDirector.Instance.OnBeatCompleted   += HandleBeatCompleted;
            }
            else
            {
                // Director may not exist post-onboarding. This component still fires the
                // chain (stagger=0), so not fatal.  Just log once.
                Debug.LogWarning($"{name}: OnboardingDirector.Instance is null — " +
                                 "beat-gating disabled; chain fires on every entity spawn.", this);
                ArmForNextSpawn();  // always armed if no director
            }

            if (TileManager.Instance != null)
                TileManager.Instance.OnEntitySpawned += HandleEntitySpawned;
            else
            {
                Debug.LogError($"{name}: TileManager.Instance is null — 1.3 juice chain will never fire. " +
                               "Ensure TileManager is in the scene.", this);
                enabled = false;
            }
        }

        void OnDestroy()
        {
            if (OnboardingDirector.Instance != null)
            {
                OnboardingDirector.Instance.OnBeatEntered   -= HandleBeatEntered;
                OnboardingDirector.Instance.OnBeatCompleted -= HandleBeatCompleted;
            }

            if (TileManager.Instance != null)
                TileManager.Instance.OnEntitySpawned -= HandleEntitySpawned;
        }

        // ── Meaning-event handlers ─────────────────────────────────────────────

        void HandleBeatEntered(OnboardingBeatId beat)
        {
            if (beat == OnboardingBeatId.Beat_Click_Confirm)
            {
                _firedOnce = false;
                ArmForNextSpawn();
            }
        }

        void HandleBeatCompleted(OnboardingBeatId beat)
        {
            // Disarm if the beat completes without a spawn (edge-case safety).
            if (beat == OnboardingBeatId.Beat_Click_Confirm)
                _armed = false;
        }

        void HandleEntitySpawned(Tile tile, string entityId)
        {
            if (!_armed) return;
            if (_firedOnce) return;  // one-shot within the beat

            _firedOnce = true;
            _armed     = false;

            // Determine stagger: collapse to zero after graduation.
            bool isTeaching = OnboardingDirector.Instance != null &&
                              OnboardingDirector.Instance.IsActive;
            float gap = isTeaching ? staggerDelay : 0f;

            StartCoroutine(FireChain(tile, gap));
        }

        // ── Chain ──────────────────────────────────────────────────────────────

        IEnumerator FireChain(Tile tile, float gap)
        {
            // ── Signal 1: tile colour flash ───────────────────────────────────
            FlashTileColor(tile);

            yield return new WaitForSeconds(gap);

            // ── Signal 2: delta tip at tile ───────────────────────────────────
            // Re-derive world pos now (not at HandleEntitySpawned time) so any
            // camera movement during the stagger gap is already accounted for.
            GameObject tip = SpawnDeltaTip(tile);
            if (tip != null)
                StartCoroutine(TrackTipToTile(tip, tile, deltaTipDuration));

            yield return new WaitForSeconds(gap);

            // ── Signal 3: score bubble arc to corner ──────────────────────────
            // Re-derive world pos fresh again so the bubble arc starts from the
            // tile's current screen position, not a stale snapshot.
            LaunchScoreBubble(tile);
        }

        // ── Signal implementations ─────────────────────────────────────────────

        void FlashTileColor(Tile tile)
        {
            if (TileManager.Instance == null) return;

            GameObject tileGO = TileManager.Instance.GetTileGameObject(tile);
            if (tileGO == null) return;

            TileVisualizer vis = tileGO.GetComponent<TileVisualizer>();
            if (vis == null) return;

            // Flash: tween material _BaseColor to commitFlashColor then back.
            // TileVisualizer uses a material instance with "_BaseColor".
            // We use LeanTween.value to drive the lerp safely.
            Color originalColor = vis.GetBaseColor();
            float halfDur = flashDuration * 0.5f;

            // Use a MaterialPropertyBlock so we write to the renderer without
            // instantiating a per-call material copy (avoids a memory leak).
            MeshRenderer meshRenderer = tileGO.GetComponent<MeshRenderer>();
            if (meshRenderer == null) return;
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();

            // Tween from original → flash colour
            LeanTween.value(tileGO, 0f, 1f, halfDur)
                .setOnUpdate((float t) =>
                {
                    if (meshRenderer == null) return;
                    meshRenderer.GetPropertyBlock(mpb);
                    mpb.SetColor("_BaseColor", Color.Lerp(originalColor, commitFlashColor, t));
                    meshRenderer.SetPropertyBlock(mpb);
                })
                .setOnComplete(() =>
                {
                    // Tween back from flash colour → original
                    LeanTween.value(tileGO, 1f, 0f, halfDur)
                        .setOnUpdate((float t) =>
                        {
                            if (meshRenderer == null) return;
                            meshRenderer.GetPropertyBlock(mpb);
                            mpb.SetColor("_BaseColor", Color.Lerp(originalColor, commitFlashColor, t));
                            meshRenderer.SetPropertyBlock(mpb);
                        });
                });
        }

        // Returns the spawned tip GameObject (or null on failure).
        // Position is intentionally NOT set here — TrackTipToTile drives it
        // per-frame so the tip follows the tile through any camera movement.
        GameObject SpawnDeltaTip(Tile tile)
        {
            if (deltaTipPrefab == null) return null;

            GameObject tip = Instantiate(deltaTipPrefab);

            // Snap to the tile's current screen position immediately so there
            // is no single-frame flash at the origin before the coroutine runs.
            SnapTipToTile(tip, tile, floatProgress: 0f);

            // Drive only alpha/fade here — position is owned by TrackTipToTile.
            // We intentionally do NOT call DuskBubble.PlayOneShot because that
            // method starts its own LeanTween.move, which would fight the
            // per-frame reprojection coroutine.
            CanvasGroup cg = tip.GetComponent<CanvasGroup>();
            if (cg == null) cg = tip.AddComponent<CanvasGroup>();
            cg.alpha = 1f;

            LeanTween.alphaCanvas(cg, 0f, deltaTipDuration * 0.4f)
                .setDelay(deltaTipDuration * 0.6f)
                .setOnComplete(() => { if (tip != null) Destroy(tip); });

            return tip;
        }

        // Re-projects the tile's world position to screen space each frame while
        // the tip is alive, adding a time-proportional float offset so the tip
        // drifts upward relative to the tile rather than drifting with the camera.
        IEnumerator TrackTipToTile(GameObject tip, Tile tile, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration && tip != null)
            {
                SnapTipToTile(tip, tile, floatProgress: elapsed / duration);
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        // Computes the tile's current world position + float-up offset and
        // applies it to the tip transform, re-projecting via WorldToScreenPoint
        // on every call so camera movement is always compensated.
        void SnapTipToTile(GameObject tip, Tile tile, float floatProgress)
        {
            if (tip == null) return;
            if (TileManager.Instance == null) return;

            Vector3 tileWorldPos = TileManager.Instance.GridToWorldPosition(tile.gridPosition)
                                   + deltaTipOffset;

            // Add a smooth upward drift so the tip visually floats above the tile.
            // floatProgress goes 0→1 over deltaTipDuration; ease-out via sqrt.
            float floatY = Mathf.Lerp(0f, 0.8f, Mathf.Sqrt(floatProgress));
            tileWorldPos.y += floatY;

            Canvas canvas = tip.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                Camera cam = Camera.main;
                if (cam != null)
                    tip.transform.position = cam.WorldToScreenPoint(tileWorldPos);
            }
            else
            {
                // World-space canvas or 3D object — place directly.
                tip.transform.position = tileWorldPos;
            }
        }

        void LaunchScoreBubble(Tile tile)
        {
            if (scoreBubbleTemplate == null || scoreBubbleTarget == null) return;
            if (TileManager.Instance == null) return;

            // Re-derive the tile's world position at the exact moment of launch
            // so the bubble arc starts at the tile's current screen location,
            // not a snapshot taken before the stagger delay.
            Vector3 fromWorldPos = TileManager.Instance.GridToWorldPosition(tile.gridPosition)
                                   + deltaTipOffset;
            scoreBubbleTemplate.LaunchToCorner(fromWorldPos, scoreBubbleTarget);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        void ArmForNextSpawn() => _armed = true;
    }
}

// =============================================================================
// INSPECTOR WIRING CHECKLIST
// =============================================================================
//
// On the Beat1_3JuiceDirector component:
//
//   Tile Flash
//   ─────────────────────────────────────────────────────────────────
//   • commitFlashColor   — warm gold (1, 0.85, 0.3) or your brand color
//   • flashDuration      — 0.35 s (edit to taste)
//
//   Delta Tip
//   ─────────────────────────────────────────────────────────────────
//   • deltaTipPrefab     — a prefab with either:
//       (a) a DuskBubble component (preferred — reuses pool logic), or
//       (b) a CanvasGroup + TextMeshProUGUI showing "+Δ soils" or similar
//     Place under Assets/_GAME/Prefabs/Onboarding/DeltaTip.prefab
//   • deltaTipOffset     — (0, 1.2, 0) — adjust if tiles are elevated
//   • deltaTipDuration   — 1.2 s
//
//   Score Bubble
//   ─────────────────────────────────────────────────────────────────
//   • scoreBubbleTemplate — drag the DuskBubble prefab INSTANCE from the
//     scene (or a standalone prefab reference) that has DuskBubble.cs
//   • scoreBubbleTarget   — the RectTransform of the score/day counter
//     UI element in the top corner of your HUD canvas
//
//   Timing
//   ─────────────────────────────────────────────────────────────────
//   • staggerDelay       — 0.25 s (250 ms between signals during onboarding)
//
// =============================================================================
