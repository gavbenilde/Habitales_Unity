using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Habitales.Entities;

// PingDirector — Azi tier system.
//
// Owns ONE giant ground-projected quad and bursts expanding "ring" pings from every dying
// (Critical-tier, Plant-category) tile at day-resolution / action-landing, plus one-shot pings
// for stream A's T3 death popups. The quad's MeshRenderer is disabled whenever no ping is
// currently animating — zero idle cost.
//
// UNCAPPED PING COUNT: every active ping (center + start time) lives in a plain List that grows
// as bursts/one-shots add to it and shrinks as pings finish their lifetime. The GPU-side
// StructuredBuffer is (re)sized to that list's actual count on every change — never a fixed
// array. The Shader Graph ring shader loops the buffer per-pixel and combines overlapping rings
// with max() (never +) — see PingRingShaderSpec.md / PingRingFunction.hlsl.
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

    [Header("Ground Quad (required)")]
    [Tooltip("The single oversized ground-projected quad that renders every active ping ring. " +
             "Its MeshRenderer is disabled whenever no ping is animating (zero idle cost).")]
    [SerializeField] private MeshRenderer groundQuadRenderer;

    [Header("Stagger (burst sweeps only — PingAt is never staggered)")]
    [Tooltip("Base seconds between one burst-ping's start and the next, in director-clock time.")]
    [SerializeField] private float baseStaggerInterval = 0.12f;
    [Tooltip("Random +/- jitter added to each burst-ping's start offset, in director-clock seconds.")]
    [SerializeField] private float staggerJitter = 0.05f;

    [Header("Lifecycle")]
    [Tooltip("How long (director-clock seconds) a single ping stays in the active buffer before " +
             "this script prunes it and (if it was the last one) disables the ground quad. This is " +
             "a C#-side bookkeeping value only — it does not draw anything itself. Keep it roughly " +
             "matched to the shader material's own Ring Max Radius / Ring Speed (radius / speed = " +
             "seconds for the ring to finish) so the quad doesn't go dark mid-fade or stay lit idle " +
             "after the shader's own ring math has already faded to nothing. Tune in play mode " +
             "alongside the material — see PingRingShaderSpec.md.")]
    [SerializeField] private float pingLifetimeSeconds = 2.5f;

    // ── GPU buffer ───────────────────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct PingBufferEntry
    {
        public Vector3 center;     // world-space ring origin
        public float   startTime;  // director-clock time this ping begins expanding
    }
    private const int PingBufferStride = 16; // 3 floats + 1 float = 16 bytes, matches the .hlsl struct

    private static readonly int PingBufferId = Shader.PropertyToID("_PingBuffer");
    private static readonly int PingCountId  = Shader.PropertyToID("_PingCount");
    private static readonly int ClockId      = Shader.PropertyToID("_HabitalesClock"); // GLOBAL — shared with the glow shader

    private ComputeBuffer pingBuffer;
    private MaterialPropertyBlock propertyBlock;

    private struct PingRuntime
    {
        public Vector3 center;
        public float   startTime;
        public float   endTime;
    }
    private readonly List<PingRuntime> activePings = new List<PingRuntime>();
    private bool bufferDirty;

    private float clock; // script-owned, halts while RunManager.IsEventPaused

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (groundQuadRenderer == null)
        {
            Debug.LogError($"{name}: groundQuadRenderer is not wired — PingDirector cannot render any ping. Assign the ground quad's MeshRenderer in the Inspector.", this);
            enabled = false;
            return;
        }

        propertyBlock = new MaterialPropertyBlock();
        groundQuadRenderer.enabled = false; // zero idle cost — nothing is animating yet
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

        ReleaseBuffer();

        if (Instance == this) Instance = null;
    }

    // The ONLY polling-shaped code in stream C: clock accumulation, halted behind the flag.
    void Update()
    {
        if (RunManager.Instance == null || !RunManager.Instance.IsEventPaused)
            clock += Time.deltaTime;

        Shader.SetGlobalFloat(ClockId, clock); // shared uniform — ping shader AND glow shader read this

        PruneExpiredPings();

        if (bufferDirty)
            UploadBuffer();
    }

    // ── Public API (parallel agents compile against this exact surface) ────────────────────
    /// <summary>One-shot single ping anchored at a world position — works on empty/debris tiles,
    /// the anchor is a position, not an entity. Never staggered (starts immediately).</summary>
    public void PingAt(Vector3 worldPos)
    {
        if (!enabled) return; // groundQuadRenderer missing — already loud-failed in Awake
        AddPing(worldPos, clock);
    }

    // ── Burst triggers ───────────────────────────────────────────────────────────────────────
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
        if (!enabled) return;
        if (TileManager.Instance == null) return;

        int index = 0;
        foreach (Tile tile in TileManager.Instance.GetAllTiles())
        {
            if (tile.lastTier != Tier.Critical) continue;
            if (!(tile.entity is GenericTileEntity g) || g.def == null || g.def.category != EntityCategory.Plant) continue;

            Vector3 worldPos = TileManager.Instance.GridToWorldPosition(tile.gridPosition);
            float jitter = Random.Range(-staggerJitter, staggerJitter);
            float start = clock + baseStaggerInterval * index + jitter;
            AddPing(worldPos, start);
            index++;
        }
    }

    // ── Active-ping bookkeeping ──────────────────────────────────────────────────────────────
    void AddPing(Vector3 worldPos, float startTime)
    {
        activePings.Add(new PingRuntime
        {
            center    = worldPos,
            startTime = startTime,
            endTime   = startTime + pingLifetimeSeconds
        });
        bufferDirty = true;
    }

    void PruneExpiredPings()
    {
        bool removedAny = false;
        for (int i = activePings.Count - 1; i >= 0; i--)
        {
            if (clock >= activePings[i].endTime)
            {
                activePings.RemoveAt(i);
                removedAny = true;
            }
        }
        if (removedAny) bufferDirty = true;
    }

    void UploadBuffer()
    {
        bufferDirty = false;
        int count = activePings.Count;

        groundQuadRenderer.enabled = count > 0; // zero idle cost when nothing is animating

        if (count == 0)
        {
            ReleaseBuffer();
            return;
        }

        if (pingBuffer == null || pingBuffer.count != count)
        {
            ReleaseBuffer();
            pingBuffer = new ComputeBuffer(count, PingBufferStride);
        }

        var entries = new PingBufferEntry[count];
        for (int i = 0; i < count; i++)
        {
            entries[i].center    = activePings[i].center;
            entries[i].startTime = activePings[i].startTime;
        }
        pingBuffer.SetData(entries);

        propertyBlock.SetBuffer(PingBufferId, pingBuffer);
        propertyBlock.SetInt(PingCountId, count);
        groundQuadRenderer.SetPropertyBlock(propertyBlock);
    }

    void ReleaseBuffer()
    {
        if (pingBuffer != null)
        {
            pingBuffer.Release();
            pingBuffer = null;
        }
    }
}
