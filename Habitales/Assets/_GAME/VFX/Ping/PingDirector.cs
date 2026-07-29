using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Habitales.Entities;

// PingDirector — Azi tier system.
//
// Owns expanding "ring" ping surfaces and bursts them from every dying (Critical-tier,
// Plant-category) tile at day-resolution / action-landing, plus one-shot pings for stream A's T3
// death popups and onboarding's worker pings. Each surface's MeshRenderer is disabled whenever no
// ping is currently animating on it — zero idle cost.
//
// TWO SURFACES (2026-07-23): the same struct/stride/shader drives two independent quads.
//   • GROUND  — a world-covering ground-projected quad. Critical-tile pings (BurstSweep) and the
//     color-less/colored PingAt(...) one-shots render here; the shader measures ring distance in
//     world XZ (its material keeps _PingPlaneMode = 0).
//   • OVERLAY — an optional quad strapped to the FRONT of the camera (parented to it, facing the
//     lens). PingOverlay(...) routes here; PingSurface keeps the ping's WORLD anchor and
//     re-projects it into the quad's own local plane EVERY frame (not just once at ping-start), so
//     the quad can keep riding the camera while the ring still tracks the anchor instead of
//     freezing at the screen position the camera happened to be at when the ping began. The shader
//     measures distance in local XY (material sets _PingPlaneMode = 1), so a camera-glued quad reads
//     as flat, un-skewed UI rings at any camera angle. Used by onboarding's white "here's your crew"
//     / red "these are tired" worker pings.
// The two never share a buffer — the overlay's buffer just gets re-projected to local space on
// upload instead of being stored that way.
//
// UNCAPPED PING COUNT: every active ping (center + start time + color) lives in a plain List that
// grows as bursts/one-shots add to it and shrinks as pings finish their lifetime. Each surface's
// GPU-side StructuredBuffer is (re)sized to that list's actual count on every change — never a
// fixed array. The Shader Graph ring shader loops the buffer per-pixel and combines overlapping
// rings with max() (never +) — see PingRingShaderSpec.md / PingRingFunction.hlsl.
//
// SCRIPT-OWNED CLOCK: RunManager's pause (IsEventPaused) is a flag, not a time-scale — Time.time
// / shader _Time NEVER stop. This component accumulates its own float clock in Update(), halted
// while RunManager.IsEventPaused, and pushes it as a GLOBAL shader float (_HabitalesClock) so
// BOTH this ping shader and the CriticalGlowController's glow shader read the same pausable
// clock and visibly freeze mid-animation behind an intrusive popup. This Update() is the ONLY
// polling-shaped code in stream C (Law 2) — every burst is event-triggered.
[DefaultExecutionOrder(100)] // after RunManager/TileManager/ActionManager Awake (S4 singletons)
public class PingDirector : MonoBehaviour
{
    public static PingDirector Instance { get; private set; }

    [Header("Ground Quad (world-space Critical pings)")]
    [Tooltip("The oversized ground-projected quad that renders the Critical-tile / PingAt world-space " +
             "rings. Its material keeps _PingPlaneMode = 0 (world XZ). Its MeshRenderer is disabled " +
             "whenever no ping is animating (zero idle cost). At least one of ground/overlay is required.")]
    [SerializeField] private MeshRenderer groundQuadRenderer;

    [Header("Overlay Quad (optional — lens/UI worker pings)")]
    [Tooltip("Second quad strapped to the FRONT of the camera (parented to it, facing the lens), sized " +
             "to fill the frustum. Pings routed via PingOverlay(...) render here in the quad's own local " +
             "plane, so they read as flat, un-skewed UI rings regardless of camera angle. Its material " +
             "must set _PingPlaneMode = 1 (object-space XY). Leave null if unused.")]
    [SerializeField] private MeshRenderer overlayQuadRenderer;

    [Header("Stagger (burst sweeps only — PingAt / PingOverlay are never staggered)")]
    [Tooltip("Base seconds between one burst-ping's start and the next, in director-clock time.")]
    [SerializeField] private float baseStaggerInterval = 0.12f;
    [Tooltip("Random +/- jitter added to each burst-ping's start offset, in director-clock seconds.")]
    [SerializeField] private float staggerJitter = 0.05f;

    [Header("Lifecycle")]
    [Tooltip("How long (director-clock seconds) a single ping stays in the active buffer before " +
             "this script prunes it and (if it was the last one on that surface) disables the quad. This " +
             "is a C#-side bookkeeping value only — it does not draw anything itself. Keep it roughly " +
             "matched to the shader material's own Ring Max Radius / Ring Speed (radius / speed = " +
             "seconds for the ring to finish) so the quad doesn't go dark mid-fade or stay lit idle " +
             "after the shader's own ring math has already faded to nothing. Tune in play mode " +
             "alongside the material — see PingRingShaderSpec.md.")]
    [SerializeField] private float pingLifetimeSeconds = 2.5f;

    [Header("Color")]
    [Tooltip("Ring color used by the color-less PingAt(pos) path AND every day-resolution / " +
             "action-landing burst sweep — the Critical-warning red. Callers that want a different " +
             "hue (e.g. onboarding's white 'here's your crew' and red 'these are tired' worker pings) " +
             "use the PingAt(pos, Color) / PingOverlay(pos, Color) overloads instead.")]
    [SerializeField] private Color defaultPingColor = new Color(1f, 0.25f, 0.1f, 1f); // hot red-orange

    // ── GPU buffer (shared shape for both surfaces) ───────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct PingBufferEntry
    {
        public Vector3 center;     // ring origin — WORLD space (ground) or quad-LOCAL space (overlay)
        public float   startTime;  // director-clock time this ping begins expanding
        public Vector4 color;      // per-ping RGBA hue — the shader tints each pixel by its winning (max-intensity) ring's color
    }
    private const int PingBufferStride = 32; // 3 + 1 + 4 floats = 32 bytes, matches the .hlsl PingData struct

    private static readonly int PingBufferId = Shader.PropertyToID("_PingBuffer");
    private static readonly int PingCountId  = Shader.PropertyToID("_PingCount");
    private static readonly int ClockId      = Shader.PropertyToID("_HabitalesClock"); // GLOBAL — shared with the glow shader

    private struct PingRuntime
    {
        public Vector3 center;
        public float   startTime;
        public float   endTime;
        public Vector4 color;
    }

    // One render surface = one quad + its own ComputeBuffer + active-ping list. PingDirector owns two
    // (ground world-space + overlay object-space); they never share a buffer because their centers
    // are in different spaces. The distance metric is a per-material toggle (_PingPlaneMode), so the
    // same shader draws both — this class only feeds each quad the centers already in its space.
    private sealed class PingSurface
    {
        private readonly MeshRenderer renderer;
        private readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        private readonly List<PingRuntime> pings = new List<PingRuntime>();
        private readonly bool reprojectLocal;
        private ComputeBuffer buffer;
        private bool dirty;

        /// <param name="reprojectLocal">True for the overlay surface: pings store a WORLD anchor and
        /// get re-projected into the quad's CURRENT local space on every upload (not just once at
        /// Add-time), so the quad can ride the camera every frame while the ring still tracks its
        /// world anchor instead of drifting once the camera pans/zooms mid-ping. False for the ground
        /// surface, whose quad never moves, so the world center it's given is uploaded as-is.</param>
        public PingSurface(MeshRenderer renderer, bool reprojectLocal = false)
        {
            this.renderer = renderer;
            this.reprojectLocal = reprojectLocal;
            if (renderer != null) renderer.enabled = false; // zero idle cost until something animates
        }

        public bool Valid => renderer != null;

        /// <summary>Queues a ping. center is WORLD space for both surfaces — the overlay surface
        /// re-projects it into the quad's local space itself, continuously (see reprojectLocal).</summary>
        public void Add(Vector3 center, float startTime, float lifetime, Color color)
        {
            if (renderer == null) return;
            pings.Add(new PingRuntime
            {
                center    = center,
                startTime = startTime,
                endTime   = startTime + lifetime,
                color     = color   // Color → Vector4 (implicit)
            });
            dirty = true;
        }

        /// <summary>Prune expired pings, then re-upload the buffer if anything changed. Every frame.</summary>
        public void Tick(float clock)
        {
            if (renderer == null) return;

            for (int i = pings.Count - 1; i >= 0; i--)
            {
                if (clock >= pings[i].endTime)
                {
                    pings.RemoveAt(i);
                    dirty = true;
                }
            }

            // The overlay quad rides the camera every frame, so its world→local mapping changes even
            // when no ping was added/removed this frame — keep re-uploading while anything is active.
            if (reprojectLocal && pings.Count > 0) dirty = true;

            if (dirty) Upload();
        }

        private void Upload()
        {
            dirty = false;
            int count = pings.Count;

            renderer.enabled = count > 0; // zero idle cost when nothing is animating

            if (count == 0) { Release(); return; }

            if (buffer == null || buffer.count != count)
            {
                Release();
                buffer = new ComputeBuffer(count, PingBufferStride);
            }

            var entries = new PingBufferEntry[count];
            for (int i = 0; i < count; i++)
            {
                entries[i].center    = reprojectLocal
                    ? renderer.transform.InverseTransformPoint(pings[i].center) // world -> quad's CURRENT local space
                    : pings[i].center;
                entries[i].startTime = pings[i].startTime;
                entries[i].color     = pings[i].color;
            }
            buffer.SetData(entries);

            mpb.SetBuffer(PingBufferId, buffer);
            mpb.SetInt(PingCountId, count);
            renderer.SetPropertyBlock(mpb);
        }

        public void Release()
        {
            if (buffer != null)
            {
                buffer.Release();
                buffer = null;
            }
        }
    }

    private PingSurface ground;
    private PingSurface overlay;

    private float clock; // script-owned, halts while RunManager.IsEventPaused

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        ground  = new PingSurface(groundQuadRenderer);
        overlay = new PingSurface(overlayQuadRenderer, reprojectLocal: true);

        if (!ground.Valid && !overlay.Valid)
        {
            Debug.LogError($"{name}: neither groundQuadRenderer nor overlayQuadRenderer is wired — " +
                           "PingDirector cannot render any ping. Assign at least one in the Inspector.", this);
            enabled = false;
        }
    }

    void Start()
    {
        if (RunManager.Instance == null)
        {
            Debug.LogError("[PingDirector] RunManager.Instance is null at Start — cannot subscribe to OnDayResolved. Day-resolution pings will never fire.", this);
        }
        else
        {
            RunManager.Instance.OnDayResolved += HandleDayResolved;
        }

        if (ActionManager.Instance == null)
        {
            Debug.LogError("[PingDirector] ActionManager.Instance is null at Start — cannot subscribe to OnActionCompleted. Landing-day pings will never fire.", this);
        }
        else
        {
            ActionManager.Instance.OnActionCompleted += HandleActionCompleted;
        }

        if (TileManager.Instance == null)
        {
            Debug.LogError("[PingDirector] TileManager.Instance is null at Start — burst sweeps have no tiles to scan.", this);
        }
    }

    void OnDestroy()
    {
        if (RunManager.Instance != null)
            RunManager.Instance.OnDayResolved -= HandleDayResolved;
        if (ActionManager.Instance != null)
            ActionManager.Instance.OnActionCompleted -= HandleActionCompleted;

        ground?.Release();
        overlay?.Release();

        if (Instance == this) Instance = null;
    }

    // The ONLY polling-shaped code in stream C: clock accumulation, halted behind the flag.
    void Update()
    {
        if (ground == null) return; // a duplicate instance (Awake returned before building surfaces) — being destroyed

        if (RunManager.Instance == null || !RunManager.Instance.IsEventPaused)
            clock += Time.deltaTime;

        Shader.SetGlobalFloat(ClockId, clock); // shared uniform — ping shader AND glow shader read this

        ground.Tick(clock);
        overlay.Tick(clock);
    }

    // ── Public API ─────────────────────────────────────────────────────────────────────────────
    /// <summary>One-shot single ping on the GROUND surface, anchored at a world position — works on
    /// empty/debris tiles, the anchor is a position, not an entity. Never staggered (starts
    /// immediately). Uses <see cref="defaultPingColor"/> (the Critical-warning red).</summary>
    public void PingAt(Vector3 worldPos)
    {
        if (!enabled) return;
        ground.Add(worldPos, clock, pingLifetimeSeconds, defaultPingColor);
        Debug.Log("PINGED AT LOCATION");
    }

    /// <summary>One-shot single ping on the GROUND surface in an explicit color. Anchored at a world
    /// position, never staggered.</summary>
    public void PingAt(Vector3 worldPos, Color color)
    {
        if (!enabled) return;
        ground.Add(worldPos, clock, pingLifetimeSeconds, color);
    }

    /// <summary>One-shot single ping on the OVERLAY (lens) surface — onboarding's worker pings.
    /// worldPos is a world-space anchor (e.g. a worker). Unlike the ground surface, PingSurface
    /// re-projects this world anchor into the overlay quad's LOCAL space itself, every frame (see
    /// PingSurface.reprojectLocal) — so the quad can keep riding the camera while the ring still
    /// tracks the anchor's true world/on-screen position instead of freezing at the spot the camera
    /// happened to be looking at when the ping started. No-op if the overlay quad isn't wired.</summary>
    public void PingOverlay(Vector3 worldPos, Color color)
    {
        if (!enabled || !overlay.Valid) return;
        overlay.Add(worldPos, clock, pingLifetimeSeconds, color);
    }

    // ── Burst triggers (GROUND surface only) ────────────────────────────────────────────────────
    /// <summary>Day-resolution burst — SUPPRESSED while an action is running (no pings on watched
    /// action days; the landing-day OnActionCompleted burst covers that case instead).</summary>
    void HandleDayResolved(int day)
    {
        if (ActionManager.Instance != null && ActionManager.Instance.IsActionRunning) return;
        BurstSweep();
    }

    /// <summary>Fires on the action's coroutine completing — clean finish OR abort: an
    /// interrupted action still produces its landing-day burst.</summary>
    void HandleActionCompleted(Tile tile, int daysElapsed) => BurstSweep();

    /// <summary>Sweeps every tile whose tracked tier is Critical AND whose current occupant is
    /// category Plant, staggering each resulting ping's start so a bad day reads as a rolling
    /// wave rather than one synchronized flash. Uses tile.lastTier (RunManager's own
    /// crossing-tracked tier, refreshed every EvaluateThresholds pass, which always runs before
    /// OnDayResolved / before an action's day-loop finishes) rather than recomputing thresholds
    /// here — one tier classifier, one place (S2).</summary>
    void BurstSweep()
    {
        if (!enabled || !ground.Valid) return;
        if (TileManager.Instance == null) return;

        int index = 0;
        foreach (Tile tile in TileManager.Instance.GetAllTiles())
        {
            if (tile.lastTier != Tier.Critical) continue;
            if (!(tile.entity is GenericTileEntity g) || g.def == null || g.def.category != EntityCategory.Plant) continue;

            Vector3 worldPos = TileManager.Instance.GridToWorldPosition(tile.gridPosition);
            float jitter = Random.Range(-staggerJitter, staggerJitter);
            float start = clock + baseStaggerInterval * index + jitter;
            ground.Add(worldPos, start, pingLifetimeSeconds, defaultPingColor);
            index++;
        }
    }
}
