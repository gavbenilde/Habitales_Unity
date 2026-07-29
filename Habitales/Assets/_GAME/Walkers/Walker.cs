using System.Collections;
using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;

/// <summary>
/// The one authoritative activity state for a walker, shared by every species. Idle/Walking are
/// derived by Walker from measured motion; Flying/Working are declared by WorkerWalker, which is
/// the only thing that knows about them.
///
/// The integer values are the contract with the Animator parameter — keep them stable, and
/// append rather than reorder if this ever grows.
/// </summary>
public enum WalkerState
{
    Idle = 0,
    Walking = 1,
    Flying = 2,   // mid-air on the Working flight arc
    Working = 3,  // landed on the work tile, action in progress
}

/// <summary>
/// One facing's worth of art for a walker, plus the clip names to play on it per WalkerState.
///
/// Two art pipelines are in play and both are supported, because the project genuinely uses both:
/// workers are Spine (`_ART/_Characters/Workers/WorkerAnim`, spine-unity 3.8), animals are
/// spritesheet + Mecanim (`_ART/_Characters/Animals/Carabao/Animations`). Wire whichever one this
/// rig uses and leave the other null — clip names are the common currency, so nothing else in
/// Walker has to care which pipeline it's driving.
/// </summary>
[System.Serializable]
public class WalkerFacingRig
{
    [Tooltip("The GameObject holding this facing's art. Shown/hidden by toggling its Renderers.")]
    public GameObject root;
    [Tooltip("Spine pipeline (workers). Leave null for Mecanim art.")]
    public SkeletonAnimation spine;
    [Tooltip("Mecanim pipeline (animals). Leave null for Spine art. Clip names below are treated as " +
             "Animator STATE names.")]
    public Animator animator;

    // dataField points these dropdowns at this rig's own skeleton, so the inspector lists the real
    // animation names straight out of the SkeletonData instead of asking you to type them from
    // memory. fallbackToTextField keeps it a plain text box for Mecanim rigs rather than erroring.
    [SpineAnimation(dataField: "spine", fallbackToTextField: true)] public string idleClip;
    [SpineAnimation(dataField: "spine", fallbackToTextField: true)] public string walkClip;
    [Tooltip("Optional. Blank falls back to walkClip, then idleClip.")]
    [SpineAnimation(dataField: "spine", fallbackToTextField: true)] public string flyClip;
    [Tooltip("Optional. Blank falls back to walkClip, then idleClip.")]
    [SpineAnimation(dataField: "spine", fallbackToTextField: true)] public string workClip;

    [System.NonSerialized] private Renderer[] renderers;
    [System.NonSerialized] private string playingClip;
    [System.NonSerialized] private float baseScaleX = 1f;

    // Tint targets, cached separately from the `spine`/`animator` fields above on purpose. Those two
    // are the ANIMATION contract and are wired per rig (the worker back rig deliberately leaves
    // `spine` empty and drives its clips through `animator`); tinting has to reach every skeleton and
    // sprite under the rig regardless of which pipeline plays it, so it searches the hierarchy
    // instead of trusting those refs. Deriving tinting from `spine` would silently stop tinting the
    // back-facing worker.
    [System.NonSerialized] private SkeletonAnimation[] tintSkeletons;
    [System.NonSerialized] private SpriteRenderer[] tintSprites;

    public bool Exists => root != null;

    public void Cache()
    {
        if (root == null)
        {
            renderers = new Renderer[0];
            tintSkeletons = new SkeletonAnimation[0];
            tintSprites = new SpriteRenderer[0];
            return;
        }
        // `true` so a facing authored disabled in the prefab is still found and can be shown later.
        renderers = root.GetComponentsInChildren<Renderer>(true);
        tintSkeletons = root.GetComponentsInChildren<SkeletonAnimation>(true);
        tintSprites = root.GetComponentsInChildren<SpriteRenderer>(true);
        baseScaleX = Mathf.Abs(root.transform.localScale.x);
        if (Mathf.Approximately(baseScaleX, 0f)) baseScaleX = 1f;
    }

    /// <summary>Clip for a state, falling back down the chain when the optional ones are unauthored.
    /// The worker rigs only ship idle + walk, so Flying and Working resolve to the walk cycle until
    /// someone exports dedicated clips — deliberately not an error.</summary>
    public string ClipFor(WalkerState state)
    {
        switch (state)
        {
            case WalkerState.Walking: return First(walkClip, idleClip);
            case WalkerState.Flying:  return First(flyClip, walkClip, idleClip);
            case WalkerState.Working: return First(workClip, walkClip, idleClip);
            default:                  return idleClip;
        }
    }

    private static string First(params string[] candidates)
    {
        for (int i = 0; i < candidates.Length; i++)
            if (!string.IsNullOrEmpty(candidates[i])) return candidates[i];
        return null;
    }

    /// <summary>Plays a clip, skipping the call when it's already the one playing — Spine's
    /// SetAnimation restarts from frame 0 on every call, so re-issuing it each frame would freeze
    /// the walker on its first pose.</summary>
    public void Play(string clip, float mecanimCrossfade)
    {
        if (string.IsNullOrEmpty(clip) || clip == playingClip) return;
        playingClip = clip;

        if (spine != null && spine.AnimationState != null) spine.AnimationState.SetAnimation(0, clip, true);
        else if (animator != null) animator.CrossFadeInFixedTime(clip, mecanimCrossfade);
    }

    public void SetRenderersEnabled(bool enabledState)
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].enabled = enabledState;
    }

    /// <summary>Horizontal mirror. `t` runs 1 (unflipped) → −1 (flipped); passing through 0 reads as
    /// the art turning edge-on, which is the card-flip look the rest of the game already uses.
    /// Spine gets its native Skeleton.ScaleX; anything else falls back to transform scale.</summary>
    public void ApplyFlip(float t)
    {
        if (spine != null && spine.Skeleton != null) { spine.Skeleton.ScaleX = t; return; }
        if (root == null) return;
        Vector3 s = root.transform.localScale;
        s.x = baseScaleX * t;
        root.transform.localScale = s;
    }

    /// <summary>
    /// Multiplies a colour over this rig's art. Both pipelines are unlit, so this is the ONLY tint
    /// channel either of them has — the Spine worker materials run `Spine/Skeleton`, which is
    /// `Lighting Off` with no `_Color`/`_BaseColor` uniform, so a MaterialPropertyBlock has nothing
    /// to write to (an earlier fatigue-tint attempt failed exactly there). Spine's supported channel
    /// is the skeleton's vertex colour, which SkeletonRenderer bakes into the mesh on its next
    /// update — that also means it survives the front/back renderer toggle and doesn't fight
    /// SkeletonRenderer's own MaterialPropertyBlock use. Sprite art takes SpriteRenderer.color.
    /// </summary>
    public void ApplyTint(Color tint)
    {
        if (tintSkeletons != null)
        {
            for (int i = 0; i < tintSkeletons.Length; i++)
            {
                SkeletonAnimation sa = tintSkeletons[i];
                if (sa == null || sa.Skeleton == null) continue;
                sa.Skeleton.R = tint.r;
                sa.Skeleton.G = tint.g;
                sa.Skeleton.B = tint.b;
                sa.Skeleton.A = tint.a;
            }
        }

        if (tintSprites != null)
        {
            for (int i = 0; i < tintSprites.Length; i++)
                if (tintSprites[i] != null) tintSprites[i].color = tint;
        }
    }

    /// <summary>Orients this facing's art plane in world space, for camera billboarding. Independent
    /// of the ScaleX mirror (skeleton-space) and the renderer toggle, so it composes with both. Set
    /// on the rig root — NOT the walker root — so it never fights WalkerManager's card-flip tween.</summary>
    public void ApplyBillboard(Quaternion worldRotation)
    {
        if (root != null) root.transform.rotation = worldRotation;
    }
}

/// <summary>How a walker's flat art is turned toward the camera each frame.</summary>
public enum BillboardMode
{
    None = 0,   // leave the authored rotation — reads edge-on/skewed under an angled camera
    YAxis = 1,  // yaw-only: turns to face the camera horizontally but stays upright on the ground (2.5D default)
    Full = 2,   // also matches the camera's pitch so the art lies perfectly flat to the screen
}

// Base MonoBehaviour for the Walker system. Owns the roaming state machine every walker
// shares: idle timer, power-law destination pick, BFS pathing + waypoint following, and
// deviation placement within a tile.
//
// The claim map (tile occupancy) lives on WalkerManager, not here — this class only calls
// into the manager's claim API and mirrors "the tile I currently claim" in currentTile. A
// claim moves at decision time: the instant a walker commits to a destination, currentTile
// already reflects that destination even though the walker is still travelling there.
//
// WorkerWalker and AnimalWalker subclass this for species-specific behaviour (Working flight,
// animal-follow); species tuning differences live entirely in WalkerProfileSO data, never in
// further subclasses.
public abstract class Walker : MonoBehaviour
{
    [Tooltip("This walker's species tuning. Normally assigned by WalkerManager.Initialize() at spawn time; " +
             "the Inspector field exists so a manually-placed test walker can also self-configure.")]
    [SerializeField] protected WalkerProfileSO profile;

    [Header("Visual — facing rigs")]
    [Tooltip("Art shown while the walker faces the camera. Required.")]
    [SerializeField] private WalkerFacingRig frontRig = new WalkerFacingRig();
    [Tooltip("Art shown while the walker faces away from the camera. OPTIONAL — leave the root empty for " +
             "single-facing art (e.g. the carabao spritesheet) and the front rig simply stays visible.")]
    [SerializeField] private WalkerFacingRig backRig = new WalkerFacingRig();

    [Header("Animation")]
    [Tooltip("Mecanim-only: crossfade duration in seconds when changing state. Spine uses the SkeletonData " +
             "asset's own defaultMix instead (0.2 on the worker rigs).")]
    [SerializeField] private float mecanimCrossfade = 0.1f;
    [Tooltip("SPEED (world units per second), not a per-frame position delta — at or above this the walker reads " +
             "as Walking rather than Idle. A per-frame delta would be ~moveSpeed/60 (0.025 at the default 1.5 " +
             "speed), so a 0.1 threshold against a raw delta would pin every walker to Idle forever. Against " +
             "speed, 0.1 sits well below every roaming speed and well above float noise.")]
    [SerializeField] private float moveSpeedThreshold = 0.1f;
    [Tooltip("Exponential smoothing rate on measured speed. Stops the Idle/Walking flag flickering on the single " +
             "short frame MoveTowards produces as it snaps onto a waypoint. Higher = snappier, lower = laggier. " +
             "0 disables smoothing.")]
    [SerializeField] private float speedSmoothing = 12f;

    [Header("Facing")]
    [Tooltip("Seconds for the left/right mirror. A reversal mid-turn resumes from the current angle and takes " +
             "proportionally less time, so it never snaps. 0 = instant.")]
    [SerializeField] private float flipDuration = 0.2f;
    [Tooltip("Swap the front/back rig choice if the art's convention is the opposite of the derived one. " +
             "Should be false for correctly-authored art — reach for it only if a rig's front and back " +
             "skeletons were exported the wrong way round.")]
    [SerializeField] private bool invertFrontBack;
    [Tooltip("Swap the mirror if the art faces the other way by default. Should be false: the billboard " +
             "puts the rig's local +X on screen-right, so the derived answer is already correct. If " +
             "left/right looks reversed, the billboard rotation is the thing that's wrong — see " +
             "BillboardRotation — and setting this instead only papers over half of it.")]
    [SerializeField] private bool invertFlip;

    [Header("Lighting")]
    [Tooltip("Multiply SunSignal.Tint over this walker's art so it darkens with the day-night cycle. " +
             "Walker art is unlit — Spine rigs on Spine/Skeleton, sprite rigs on the default sprite " +
             "material — so the rotating directional light that darkens the Lit tiles never reaches " +
             "it, and without this the walker stays at noon brightness on a midnight tile. Turn off " +
             "for a walker that should read as self-lit.")]
    [SerializeField] private bool receiveDayNightTint = true;

    [Header("Billboard")]
    [Tooltip("Turn each facing rig to face the camera so the flat art never reads edge-on under an " +
             "angled camera. Y-Axis keeps the walker upright (standard 2.5D); Full also matches the " +
             "camera's pitch so the art lies flat to the screen; None leaves the authored rotation.")]
    [SerializeField] private BillboardMode billboard = BillboardMode.YAxis;

    // The tile this walker currently claims. Updated at decision time, not on arrival — see
    // BeginMoveTo. Always non-null once Initialize has run.
    protected Tile currentTile;

    private enum RoamPhase { Idling, Moving }
    private RoamPhase roamPhase = RoamPhase.Idling;
    private float idleTimer;

    // Reused per-walker BFS scratch state — cleared and refilled every BuildPath call instead
    // of being re-allocated, to avoid per-journey allocations.
    private readonly List<Tile> path = new List<Tile>();
    private readonly Dictionary<Tile, Tile> cameFrom = new Dictionary<Tile, Tile>();
    private readonly Queue<Tile> frontier = new Queue<Tile>();
    private readonly List<Tile> pathScratch = new List<Tile>();
    private int pathIndex;
    private Vector3 currentWaypointTarget;

    // Animation state. Idle/Walking are derived by measuring transform.position frame to frame
    // rather than by reading roamPhase — deliberate: WorkerWalker's flight moves the transform from
    // a coroutine with the roam loop gated off, so a roamPhase-driven animation would freeze
    // mid-flight. Measuring the transform covers every mover, present and future, for free.
    private Vector3 lastFramePosition;
    private float smoothedSpeed;
    private bool isMovingVisual;
    private bool facingBack;
    private bool facingFlipped;
    private Camera facingCamera;

    // The last horizontal direction this walker actually travelled, in WORLD space, plus whether it
    // has ever travelled at all. Facing is re-resolved from this direction against the LIVE camera
    // every frame rather than being latched as a front/back boolean at the moment of travel: a
    // boolean is frozen against whatever camera angle happened to be in play back then, so orbiting
    // the camera afterwards strands an idle walker showing the wrong side. The latch itself is
    // unchanged and deliberate — a walker that stopped walking away keeps its back to you until it
    // walks somewhere else. Nothing pulls it home to front-facing.
    private Vector3 lastTravelDirection;
    private bool hasTravelled;

    // Below this |dot| the travel direction sits too close to one of the camera's axes to answer
    // that axis's question, so the current side is kept. Only matters now that facing is resolved
    // every frame instead of only while moving — without it, a walker whose direction lies almost
    // exactly on an axis would chatter between both answers as the camera drifts across it.
    private const float FacingDeadZone = 0.05f;

    // Card-flip lifecycle tilt in degrees about the rig's own X axis, driven by WalkerManager's
    // spawn/despawn tween through SetLifecycleTilt. Composed into the billboard rotation rather
    // than living on the walker root — see SetLifecycleTilt.
    private float lifecycleTilt;

    // Last billboard rotation pushed. Reused as the answer when the camera sits directly overhead
    // and there is no horizontal direction left to yaw toward.
    private Quaternion lastBillboard = Quaternion.identity;

    // Mirror state. Runs 1 (unflipped) → −1 (flipped); both rigs are kept at the same value so a
    // front/back swap partway through a turn doesn't reveal a rig facing the wrong way.
    private float flipT = 1f;
    private float flipTargetT = 1f;
    private Coroutine flipRoutine;

    // Reveal override — see the "Reveal" region near ApplyBillboard for the full explanation.
    private bool revealOverrideActive;
    private Coroutine revealRoutine;

    // Tint state. artTint is the subclass's own channel (WorkerWalker's fatigue grey); the sun tint
    // is multiplied on top of it every frame. lastAppliedTint skips the push when nothing moved —
    // the sun changes continuously during a cycle but is static between actions, so most frames are
    // a no-op.
    private Color artTint = Color.white;
    private Color lastAppliedTint = new Color(-1f, -1f, -1f, -1f); // impossible value: forces frame one

    /// <summary>
    /// The subclass's own tint, multiplied UNDER the day-night sun tint. WorkerWalker drives the
    /// fatigue grey through here. Composition is the point: a fatigued worker at night must read as
    /// both, not as whichever system wrote last.
    /// </summary>
    protected Color ArtTint
    {
        get => artTint;
        set => artTint = value;
    }

    /// <summary>The tile this walker currently claims.</summary>
    public Tile CurrentTile => currentTile;

    /// <summary>
    /// This walker's current activity. Idle/Walking are derived here from measured motion;
    /// Flying/Working are declared by WorkerWalker via SetState and are never overwritten by the
    /// motion derivation — a worker standing still on its work tile must read Working, not Idle.
    /// </summary>
    public WalkerState State { get; private set; } = WalkerState.Idle;

    /// <summary>True while the walker's smoothed horizontal speed is at or above moveSpeedThreshold.
    /// Note this is about motion, not activity — a Flying walker is also "moving". Read State for activity.</summary>
    public bool IsMoving => isMovingVisual;

    /// <summary>True while the walker is showing its back (last movement was away from the camera).
    /// Latched — holds the last travelled direction while idle rather than resetting.</summary>
    public bool FacingBack => facingBack;

    /// <summary>Declares a state the motion derivation can't infer. WorkerWalker owns Flying/Working;
    /// handing back Idle returns control to the derivation.</summary>
    protected void SetState(WalkerState next)
    {
        if (State == next) return;
        State = next;
        PushState();
    }

    protected WalkerManager manager => WalkerManager.Instance;

    protected virtual void Awake()
    {
        // profile is usually assigned by Initialize() right after Instantiate — this only
        // catches a walker placed directly in a scene without going through the manager.
        if (profile == null)
            Debug.LogWarning($"{name}: WalkerProfileSO not assigned yet — expected to be set via Initialize().", this);

        if (frontRig == null || !frontRig.Exists)
            Debug.LogWarning($"{name}: front facing rig has no root assigned — the walker will be invisible.", this);

        // Each rig caches its own renderers + base scale; back may be unused (single-facing art).
        frontRig?.Cache();
        backRig?.Cache();

        facingCamera = Camera.main;
        lastFramePosition = transform.position;

        // Face the camera on frame one, before any motion. Repeated at the end of Initialize once
        // the walker has actually been placed — see FaceCameraNow.
        FaceCameraNow();
        ApplyCompositeTint(); // ...and at frame-one's light level, so a night spawn never flashes bright
        PushState();
    }

    /// <summary>
    /// Called by WalkerManager immediately after Instantiate. Binds the profile, claims the
    /// starting tile, and places the walker with roam-deviation offset.
    /// </summary>
    public virtual void Initialize(WalkerProfileSO walkerProfile, Tile startTile)
    {
        profile = walkerProfile;
        currentTile = startTile;
        idleTimer = Random.Range(profile.idleMin, profile.idleMax);
        roamPhase = RoamPhase.Idling;

        manager.ClaimTile(this, currentTile);
        transform.position = DeviatedPosition(currentTile, profile.roamDeviation);

        // This is a teleport, not travel. Without resyncing, the next frame would measure the
        // whole origin→start-tile jump as one frame of motion and latch a bogus facing from it.
        lastFramePosition = transform.position;

        // Now that the walker is standing where it belongs, seed the facing again. Awake's call ran
        // during Instantiate — before the line above — so the rotation it computed belonged to
        // wherever the prefab landed, not to this tile.
        FaceCameraNow();
    }

    protected virtual void Update()
    {
        // Frozen while hidden for reveal (HideForReveal/RevealFacingCamera): roaming while hidden
        // would move the walker off its spawn spot AND — worse — a facing flip mid-roam calls
        // ApplyFacingModels, which unconditionally re-enables the rig renderers and pops the walker
        // back into view before the actual reveal cue. Freezing here removes both problems at the root.
        if (revealOverrideActive) return;

        // WorkerWalker gates this off entirely while Working via CanRoamThisFrame, so the
        // flight coroutine owns movement instead.
        if (!CanRoamThisFrame()) return;

        switch (roamPhase)
        {
            case RoamPhase.Idling:
                idleTimer -= Time.deltaTime;
                if (idleTimer <= 0f) BeginRoamMove();
                break;
            case RoamPhase.Moving:
                StepAlongPath();
                break;
        }
    }

    /// <summary>Gate hook — WorkerWalker returns false while Working so this base roaming loop
    /// doesn't fight the flight coroutine for control of this frame.</summary>
    protected virtual bool CanRoamThisFrame() => true;

    /// <summary>Per-frame move speed. WorkerWalker overrides to apply fatiguedSpeedMultiplier
    /// while the worker is fatigued.</summary>
    protected virtual float EffectiveMoveSpeed() => profile.moveSpeed;

    private void BeginRoamMove()
    {
        Tile dest = PickRoamDestination();
        if (dest == null)
        {
            // No legal candidate this beat (e.g. every nearby tile claimed, or unreachable) —
            // just idle again rather than spin every frame retrying.
            idleTimer = Random.Range(profile.idleMin, profile.idleMax);
            return;
        }
        BeginMoveTo(dest);
    }

    /// <summary>Commits to a destination: builds the BFS path, moves the claim now (decision
    /// time), and starts stepping the path. Falls back to idling if unreachable.</summary>
    protected void BeginMoveTo(Tile dest)
    {
        if (!BuildPath(currentTile, dest))
        {
            idleTimer = Random.Range(profile.idleMin, profile.idleMax);
            return;
        }

        manager.ReleaseClaim(this, currentTile);
        manager.ClaimTile(this, dest);
        currentTile = dest; // claim moves at decision time, even though we haven't arrived yet

        pathIndex = 0;
        roamPhase = RoamPhase.Moving;
        currentWaypointTarget = TargetPositionForIndex(pathIndex);
    }

    private void StepAlongPath()
    {
        transform.position = Vector3.MoveTowards(transform.position, currentWaypointTarget, EffectiveMoveSpeed() * Time.deltaTime);
        if (Vector3.Distance(transform.position, currentWaypointTarget) > 0.01f) return;

        pathIndex++;
        if (pathIndex >= path.Count)
        {
            roamPhase = RoamPhase.Idling;
            idleTimer = Random.Range(profile.idleMin, profile.idleMax);
            return;
        }
        currentWaypointTarget = TargetPositionForIndex(pathIndex);
    }

    /// <summary>Resets the roaming state machine to a fresh idle beat from wherever the walker
    /// currently is. Used by WorkerWalker when Working ends, so the walker picks a new
    /// destination from its current position instead of resuming whatever mid-move state was
    /// frozen when Working began.</summary>
    protected void ResetToIdle()
    {
        roamPhase = RoamPhase.Idling;
        idleTimer = Random.Range(profile.idleMin, profile.idleMax);
    }

    private Vector3 TargetPositionForIndex(int idx)
    {
        bool isFinal = idx == path.Count - 1;
        return isFinal ? DeviatedPosition(path[idx], profile.roamDeviation) : WaypointPosition(path[idx]);
    }

    private Vector3 WaypointPosition(Tile t)
    {
        Vector3 pos = TileManager.Instance.GridToWorldPosition(t.gridPosition);
        pos.y = manager.WalkerY;
        Vector2 jitter = Random.insideUnitCircle * profile.waypointJitter;
        pos.x += jitter.x;
        pos.z += jitter.y;
        return pos;
    }

    /// <summary>Tile-center + random deviation in [-deviation, +deviation] on x/z.</summary>
    protected Vector3 DeviatedPosition(Tile t, float deviation)
    {
        Vector3 pos = TileManager.Instance.GridToWorldPosition(t.gridPosition);
        pos.y = manager.WalkerY;
        pos.x += Random.Range(-deviation, deviation);
        pos.z += Random.Range(-deviation, deviation);
        return pos;
    }

    /// <summary>
    /// Plain BFS over 4-dir adjacency via TileManager.GetAdjacentTiles. Reuses path/cameFrom/
    /// frontier/pathScratch across calls — no per-journey allocations of the big structures.
    /// Returns false (path left empty) if `to` is unreachable from `from`.
    /// </summary>
    private bool BuildPath(Tile from, Tile to)
    {
        path.Clear();
        if (from == null || to == null || from == to) return false;

        cameFrom.Clear();
        frontier.Clear();
        frontier.Enqueue(from);
        cameFrom[from] = null;

        while (frontier.Count > 0)
        {
            Tile cur = frontier.Dequeue();
            if (cur == to) break;
            foreach (Tile n in TileManager.Instance.GetAdjacentTiles(cur))
            {
                if (cameFrom.ContainsKey(n)) continue;
                cameFrom[n] = cur;
                frontier.Enqueue(n);
            }
        }

        if (!cameFrom.ContainsKey(to)) return false; // unreachable — different island, etc.

        pathScratch.Clear();
        Tile step = to;
        while (step != null && step != from)
        {
            pathScratch.Add(step);
            step = cameFrom[step];
        }
        for (int i = pathScratch.Count - 1; i >= 0; i--) path.Add(pathScratch[i]);
        return path.Count > 0;
    }

    /// <summary>
    /// Weighted random destination pick over unclaimed tiles within destinationRadius, weight
    /// = 1 / max(d,1)^falloffExponent. AnimalWalker overrides to try the follow-chance roll
    /// first, falling back to this.
    /// </summary>
    protected virtual Tile PickRoamDestination()
    {
        List<Tile> candidates = new List<Tile>();
        foreach (Tile t in TileManager.Instance.GetAllTiles())
        {
            if (t == currentTile) continue;
            if (GridDistance(currentTile.gridPosition, t.gridPosition) > profile.destinationRadius) continue;
            if (!manager.IsClaimEmpty(t)) continue; // roaming only targets unclaimed tiles
            candidates.Add(t);
        }
        return candidates.Count > 0 ? WeightedPick(candidates) : null;
    }

    /// <summary>Weighted random sample over an already-filtered candidate list. d=0 is
    /// impossible here (the current tile is always excluded by callers), but max(d,1) still
    /// guards it defensively.</summary>
    protected Tile WeightedPick(List<Tile> candidates)
    {
        float total = 0f;
        float[] weights = new float[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
        {
            int d = GridDistance(currentTile.gridPosition, candidates[i].gridPosition);
            weights[i] = 1f / Mathf.Pow(Mathf.Max(d, 1), profile.falloffExponent);
            total += weights[i];
        }

        float roll = Random.value * total;
        float accum = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            accum += weights[i];
            if (roll <= accum) return candidates[i];
        }
        return candidates[candidates.Count - 1]; // float rounding fallback
    }

    protected static int GridDistance(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    // ── Animation state + facing ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Derives Idle/Walking from measured motion and keeps the facing models in sync. Runs in
    /// LateUpdate so it reads the final position for the frame no matter who wrote it — the roam
    /// loop's Update or WorkerWalker's flight coroutine.
    /// </summary>
    protected virtual void LateUpdate()
    {
        // Ahead of the dt guard below: the sun keeps turning through a paused frame, and a walker
        // frozen at the wrong brightness is more obvious than one frozen mid-stride.
        ApplyCompositeTint();

        float dt = Time.deltaTime;
        if (dt <= 0f) return; // paused / first frame — hold the current pose

        Vector3 delta = transform.position - lastFramePosition;
        lastFramePosition = transform.position;

        // Horizontal only. The Working flight's parabolic arc is pure Y, and counting it would both
        // inflate speed at launch/landing and make facing meaningless at the arc's apex.
        delta.y = 0f;

        float rawSpeed = delta.magnitude / dt;
        smoothedSpeed = speedSmoothing > 0f
            ? Mathf.Lerp(smoothedSpeed, rawSpeed, 1f - Mathf.Exp(-speedSmoothing * dt)) // frame-rate independent
            : rawSpeed;
        isMovingVisual = smoothedSpeed >= moveSpeedThreshold;

        // Only Idle/Walking are ours to derive — Flying and Working are declared by WorkerWalker and
        // must survive a frame in which the walker happens to be stationary.
        if (State == WalkerState.Idle || State == WalkerState.Walking)
            SetState(isMovingVisual ? WalkerState.Walking : WalkerState.Idle);

        // The travel DIRECTION is only recorded while actually moving, so stopping keeps the
        // direction the walker arrived in instead of snapping back to a default pose — a walker
        // that walked away stays showing its back for as long as it stands there.
        if (isMovingVisual && delta.sqrMagnitude > 0f)
        {
            lastTravelDirection = delta.normalized;
            hasTravelled = true;
        }

        // Resolving that direction into front/back + mirror DOES run every frame, so the answer
        // tracks a camera that has moved since the walker last travelled.
        ResolveFacing();

        // Billboard runs every frame (not gated on motion): a perspective camera needs a per-
        // position yaw even for a stationary walker, and it must stay correct if the camera moves.
        ApplyBillboard();
    }

    /// <summary>
    /// Turns each rig's flat art plane toward the camera, with the card-flip lifecycle tilt composed
    /// on top. Applied to the rig roots, not the walker root — and since it writes their WORLD
    /// rotation every frame it is the sole owner of that rotation, which is why the tilt has to come
    /// through here rather than from a tween of the root (see SetLifecycleTilt). Orthogonal to the
    /// ScaleX mirror (skeleton-space) and the front/back renderer toggle — both still apply on top.
    /// </summary>
    private void ApplyBillboard()
    {
        if (revealOverrideActive) return; // HideForReveal / RevealFacingCamera own the rig rotation right now
        if (billboard == BillboardMode.None) return;
        if (facingCamera == null) facingCamera = Camera.main;
        if (facingCamera == null) return; // no camera yet — hold the authored rotation

        // Post-multiplied, so the card-flip tilt turns about the rig's OWN horizontal axis after
        // billboarding — the card flips toward the viewer rather than about some world axis.
        Quaternion rot = BillboardRotation();
        if (!Mathf.Approximately(lifecycleTilt, 0f)) rot *= Quaternion.Euler(lifecycleTilt, 0f, 0f);

        frontRig?.ApplyBillboard(rot);
        backRig?.ApplyBillboard(rot);
    }

    /// <summary>
    /// The rotation that turns a rig's flat art plane to the viewer, per billboard mode.
    ///
    /// The rig's local −Z is the art's front: Unity sprite quads and spine-unity meshes are both
    /// authored to be seen from that side (a sprite at identity rotation reads correctly to a camera
    /// sitting at −Z). So the rig's +Z must point ALONG the camera's forward — away from the viewer.
    /// Pointing +Z at the camera instead draws the art mirrored and puts local +X on screen-LEFT,
    /// which silently inverts the left/right mirror semantics on top of it. Both shaders in play
    /// (`Spine/Skeleton`, `Sprites-Default`) are Cull Off, so getting this backwards doesn't hide
    /// the walker — it just quietly reverses it, which is exactly how it went unnoticed.
    ///
    /// This is the same convention EntityVisualizer uses for every tile entity sprite in the game.
    /// </summary>
    private Quaternion BillboardRotation()
    {
        // Null-safe for every caller, not just ApplyBillboard — the reveal path can run before a
        // main camera exists, and holding the last pose beats throwing.
        if (facingCamera == null) facingCamera = Camera.main;
        if (facingCamera == null) return lastBillboard;

        // Full: the camera's own rotation, so the art lies perfectly flat to the screen (may lift
        // the feet). Matches the tile entities exactly.
        if (billboard == BillboardMode.Full)
        {
            lastBillboard = facingCamera.transform.rotation;
            return lastBillboard;
        }

        // YAxis: same convention, yaw only, so the walker stays upright with its feet on the ground.
        // Derived per walker position rather than from the camera's yaw, so a perspective camera
        // still turns each walker toward the lens rather than all of them to a single shared angle.
        Vector3 away = transform.position - facingCamera.transform.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) return lastBillboard; // camera directly overhead — hold
        lastBillboard = Quaternion.LookRotation(away);
        return lastBillboard;
    }

    /// <summary>
    /// The wake-up default: front rig, unmirrored, turned to the camera. Called from Awake AND from
    /// the end of Initialize — Awake runs during Instantiate, before Initialize writes the walker's
    /// position, so the rotation it computes belongs to wherever the prefab landed rather than to
    /// the walker's tile.
    ///
    /// A SEED, not a resting state. The moment the walker travels, ResolveFacing takes over and
    /// keeps whichever side it last walked with; nothing ever pulls it back to front.
    /// </summary>
    protected void FaceCameraNow()
    {
        if (flipRoutine != null) { StopCoroutine(flipRoutine); flipRoutine = null; }

        facingBack = false;
        facingFlipped = false;
        flipT = 1f;
        flipTargetT = 1f;
        hasTravelled = false;
        lastTravelDirection = Vector3.zero;

        ApplyFacingModels();
        ApplyFlip();
        ApplyBillboard();
    }

    /// <summary>
    /// Sets the card-flip tilt (degrees about the rig's own X axis) used by WalkerManager's spawn
    /// and despawn tweens.
    ///
    /// It lives here, composed into the billboard rotation, rather than on the walker root, because
    /// ApplyBillboard writes the rig roots' WORLD rotation every LateUpdate — a tween of the root
    /// transform is simply erased before it is ever drawn, which is why the animal spawn flip was
    /// invisible. Pushes immediately instead of waiting for the next LateUpdate, since the despawn
    /// tween deliberately runs with this component disabled.
    /// </summary>
    public void SetLifecycleTilt(float degreesX)
    {
        lifecycleTilt = degreesX;

        // With no billboard there is nothing owning the rig rotation, so the tilt goes on the walker
        // root exactly as the old tween did.
        if (billboard == BillboardMode.None)
        {
            transform.localRotation = Quaternion.Euler(degreesX, 0f, 0f);
            return;
        }
        ApplyBillboard();
    }

    /// <summary>
    /// Pushes ArtTint × the day-night sun tint to both rigs. Both rigs, not just the visible one:
    /// the hidden facing is only renderer-disabled (never deactivated, see ApplyFacingModels), so
    /// it has to already be at the right brightness the instant a turn swaps it in.
    /// </summary>
    private void ApplyCompositeTint()
    {
        Color composite = receiveDayNightTint ? artTint * SunSignal.Tint : artTint;
        if (composite == lastAppliedTint) return; // nothing moved this frame
        lastAppliedTint = composite;

        if (frontRig != null && frontRig.Exists) frontRig.ApplyTint(composite);
        if (backRig  != null && backRig.Exists)  backRig.ApplyTint(composite);
    }

    // ── Reveal (entrance cue driven by an external system, e.g. onboarding) ────────────────────
    // Lets a walker start hidden and turned away, then be shown and turned to face the camera on
    // cue. Suppresses the automatic per-frame ApplyBillboard above while active — otherwise the rig
    // would snap straight to its camera-facing pose on the very next LateUpdate, before the lerp
    // below ever got drawn — and hands control back the instant the lerp lands on that same pose,
    // so the handoff is invisible. Roaming/animation are untouched by any of this; only the art
    // (rig rotation + renderers) is affected.

    /// <summary>Hides both rigs and turns them 180° off their camera-facing pose. Call right after
    /// Initialize on a walker that shouldn't be seen yet — RevealFacingCamera is its counterpart.</summary>
    public void HideForReveal()
    {
        revealOverrideActive = true;
        Quaternion away = BillboardRotation() * Quaternion.Euler(0f, 180f, 0f);
        frontRig?.ApplyBillboard(away);
        backRig?.ApplyBillboard(away);
        frontRig?.SetRenderersEnabled(false);
        backRig?.SetRenderersEnabled(false);
    }

    /// <summary>Shows the rigs and lerps them from the hidden, turned-away pose to facing the
    /// camera over `duration` seconds. No-op if this walker was never hidden or is already
    /// revealing — safe to call more than once.</summary>
    public void RevealFacingCamera(float duration)
    {
        if (!revealOverrideActive || revealRoutine != null) return;
        ApplyFacingModels(); // shows whichever rig (front/back) belongs at the current facing
        revealRoutine = StartCoroutine(RevealRoutine(duration));
    }

    private IEnumerator RevealRoutine(float duration)
    {
        // Both ends of the lerp come from BillboardRotation, so the pose this lands on IS the pose
        // ApplyBillboard takes over with on the next frame — the handoff is invisible in every
        // billboard mode. (The old yaw-only reveal popped on a Full-mode walker, which the Worker
        // prefab is, at exactly the moment the crew is first shown.) The target is re-read each
        // frame so a camera moving during the reveal is tracked rather than aimed at once.
        Quaternion start = BillboardRotation() * Quaternion.Euler(0f, 180f, 0f);
        float t = 0f;
        while (t < 1f)
        {
            t += duration > 0f ? Time.deltaTime / duration : 1f;
            Quaternion current = Quaternion.Slerp(start, BillboardRotation(), Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            frontRig?.ApplyBillboard(current);
            backRig?.ApplyBillboard(current);
            yield return null;
        }

        revealOverrideActive = false; // hand back to the normal per-frame ApplyBillboard above
        revealRoutine = null;
    }

    /// <summary>
    /// Resolves the last travelled direction onto 2 models × a turn, by projecting it onto the
    /// camera's own flattened forward and right axes.
    ///
    /// Two projections are needed, not one: a single dot product yields a single sign, which can
    /// only ever answer front-vs-back OR left-vs-right, never both. Forward picks the model, right
    /// picks the turn, and together they partition all 360° with no undefined wedge.
    ///
    /// Deriving the axes from the camera rather than hardcoding world +x/+z keeps the mapping right
    /// at whatever yaw the scene camera sits at, and — because this runs every frame against the
    /// stored direction rather than once at the moment of travel — it stays right when that yaw
    /// changes underneath a walker that has been standing still for a while.
    /// </summary>
    private void ResolveFacing()
    {
        if (!hasTravelled) return; // never moved — hold the FaceCameraNow seed
        if (facingCamera == null) facingCamera = Camera.main;
        if (facingCamera == null) return; // no camera yet — hold the last facing rather than guess

        Transform cam = facingCamera.transform;
        Vector3 camForward = cam.forward; camForward.y = 0f;
        Vector3 camRight   = cam.right;   camRight.y   = 0f;

        // A perfectly top-down camera has no horizontal forward to project onto.
        if (camForward.sqrMagnitude < 0.0001f) return;
        camForward.Normalize();
        camRight.Normalize();

        // Camera forward points into the screen, so travelling along it is travelling away from the
        // viewer — which is exactly when the walker's back is what we should see.
        float awayDot  = Vector3.Dot(lastTravelDirection, camForward);
        float rightDot = Vector3.Dot(lastTravelDirection, camRight);

        // Each axis is only allowed to answer when the direction is decisively off it — see
        // FacingDeadZone. An inconclusive axis leaves its half of the facing exactly as it was.
        if (Mathf.Abs(awayDot) > FacingDeadZone)
        {
            bool movingAway = awayDot > 0f;
            bool nextBack = invertFrontBack ? !movingAway : movingAway;
            if (nextBack != facingBack)
            {
                facingBack = nextBack;
                ApplyFacingModels();
            }
        }

        if (Mathf.Abs(rightDot) > FacingDeadZone)
        {
            bool movingRight = rightDot > 0f;
            bool nextFlipped = invertFlip ? !movingRight : movingRight;
            if (nextFlipped != facingFlipped)
            {
                facingFlipped = nextFlipped;
                BeginFlip(facingFlipped);
            }
        }
    }

    /// <summary>
    /// Shows one rig and hides the other by toggling Renderers, deliberately NOT by SetActive:
    /// deactivating a rig stops its SkeletonAnimation/Animator, so its skeleton freezes at whatever
    /// pose it held and pops on the way back in. Keeping both objects active keeps both rigs playing
    /// in lockstep (see PushState), so the swap lands mid-stride. It also keeps WorkerWalker's
    /// Awake-time renderer cache (for the fatigue tint) complete, since that call skips inactive objects.
    ///
    /// Single-facing art (backRig unassigned — e.g. the carabao spritesheet) keeps the front rig
    /// visible regardless of facingBack; only the left/right mirror still applies to it.
    /// </summary>
    private void ApplyFacingModels()
    {
        bool showBack = facingBack && backRig != null && backRig.Exists;
        if (frontRig != null && frontRig.Exists) frontRig.SetRenderersEnabled(!showBack);
        if (backRig  != null && backRig.Exists)  backRig.SetRenderersEnabled(showBack);
    }

    /// <summary>Plays the current state's clip on BOTH rigs (each resolves its own clip name via
    /// ClipFor), so the hidden rig stays in lockstep and is already mid-stride when it's shown. Rig.Play
    /// skips the call when the clip is unchanged, so this is safe to hit every state change.</summary>
    private void PushState()
    {
        if (frontRig != null && frontRig.Exists) frontRig.Play(frontRig.ClipFor(State), mecanimCrossfade);
        if (backRig  != null && backRig.Exists)  backRig.Play(backRig.ClipFor(State), mecanimCrossfade);
    }

    // ── The turn (mirror, lerped, never a snap) ──────────────────────────────────────────────────
    // flipT runs 1 (unflipped) → −1 (flipped); passing through 0 reads as the art turning edge-on,
    // the card-flip look the rest of the game uses. Both rigs are driven to the same value so a
    // front/back swap mid-turn never reveals a rig facing the wrong way.

    private void BeginFlip(bool flipped)
    {
        float target = flipped ? -1f : 1f;
        if (Mathf.Approximately(target, flipTargetT)) return;
        flipTargetT = target;

        if (flipDuration <= 0f)
        {
            flipT = target;
            ApplyFlip();
            return;
        }

        if (flipRoutine != null) StopCoroutine(flipRoutine);
        flipRoutine = StartCoroutine(FlipRoutine(target));
    }

    /// <summary>Lerps flipT to the target (±1) over flipDuration. A reversal partway through starts
    /// from the current value and scales its duration to the distance left to travel (full swing is
    /// a span of 2), so a walker that changes direction mid-turn swings back smoothly instead of jumping.</summary>
    private IEnumerator FlipRoutine(float target)
    {
        float start = flipT;
        float duration = flipDuration * (Mathf.Abs(target - start) / 2f);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(duration, 0.0001f);
            flipT = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            ApplyFlip();
            yield return null;
        }

        flipT = target;
        ApplyFlip();
        flipRoutine = null;
    }

    /// <summary>Pushes the current mirror value to both rigs — Spine gets Skeleton.ScaleX, Mecanim/
    /// sprite art gets a negative transform scale (see WalkerFacingRig.ApplyFlip).</summary>
    private void ApplyFlip()
    {
        if (frontRig != null && frontRig.Exists) frontRig.ApplyFlip(flipT);
        if (backRig  != null && backRig.Exists)  backRig.ApplyFlip(flipT);
    }

    protected virtual void OnDestroy()
    {
        if (WalkerManager.Instance != null && currentTile != null)
            WalkerManager.Instance.ReleaseClaim(this, currentTile);
    }
}
