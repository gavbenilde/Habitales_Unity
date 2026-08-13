using System;
using System.Collections;
using UnityEngine;
using FMOD.Studio;
using FMODUnity;

public class DayNightCycleHandler : MonoBehaviour
{
    [SerializeField] private GameObject directionalLight;

    // ── Cycle timing (2026-07-17 rework: sliders replace the readonly constants) ─
    [Header("Cycle Timing")]
    [Tooltip("Seconds one full day-night rotation takes on the FIRST day of an action. " +
             "Lower = faster days from the start.")]
    [Range(0.25f, 10f)]
    [SerializeField] private float baseDayDuration = 3f;

    [Tooltip("Exponential speed-up per consecutive action day: day N lasts " +
             "baseDayDuration / speedMultiplier^N. 1 = every day the same length; " +
             "2 = each day twice as fast as the previous one.")]
    [Range(1f, 5f)]
    [SerializeField] private float speedMultiplier = 2f;

    [Tooltip("Hard floor for a single cycle — no day ever spins faster than this, " +
             "no matter how high the multiplier compounds.")]
    [Range(0.05f, 2f)]
    [SerializeField] private float minDuration = 0.3125f;

    // ── Sun tint for UNLIT art (2026-07-28) ──────────────────────────────────
    // The light below only reaches Lit shaders. Unlit art (Spine walkers, sprite rigs) reads
    // SunSignal instead and multiplies the tint into its own colour channel. See SunSignal.cs.
    [Header("Sun Tint (unlit art)")]
    [Tooltip("Publish SunSignal.Daylight/Tint each frame so unlit art (the Spine workers, sprite " +
             "rigs) darkens with the cycle like the Lit tiles already do. Off = unlit art stays at " +
             "full brightness, i.e. the pre-2026-07-28 behaviour.")]
    [SerializeField] private bool driveSunTint = true;

    [Tooltip("Tint unlit art multiplies by at full night. Not black on purpose — the tiles keep " +
             "some ambient at night, so a pure-black walker would read as a hole rather than a " +
             "silhouette. Cool/blue sells moonlight.")]
    [SerializeField] private Color nightTint = new Color(0.34f, 0.40f, 0.58f, 1f);

    [Tooltip("Tint unlit art multiplies by at the sun's zenith. White = the art's authored colours.")]
    [SerializeField] private Color dayTint = Color.white;

    [Tooltip("How much of the directional light's own colour and intensity to fold into the tint. " +
             "AtmosphereDirector drives those per weather, so at 1 a storm dims the walkers along " +
             "with everything else; 0 keeps the tint purely a function of the sun's angle.")]
    [Range(0f, 1f)]
    [SerializeField] private float lightColorInfluence = 1f;

    private Coroutine currentCycle;
    private int actionDayIndex = 0;

    private Quaternion _dayStartRotation;
    private bool _subscribed;
    private Light _light;   // the Light on directionalLight, for colour/intensity; may be null
    
    [Header("Audio")]
    [SerializeField] private EventReference timeWoosh;
    
    private EventInstance timeWooshInstance;
    private bool timeWooshPlaying;

    // ── Idle gate ────────────────────────────────────────────────────────────
    // ResourceManager.AdvanceTimeStepped yields on this.
    // Fixed: was never set false, so the WaitUntil resolved immediately.
    public static bool IsIdle { get; private set; } = true;

    // Force the gate back to idle. Used by RunRestart: if a restart lands mid-cycle (an action
    // running, an event interrupt, etc.) IsIdle could be latched false with the coroutine that
    // would have flipped it back destroyed by the scene reload — the next run's first
    // AdvanceTimeStepped would then hang forever on WaitUntil(IsIdle). Not used mid-run.
    public static void ForceIdle() => IsIdle = true;

    // Scene-singleton convenience (one handler per scene) — not a manager, no init-order
    // pin. Lets ActionManager resolve it without FindObjectOfType.
    public static DayNightCycleHandler Instance { get; private set; }

    public event Action<int> OnCycleEnd;

    void Awake()
    {
        Instance = this;

        if (directionalLight == null)
        {
            // Loud, not fatal: StartCycle degrades to instant cycles so the game
            // clock never deadlocks on a missing scene reference.
            Debug.LogError("DayNightCycleHandler: 'Directional Light' is not assigned — " +
                           "day/night rotation disabled, days will resolve instantly.", this);
            return;
        }

        _dayStartRotation = directionalLight.transform.rotation;
        _light = directionalLight.GetComponent<Light>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Runs every frame, not just during a cycle: the sun sits at a fixed angle between actions and
    // unlit art still has to match THAT angle, and AtmosphereDirector can change the light's colour
    // on a weather flip while no cycle is running.
    void Update()
    {
        if (!driveSunTint || directionalLight == null) return;

        // Exactly the Lambert term a flat, upward-facing surface gets from this light, so walkers
        // track the ground they stand on. Negative (sun below the horizon) clamps to full night.
        float daylight = Mathf.Clamp01(Vector3.Dot(-directionalLight.transform.forward, Vector3.up));

        Color tint = Color.Lerp(nightTint, dayTint, daylight);

        if (_light != null && lightColorInfluence > 0f)
        {
            // Intensity is folded in as a multiplier clamped at 1 so the normal 1.0 case is a no-op
            // and only a director DIMMING the light (storm) darkens the art — a lightning flash
            // boosting intensity above 1 must not blow the walkers out to white.
            Color lightTerm = _light.color * Mathf.Clamp01(_light.intensity);
            tint *= Color.Lerp(Color.white, lightTerm, lightColorInfluence);
        }

        tint.a = 1f; // tint, never opacity — see SunSignal.Tint
        SunSignal.Daylight = daylight;
        SunSignal.Tint = tint;
    }

    // Subscription is attempted twice: OnEnable (normal path) and Start (safety net for
    // any enable-order edge where ResourceManager.Instance wasn't up yet). The old
    // one-shot OnEnable check failed SILENTLY when it lost that race — the cycle then
    // never ran and days blasted through with no sun movement.
    void OnEnable() => TrySubscribe(warnIfMissing: false);

    void Start()
    {
        TrySubscribe(warnIfMissing: true);
    }

    private void TrySubscribe(bool warnIfMissing)
    {
        if (_subscribed) return;

        if (ResourceManager.Instance == null)
        {
            if (warnIfMissing)
                Debug.LogWarning("DayNightCycleHandler: no ResourceManager.Instance by Start() — " +
                                 "day/night cycle will never trigger in this scene.", this);
            return;
        }

        ResourceManager.Instance.OnTimeAdvanced += StartCycle;
        _subscribed = true;
    }

    void OnDisable()
    {
        if (_subscribed && ResourceManager.Instance != null)
            ResourceManager.Instance.OnTimeAdvanced -= StartCycle;
        _subscribed = false;

        TimeFlowSignal.SpeedFactor = 1f; // a disabled handler kills its coroutine — never leave the factor stuck high
        SunSignal.Reset();               // ...and never leave unlit art stuck at midnight with nothing left to brighten it
    }

    // Called by ActionManager once before the first day of a new action.
    // Resets the duration so day 1 always starts at full baseDayDuration.
    public void ResetForNewAction()
    {
        actionDayIndex = 0;
    }

    public void StartCycle(int cycles)
    {
        if (currentCycle != null)
            StopCoroutine(currentCycle);

        // Missing light (or inactive GO — StartCoroutine would throw): resolve the cycles
        // instantly instead of latching IsIdle false and hanging AdvanceTimeStepped forever.
        if (directionalLight == null || !gameObject.activeInHierarchy)
        {
            IsIdle = true;
            for (int i = 0; i < cycles; i++) OnCycleEnd?.Invoke(i + 1);
            return;
        }

        StartTimeWoosh();
        
        IsIdle = false;   // ← was missing; caused AdvanceTimeStepped to never wait
        currentCycle = StartCoroutine(RunCycles(cycles));
    }

    private IEnumerator RunCycles(int cycles)
    {
        // try/finally so the idle gate and speed factor ALWAYS recover — even if the
        // coroutine is stopped (StartCycle, deactivation) or a subscriber throws.
        // A dead coroutine leaving IsIdle latched false deadlocks the whole action clock.
        try
        {
            for (int i = 0; i < cycles; i++)
            {
                // Sliders are read fresh each day, so both are live-tunable in Play mode.
                float currentDuration = Mathf.Max(
                    baseDayDuration / Mathf.Pow(Mathf.Max(1f, speedMultiplier), actionDayIndex),
                    minDuration
                );

                // Publish the effective time-lapse factor (1..base/min) — atmosphere FX scroll faster with it.
                TimeFlowSignal.SpeedFactor = baseDayDuration / currentDuration;

                float elapsed = 0f;
                float rotationSpeed = 360f / currentDuration;

                while (elapsed < currentDuration)
                {
                    float delta = Time.deltaTime;
                    directionalLight.transform.Rotate(Vector3.right * (rotationSpeed * delta));
                    elapsed += delta;
                    yield return null;
                }

                // Snap back to exact start angle — kills any float drift from delta accumulation
                directionalLight.transform.rotation = _dayStartRotation;

                actionDayIndex++;
                OnCycleEnd?.Invoke(i + 1);
            }
        }
        finally
        {
            currentCycle = null;
            TimeFlowSignal.SpeedFactor = 1f; // time-lapse over — atmosphere FX ease back to real time
            IsIdle = true;
            
            StopTimeWoosh();
        }
    }
    
    private void StartTimeWoosh()
    {
        if (timeWooshPlaying)
            return;

        timeWooshInstance = RuntimeManager.CreateInstance(timeWoosh);
        timeWooshInstance.start();

        timeWooshPlaying = true;
    }

    private void StopTimeWoosh()
    {
        if (!timeWooshPlaying)
            return;

        timeWooshInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        timeWooshInstance.release();

        timeWooshPlaying = false;
    }
}
