using System.Collections;
using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;
using UnityEngine.VFX;

// Binds 1:1 to a ResourceManager Worker record. Adds two things on top of the base Walker's
// roaming: the Working state (a flight animation to a work tile) and a visual tint reflecting
// Worker.isFatigued. This component never writes worker data, only reads it — Walkers
// represent existing Workers, they don't create new ones.
[DisallowMultipleComponent]
public class WorkerWalker : Walker
{
    [Header("Exhausted Visual (worker-only)")]
    [Tooltip("Tint multiplied over the whole Spine skeleton while Worker.isFatigued is true, via the " +
             "skeleton's vertex color (Skeleton.R/G/B/A). Kept as a multiply — grays/dims the worker. " +
             "This is NOT a material property: the Spine/Skeleton shader has no _BaseColor/_Color, so the " +
             "old MaterialPropertyBlock tint never took; vertex color is Spine's supported channel.")]
    [SerializeField] private Color desaturatedTint = new Color(0.55f, 0.55f, 0.55f, 1f);

    private enum Mode { Roaming, Working }
    private Mode mode = Mode.Roaming;

    private Worker worker;                 // the bound data record — read only
    private bool isGameOver;
    private bool isExhaustedVisual;

    // Both facing rigs' skeletons, so the tint tracks whichever one is currently shown. Found in
    // children (true = include inactive), matching the base Walker's keep-both-rigs-active rule.
    private SkeletonAnimation[] skeletons;

    private VisualEffect activeWorkingVFX;
    private Coroutine flightRoutine;

    /// <summary>Called once by WalkerManager right after Instantiate, before Initialize.</summary>
    public void Bind(Worker boundWorker)
    {
        worker = boundWorker;
    }

    protected override void Awake()
    {
        base.Awake();
        skeletons = GetComponentsInChildren<SkeletonAnimation>(true);
    }

    void OnEnable()
    {
        // These two events are subscribed directly rather than routed through WalkerManager's
        // fan-out — OnActionDayStarted/OnGameOverTriggered are fanned out by WalkerManager
        // instead (see NotifyActionDayStarted/NotifyGameOver below).
        if (ActionManager.Instance != null) ActionManager.Instance.OnActionCompleted += HandleActionCompleted;
        if (ResourceManager.Instance != null) ResourceManager.Instance.OnWorkerRecovered += HandleWorkerRecovered;
    }

    void OnDisable()
    {
        if (ActionManager.Instance != null) ActionManager.Instance.OnActionCompleted -= HandleActionCompleted;
        if (ResourceManager.Instance != null) ResourceManager.Instance.OnWorkerRecovered -= HandleWorkerRecovered;
    }

    protected override bool CanRoamThisFrame() => mode == Mode.Roaming;

    protected override float EffectiveMoveSpeed()
    {
        float speed = profile.moveSpeed;
        if (worker != null && worker.isFatigued) speed *= profile.fatiguedSpeedMultiplier;
        return speed;
    }

    // ── Fanned out by WalkerManager ──────────────────────────────────────────────

    /// <summary>WalkerManager calls this for every WorkerWalker on ActionManager.OnActionDayStarted.
    /// Non-fatigued workers enter (or re-target) Working to a random tile in the new batch;
    /// fatigued workers sit this batch out and keep roaming.</summary>
    public void NotifyActionDayStarted(IReadOnlyList<Tile> batch)
    {
        if (isGameOver) return; // nothing new starts after game-over
        if (batch == null || batch.Count == 0) return;
        if (worker != null && worker.isFatigued) return; // fatigued workers don't enter Working

        Tile target = batch[Random.Range(0, batch.Count)];
        EnterWorking(target);
    }

    /// <summary>WalkerManager calls this for every WorkerWalker on RunManager.OnGameOverTriggered.
    /// Working decays to Roaming permanently; nothing new starts.</summary>
    public void NotifyGameOver()
    {
        isGameOver = true;
        if (mode == Mode.Working)
        {
            CancelFlight();
            StopWorkingVFX();
            mode = Mode.Roaming;
            ResetToIdle();
            SetState(WalkerState.Idle); // hand animation back to the base motion derivation
        }
    }

    // ── Direct subscriptions ─────────────────────────────────────────────────

    // Fires on a clean finish AND an abort (ActionManager.FinalizeAction) — both mean "go home".
    // Fatigue (ApplyFatigue) is applied earlier in that same method, so worker.isFatigued is
    // already final by the time this runs — the exhausted-visual sync below reads the settled value.
    private void HandleActionCompleted(Tile _, int __)
    {
        if (mode == Mode.Working)
        {
            CancelFlight();
            StopWorkingVFX();
            mode = Mode.Roaming;
            ResetToIdle(); // re-enter Roaming from the walker's current position
            SetState(WalkerState.Idle);
        }
        SyncExhaustedVisual();
    }

    private void HandleWorkerRecovered(Worker recovered)
    {
        if (recovered == worker) SyncExhaustedVisual(); // clears the fatigued tint when this worker recovers
    }

    // ── Working / flight ────────────────────────────────────────────────────────────────────

    private void EnterWorking(Tile target)
    {
        // Working claims freely into the shared batch tile — no empty-check, since multiple
        // workers can be sent to the same tile. Claim moves at decision time.
        manager.ReleaseClaim(this, currentTile);
        manager.ClaimTile(this, target);
        currentTile = target;

        StopWorkingVFX(); // stop the previous tile's smoke on a mid-action re-scatter
        mode = Mode.Working;

        CancelFlight();
        flightRoutine = StartCoroutine(FlightToTile(target));
    }

    private void CancelFlight()
    {
        if (flightRoutine != null)
        {
            StopCoroutine(flightRoutine);
            flightRoutine = null;
        }
    }

    /// <summary>Straight-line, walkability-ignoring, constant-duration parabolic lerp.
    /// Staggered launch so a batch of workers scrambles instead of launching in lockstep.</summary>
    private IEnumerator FlightToTile(Tile target)
    {
        // State is deliberately left alone during the stagger delay — the walker is standing still
        // waiting its turn to launch, so the base derivation reading that as Idle is correct.
        float delay = Random.Range(0f, profile.flightStaggerMax);
        if (delay > 0f) yield return new WaitForSeconds(delay);

        SetState(WalkerState.Flying);

        Vector3 start = transform.position;
        Vector3 end = DeviatedPosition(target, profile.workDeviation);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(profile.flightDuration, 0.01f);
            float clamped = Mathf.Clamp01(t);
            Vector3 pos = Vector3.Lerp(start, end, clamped);
            pos.y += profile.arcCurve.Evaluate(clamped) * profile.arcHeight;
            transform.position = pos;
            yield return null;
        }

        transform.position = end;
        flightRoutine = null;
        SetState(WalkerState.Working); // landed — holds until OnActionCompleted, even though we're now stationary
        PlayWorkingVFX(target);
    }

    private void PlayWorkingVFX(Tile at)
    {
        if (string.IsNullOrEmpty(profile.workingVfxKey) || VFXManager.Instance == null) return;
        Vector3 pos = TileManager.Instance.GridToWorldPosition(at.gridPosition);
        activeWorkingVFX = VFXManager.Instance.SpawnVFX(profile.workingVfxKey, pos);
    }

    private void StopWorkingVFX()
    {
        if (activeWorkingVFX == null) return;
        if (VFXManager.Instance != null) VFXManager.Instance.DestroyVFX(activeWorkingVFX);
        activeWorkingVFX = null;
    }

    // ── Exhausted visual (reads Worker.isFatigued; no new fatigue system) ──────────────────────

    private void SyncExhaustedVisual()
    {
        bool fatigued = worker != null && worker.isFatigued;
        if (fatigued == isExhaustedVisual) return; // avoid redundant skeleton-color writes
        isExhaustedVisual = fatigued;
        ApplyTint(fatigued ? desaturatedTint : Color.white);
    }

    /// <summary>Writes the tint into every rig's Spine skeleton vertex color. SkeletonRenderer bakes
    /// this into the mesh on its next update, so it survives the front/back renderer toggling and
    /// doesn't fight SkeletonRenderer's own MaterialPropertyBlock usage.</summary>
    private void ApplyTint(Color tint)
    {
        if (skeletons == null) return;
        foreach (SkeletonAnimation sa in skeletons)
        {
            if (sa == null || sa.Skeleton == null) continue;
            sa.Skeleton.R = tint.r;
            sa.Skeleton.G = tint.g;
            sa.Skeleton.B = tint.b;
            sa.Skeleton.A = tint.a;
        }
    }
}
