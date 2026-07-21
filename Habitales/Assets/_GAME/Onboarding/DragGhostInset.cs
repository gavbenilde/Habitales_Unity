using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Habitales.Onboarding
{
    /// <summary>
    /// Work-Order B4 — beat-2.1 drag ghost-inset.
    /// A small UI panel in a screen corner that replays a drag demo on a loop:
    /// a ghost-mouse sprite tweens horizontally; a "pressed" sprite swaps in at drag-start;
    /// 3–4 ghost tile sprites swap their shape sprite as the cursor x passes each tile.
    ///
    /// Subscribes to OnboardingDirector.OnBeatEntered / OnBeatCompleted for phase 6
    /// (Phase_06_SelectTiles — the hold-drag multi-select teaching phase).
    ///
    /// Rule of Three:
    ///   - Only starts the demo when 3+ valid (unoccupied) target tiles exist in the world.
    ///   - Replays for up to 3 completed actions during the beat, then fades out permanently.
    ///   "Valid target tile" is defined as a tile with entity == null, matching the wall check
    ///   in TileSelector.EnterFloodFillMode. TileManager.GetAllTiles() is the source of truth.
    ///   "Drag-eligible completed action" is any ActionManager.OnActionCompleted fired while
    ///   the drag beat is active — OnActionCompleted does not carry the action type, so we count
    ///   all completions during the beat. This is conservative; in practice the beat only runs
    ///   while the player is doing drag (FloodFill) actions.
    /// </summary>
    public class DragGhostInset : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────────────────────────────

        [Header("Dormancy")]
        [Tooltip("DORMANT by default — this onboarding-juice demo is parked pending a design decision " +
                 "(ONBOARDING_HANDOFF §3). While true the component disables itself in Awake and never " +
                 "subscribes to beat/action events. Flip false to revive it (re-points to phase 6).")]
        [SerializeField] private bool dormant = true;

        [Header("UI References")]
        [Tooltip("The corner panel GameObject, hidden by default.")]
        [SerializeField] private GameObject insetRoot;

        [Tooltip("RectTransform for the ghost cursor sprite.")]
        [SerializeField] private RectTransform ghostCursor;

        [Tooltip("Image component for the ghost cursor.")]
        [SerializeField] private Image cursorImage;

        [Header("Sprites")]
        [Tooltip("Idle mouse cursor sprite.")]
        [SerializeField] private Sprite cursorIdle;

        [Tooltip("Pressed/held mouse cursor sprite.")]
        [SerializeField] private Sprite cursorPressed;

        [Tooltip("Empty tile sprite (initial state).")]
        [SerializeField] private Sprite tileEmpty;

        [Tooltip("Filled tile sprite (when cursor passes).")]
        [SerializeField] private Sprite tileFilled;

        [Header("Ghost Tiles")]
        [Tooltip("3–4 Image components for the ghost tiles in left-to-right order.")]
        [SerializeField] private Image[] ghostTiles = new Image[3];

        [Header("Animation Tuning")]
        [Tooltip("Duration of a single drag motion in seconds.")]
        [SerializeField] private float dragDuration = 1.2f;

        [Tooltip("Start position (local) of the drag in screen space.")]
        [SerializeField] private Vector2 dragStartPos = Vector2.zero;

        [Tooltip("End position (local) of the drag in screen space.")]
        [SerializeField] private Vector2 dragEndPos = new Vector2(200f, 0f);

        [Tooltip("Pause between drag loops in seconds.")]
        [SerializeField] private float loopPauseSeconds = 0.6f;

        [Header("Rule of Three")]
        [Tooltip("Minimum number of unoccupied tiles required before the demo is shown. Default: 3.")]
        [SerializeField] private int validTileThreshold = 3;

        [Tooltip("How many drag-eligible completed actions the demo replays for before fading permanently. Default: 3.")]
        [SerializeField] private int maxReplays = 3;

        [Tooltip("Duration of the fade-out after the replay budget is exhausted (seconds).")]
        [SerializeField] private float fadeOutDuration = 0.5f;

        // ─────────────────────────────────────────────────────────────────────
        // Runtime state
        // ─────────────────────────────────────────────────────────────────────

        private Coroutine _loopCoroutine;
        private Coroutine _fadeCoroutine;

        // Remaining replay budget (counts down from maxReplays on each completed action).
        // When it reaches 0 the demo fades out and unsubscribes from ActionManager permanently.
        private int _replaysRemaining;

        // True once the replay budget is exhausted — prevents the demo from ever showing again.
        private bool _exhausted = false;

        // True while we are inside Phase_06_SelectTiles (between OnBeatEntered and OnBeatCompleted).
        private bool _beatActive = false;

        // CanvasGroup used to show/hide the inset WITHOUT deactivating the GameObject.
        // The script (controller) and insetRoot may be the same GameObject in the scene; calling
        // insetRoot.SetActive(false) there would disable the controller and kill its subscription.
        // Driving alpha instead keeps the controller alive regardless of how insetRoot is wired.
        private CanvasGroup _insetGroup;

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        void Awake()
        {
            if (dormant) { enabled = false; return; }   // parked — see the `dormant` tooltip

            // Loud-fail on missing refs.
            bool ok = true;

            if (insetRoot == null)
            {
                Debug.LogError($"{name}: insetRoot is not assigned — cannot show the drag demo. Wire it in the Inspector.", this);
                ok = false;
            }

            if (ghostCursor == null)
            {
                Debug.LogError($"{name}: ghostCursor is not assigned — cannot animate the drag. Wire it in the Inspector.", this);
                ok = false;
            }

            if (cursorImage == null)
            {
                Debug.LogError($"{name}: cursorImage is not assigned — cannot swap cursor sprites. Wire it in the Inspector.", this);
                ok = false;
            }

            if (cursorIdle == null || cursorPressed == null)
            {
                Debug.LogError($"{name}: cursorIdle or cursorPressed sprite is not assigned. Wire both in the Inspector.", this);
                ok = false;
            }

            if (tileEmpty == null || tileFilled == null)
            {
                Debug.LogError($"{name}: tileEmpty or tileFilled sprite is not assigned. Wire both in the Inspector.", this);
                ok = false;
            }

            if (ghostTiles == null || ghostTiles.Length == 0)
            {
                Debug.LogError($"{name}: ghostTiles array is empty or not assigned. Wire 3–4 Image refs in the Inspector.", this);
                ok = false;
            }
            else
            {
                foreach (var tile in ghostTiles)
                {
                    if (tile == null)
                    {
                        Debug.LogError($"{name}: One or more ghostTiles are null. Ensure all 3–4 slots are wired.", this);
                        ok = false;
                        break;
                    }
                }
            }

            if (!ok) { enabled = false; return; }

            // Initialise replay budget.
            _replaysRemaining = maxReplays;

            // Get-or-add the CanvasGroup we drive for visibility, and start hidden.
            _insetGroup = insetRoot.GetComponent<CanvasGroup>();
            if (_insetGroup == null)
                _insetGroup = insetRoot.AddComponent<CanvasGroup>();
            SetInsetVisible(false);
        }

        // Subscribe in Start (not OnEnable): OnboardingDirector sets its singleton in Awake at
        // execution order 50 — by Start it is guaranteed live, so we never miss the subscription.
        void Start()
        {
            SubscribeToBeatEvents();
            SubscribeToActionManager();
        }

        void OnDestroy()
        {
            UnsubscribeFromBeatEvents();
            UnsubscribeFromActionManager();
            if (_loopCoroutine != null)
            {
                StopCoroutine(_loopCoroutine);
                _loopCoroutine = null;
            }
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }
        }

        // Toggle inset visibility without deactivating the GameObject (see _insetGroup note).
        void SetInsetVisible(bool visible)
        {
            if (_insetGroup == null) return;
            _insetGroup.alpha          = visible ? 1f : 0f;
            _insetGroup.interactable   = visible;
            _insetGroup.blocksRaycasts = visible;
        }

        // Fades alpha from current value to 0 over fadeOutDuration, then hides the panel.
        IEnumerator FadeOutCoroutine()
        {
            if (_insetGroup == null) yield break;

            float startAlpha = _insetGroup.alpha;
            float elapsed    = 0f;

            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.deltaTime;
                _insetGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / fadeOutDuration);
                yield return null;
            }

            SetInsetVisible(false);
            _fadeCoroutine = null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Beat event subscription
        // ─────────────────────────────────────────────────────────────────────

        void SubscribeToBeatEvents()
        {
            var director = OnboardingDirector.Instance;
            if (director == null)
            {
                Debug.LogWarning($"{name}: OnboardingDirector.Instance is null at Start. Beat events will not fire.", this);
                return;
            }

            director.OnBeatEntered  += HandleBeatEntered;
            director.OnBeatCompleted += HandleBeatCompleted;
        }

        void UnsubscribeFromBeatEvents()
        {
            var director = OnboardingDirector.Instance;
            if (director == null) return;

            director.OnBeatEntered  -= HandleBeatEntered;
            director.OnBeatCompleted -= HandleBeatCompleted;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ActionManager subscription (Rule of Three replay counter)
        // ─────────────────────────────────────────────────────────────────────

        void SubscribeToActionManager()
        {
            var am = ActionManager.Instance;
            if (am == null)
            {
                Debug.LogWarning($"{name}: ActionManager.Instance is null at Start. Replay counter will not decrement.", this);
                return;
            }
            am.OnActionCompleted += HandleActionCompleted;
        }

        void UnsubscribeFromActionManager()
        {
            var am = ActionManager.Instance;
            if (am == null) return;
            am.OnActionCompleted -= HandleActionCompleted;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Beat event handlers
        // ─────────────────────────────────────────────────────────────────────

        void HandleBeatEntered(OnboardingBeatId beat)
        {
            if (beat != OnboardingBeatId.Phase_06_SelectTiles) return;

            _beatActive = true;

            // Rule of Three gate (a): do not show if budget exhausted or threshold not met.
            if (_exhausted) return;
            TryShowDemo();
        }

        void HandleBeatCompleted(OnboardingBeatId beat)
        {
            if (beat != OnboardingBeatId.Phase_06_SelectTiles) return;

            _beatActive = false;
            StopDemoLoop();
            SetInsetVisible(false);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Rule of Three helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the count of unoccupied (entity == null) tiles in the world.
        /// Source: TileManager.GetAllTiles() — same wall definition used by
        /// TileSelector.EnterFloodFillMode (tile.entity != null skips a tile).
        /// </summary>
        int CountValidTargetTiles()
        {
            var tm = TileManager.Instance;
            if (tm == null) return 0;

            int count = 0;
            foreach (var tile in tm.GetAllTiles())
            {
                if (tile.entity == null)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Shows the demo loop if the tile threshold is met; otherwise silently skips.
        /// </summary>
        void TryShowDemo()
        {
            int validTiles = CountValidTargetTiles();
            if (validTiles < validTileThreshold)
            {
                // Threshold not met: do not start the demo for this beat entry.
                // When ActionManager.OnActionCompleted fires, HandleActionCompleted
                // calls TryShowDemo again so we catch the first qualifying moment.
                return;
            }

            // Stop any in-flight fade so alpha is restored immediately.
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }

            SetInsetVisible(true);
            if (_loopCoroutine != null)
                StopCoroutine(_loopCoroutine);
            _loopCoroutine = StartCoroutine(DragLoopCoroutine());
        }

        void StopDemoLoop()
        {
            if (_loopCoroutine != null)
            {
                StopCoroutine(_loopCoroutine);
                _loopCoroutine = null;
            }
        }

        /// <summary>
        /// Rule of Three gate (b): each completed action during the drag beat decrements the
        /// replay budget. When the budget reaches 0 the demo fades out and we permanently
        /// unsubscribe from ActionManager so it never shows again this run.
        ///
        /// Assumption: every OnActionCompleted fired while _beatActive is treated as a
        /// drag-eligible action. OnActionCompleted does not carry the action type, so we
        /// cannot filter by SelectionMode.FloodFill here. In practice Phase_06_SelectTiles
        /// is only live while the player is executing drag (FloodFill) actions.
        /// </summary>
        void HandleActionCompleted(Tile _, int __)
        {
            if (_exhausted) return;

            if (!_beatActive)
            {
                // Beat not active yet — check if the threshold is now met so the demo
                // can start when the beat eventually fires.  (No-op if beat hasn't entered.)
                return;
            }

            // If the demo is not yet showing (threshold was too low at beat entry), try now
            // — the completed action may have freed tiles so the count might qualify.
            if (_loopCoroutine == null && _insetGroup != null && _insetGroup.alpha < 0.5f)
                TryShowDemo();

            // Decrement the replay budget.
            _replaysRemaining--;
            if (_replaysRemaining <= 0)
            {
                _exhausted = true;
                StopDemoLoop();

                // Fade out, then stay hidden forever.
                if (_fadeCoroutine != null)
                    StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = StartCoroutine(FadeOutCoroutine());

                // Unsubscribe permanently — no more decrement attempts.
                UnsubscribeFromActionManager();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Animation loop
        // ─────────────────────────────────────────────────────────────────────

        IEnumerator DragLoopCoroutine()
        {
            while (true)
            {
                // Reset: cursor at idle sprite, start position.
                cursorImage.sprite = cursorIdle;
                ghostCursor.anchoredPosition = dragStartPos;

                // Reset all tiles to empty.
                foreach (var tile in ghostTiles)
                    tile.sprite = tileEmpty;

                // Swap cursor to pressed.
                cursorImage.sprite = cursorPressed;

                // Tween the cursor from start to end, tracking x-position for tile swaps.
                float elapsedTime = 0f;
                while (elapsedTime < dragDuration)
                {
                    elapsedTime += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsedTime / dragDuration);

                    // Lerp x position; keep y constant.
                    float newX = Mathf.Lerp(dragStartPos.x, dragEndPos.x, t);
                    ghostCursor.anchoredPosition = new Vector2(newX, dragStartPos.y);

                    // Check each tile: swap to filled if cursor has passed its x.
                    for (int i = 0; i < ghostTiles.Length; i++)
                    {
                        float tileX = ghostTiles[i].rectTransform.anchoredPosition.x;
                        if (newX >= tileX)
                            ghostTiles[i].sprite = tileFilled;
                    }

                    yield return null;
                }

                // Ensure final position and all tiles filled.
                ghostCursor.anchoredPosition = dragEndPos;
                foreach (var tile in ghostTiles)
                    tile.sprite = tileFilled;

                // Swap cursor back to idle.
                cursorImage.sprite = cursorIdle;

                // Pause before next loop.
                yield return new WaitForSeconds(loopPauseSeconds);
            }
        }
    }
}
