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
    /// Subscribes to OnboardingDirector.OnBeatEntered / OnBeatCompleted for beat 2.1.
    /// </summary>
    public class DragGhostInset : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        // Inspector
        // ─────────────────────────────────────────────────────────────────────

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

        // ─────────────────────────────────────────────────────────────────────
        // Runtime state
        // ─────────────────────────────────────────────────────────────────────

        private Coroutine _loopCoroutine;

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
        }

        void OnDestroy()
        {
            UnsubscribeFromBeatEvents();
            if (_loopCoroutine != null)
            {
                StopCoroutine(_loopCoroutine);
                _loopCoroutine = null;
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

        // ─────────────────────────────────────────────────────────────────────
        // Beat event subscription
        // ─────────────────────────────────────────────────────────────────────

        void SubscribeToBeatEvents()
        {
            var director = OnboardingDirector.Instance;
            if (director == null)
            {
                Debug.LogWarning($"{name}: OnboardingDirector.Instance is null at OnEnable. Beat events will not fire.", this);
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
        // Beat event handlers
        // ─────────────────────────────────────────────────────────────────────

        void HandleBeatEntered(OnboardingBeatId beat)
        {
            if (beat != OnboardingBeatId.Beat_Drag_Select) return;

            SetInsetVisible(true);
            if (_loopCoroutine != null)
                StopCoroutine(_loopCoroutine);
            _loopCoroutine = StartCoroutine(DragLoopCoroutine());
        }

        void HandleBeatCompleted(OnboardingBeatId beat)
        {
            if (beat != OnboardingBeatId.Beat_Drag_Select) return;

            if (_loopCoroutine != null)
            {
                StopCoroutine(_loopCoroutine);
                _loopCoroutine = null;
            }
            SetInsetVisible(false);
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
