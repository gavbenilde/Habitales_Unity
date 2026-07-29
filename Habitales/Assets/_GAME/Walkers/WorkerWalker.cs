using System.Collections;
using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;
using UnityEngine.VFX;

// Binds 1:1 to a ResourceManager Worker record. Adds two things on top of the base Walker's
// roaming: the Working state (a flight animation to a work tile) and a colour adjustment reflecting
// Worker.isFatigued. This component never writes worker data, only reads it — Walkers
// represent existing Workers, they don't create new ones.
[DisallowMultipleComponent]
public class WorkerWalker : Walker
{
    [Header("Exhausted Visual (worker-only)")]
    [Tooltip("Hue rotation in degrees applied over the whole Spine skeleton while Worker.isFatigued " +
             "is true. Same units as an image editor's Hue/Saturation dialog, and the adjustment is " +
             "done in HSL to match it. A rotation is NOT expressible as a tint, which is why this " +
             "runs in the shader rather than through Skeleton.R/G/B or a _Color multiply — see the " +
             "`Spine/Skeleton HSL Adjust` shader the worker materials are on.")]
    [SerializeField, Range(-180f, 180f)] private float fatigueHue = -150f;
    [Tooltip("Saturation percentage, −100 (fully grey) to +100. Negative scales the existing " +
             "saturation: −80 leaves a fifth of it.")]
    [SerializeField, Range(-100f, 100f)] private float fatigueSaturation = -80f;
    [Tooltip("Lightness percentage, −100 (black) to +100 (white). 0 leaves lightness alone.")]
    [SerializeField, Range(-100f, 100f)] private float fatigueLightness = 0f;

    // Shader uniforms on Spine/Skeleton HSL Adjust. Pushed per-walker through a
    // MaterialPropertyBlock so every worker keeps sharing the same two materials.
    private static readonly int HueID        = Shader.PropertyToID("_HueShift");
    private static readonly int SaturationID = Shader.PropertyToID("_Saturation");
    private static readonly int LightnessID  = Shader.PropertyToID("_Lightness");
    private static readonly int AmountID     = Shader.PropertyToID("_AdjustAmount");

    private enum Mode { Roaming, Working }
    private Mode mode = Mode.Roaming;

    private Worker worker;                 // the bound data record — read only
    private bool isGameOver;
    private bool isExhaustedVisual;

    // The renderer of every rig's skeleton, so the adjustment covers whichever facing is currently
    // shown. Taken from the SkeletonAnimations rather than a blanket GetComponentsInChildren
    // <MeshRenderer> so it can't pick up unrelated mesh art hung off the prefab later. Searched with
    // true (include inactive), matching the base Walker's keep-both-rigs-active rule.
    private MeshRenderer[] rigRenderers;
    private MaterialPropertyBlock propertyBlock;
    private bool warnedAboutShader;

    private VisualEffect activeWorkingVFX;
    private Coroutine flightRoutine;

    /// <summary>Called once by WalkerManager right after Instantiate, before Initialize.</summary>
    public void Bind(Worker boundWorker)
    {
        worker = boundWorker;
    }

    /// <summary>True while the bound Worker is fatigued (read-only mirror of Worker.isFatigued).
    /// Exposed so systems like onboarding can pick out the fatigued crew without touching worker data.</summary>
    public bool IsFatigued => worker != null && worker.isFatigued;

    protected override void Awake()
    {
        base.Awake();

        SkeletonAnimation[] skeletons = GetComponentsInChildren<SkeletonAnimation>(true);
        List<MeshRenderer> found = new List<MeshRenderer>(skeletons.Length);
        foreach (SkeletonAnimation sa in skeletons)
        {
            MeshRenderer mr = sa != null ? sa.GetComponent<MeshRenderer>() : null;
            if (mr != null) found.Add(mr);
        }
        rigRenderers = found.ToArray();
    }

    /// <summary>Bind runs between Instantiate and Initialize, so Awake is too early to read the
    /// worker — this is the first point where a walker spawned onto an ALREADY fatigued worker can
    /// be pushed into the exhausted look, instead of waiting for the next action to complete.</summary>
    public override void Initialize(WalkerProfileSO walkerProfile, Tile startTile)
    {
        base.Initialize(walkerProfile, startTile);
        SyncExhaustedVisual(force: true);
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
            GroundWalker();
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
            GroundWalker();
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

    /// <summary>Drops the walker back to ground height. Needed on every path that ends Working WITHOUT
    /// starting a new flight — killing the flight coroutine mid-arc leaves the walker stranded at
    /// whatever height it had reached, and the roam loop only walks it back down over its next move,
    /// so an idle worker would visibly hover. EnterWorking deliberately does NOT call this: a
    /// re-scatter's new arc starts from the current airborne position and lerps down to the new tile,
    /// which reads as continuous flight rather than a drop-then-relaunch.</summary>
    private void GroundWalker()
    {
        if (manager == null) return; // teardown — nothing to read the ground height from
        Vector3 pos = transform.position;
        pos.y = manager.WalkerY;
        transform.position = pos;
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

    private void SyncExhaustedVisual(bool force = false)
    {
        bool fatigued = worker != null && worker.isFatigued;
        if (fatigued == isExhaustedVisual && !force) return; // avoid redundant renderer writes
        isExhaustedVisual = fatigued;
        ApplyExhaustedAdjust(fatigued);
    }

    /// <summary>
    /// Pushes the Hue/Saturation/Lightness adjustment to every rig renderer through a
    /// MaterialPropertyBlock, with the amount as the on/off switch.
    ///
    /// A property block rather than a material instance so all workers keep sharing the two
    /// authored materials — per-instance materials would break their batching and leak a clone per
    /// walker. It survives the front/back renderer toggle (which only touches Renderer.enabled) and
    /// doesn't collide with SkeletonRenderer's own property-block use, which is scoped to
    /// per-submesh draw-order fixing and reads the per-renderer block back in before writing.
    ///
    /// The base Walker's ArtTint/sun-tint channel is untouched by all of this: that one still rides
    /// the skeleton's vertex colour, so a fatigued worker at night reads as both.
    /// </summary>
    private void ApplyExhaustedAdjust(bool fatigued)
    {
        if (rigRenderers == null || rigRenderers.Length == 0) return;
        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();

        foreach (MeshRenderer mr in rigRenderers)
        {
            if (mr == null) continue;

            // The adjustment lives in the shader, so a material left on plain Spine/Skeleton would
            // swallow these writes in silence — exactly how the previous attempt at this failed.
            // Say so once instead.
            if (fatigued && !warnedAboutShader && mr.sharedMaterial != null && !mr.sharedMaterial.HasProperty(AmountID))
            {
                warnedAboutShader = true;
                Debug.LogWarning($"{name}: material '{mr.sharedMaterial.name}' has no _AdjustAmount — " +
                                 "put it on the 'Spine/Skeleton HSL Adjust' shader or the fatigued " +
                                 "look will not appear.", this);
            }

            mr.GetPropertyBlock(propertyBlock); // keep anything else already in the block
            propertyBlock.SetFloat(HueID,        fatigueHue);
            propertyBlock.SetFloat(SaturationID, fatigueSaturation);
            propertyBlock.SetFloat(LightnessID,  fatigueLightness);
            propertyBlock.SetFloat(AmountID,     fatigued ? 1f : 0f);
            mr.SetPropertyBlock(propertyBlock);
        }
    }
}
