using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// AtmosphereDirector — the single per-frame pump for the atmosphere layer
/// (ATMOSPHERE_BUILD_PLAN.md). Purely cosmetic (arch S3): it READS WeatherManager /
/// ResourceManager state every frame and never writes sim state. Polling (not events) by
/// design — every output is a continuous lerp toward a target derived from current state,
/// so there is no edge to miss and no init-order coupling.
///
/// Owns, per frame:
///  • TimeFlowSignal smoothing + integration → `_WeatherTime` shader global (all scrolling FX),
///  • per-weather directional-light intensity/color + the active weather's grade
///    VolumeProfile swapped into the SINGLE scene Global Volume (<see cref="AmbienceProfile"/>
///    table, same data-driven pattern as WeatherProfile),
///  • cloud-shadow globals (`_CloudShadowStrength`, `_CloudPhase`, `_CloudDir`, `_CloudScale`)
///    — phase is INTEGRATED here (speed × smoothed factor), never computed as time × speed,
///  • heat-haze gating: `_HazeStrength` global + renderer-feature toggle. Active only while
///    Sunny × Dry season; escalates while a drought is active,
///  • `_HorizonColor` global (the fog color) and the camera clear color (the backdrop),
///    now two INDEPENDENT fields — the edge fog and the sky behind it can be tuned apart,
///  • lightning flash boost (<see cref="FlashLight"/> — LightningDirector calls in; the boost
///    decays HERE, on top of the lerped intensity, so the ambience lerp and the flash never
///    fight over Light.intensity).
///
/// Division of labour with DayNightCycleHandler: the cycle handler ROTATES the directional
/// light; this director owns its INTENSITY and COLOR. Neither may touch the other's channel.
///
/// Wire-up (Law 3): one per scene. Assign the directional light, main camera, the single
/// scene Global Volume, four AmbienceProfiles (each with its own grade VolumeProfile asset),
/// and the HeatHaze renderer feature.
/// Missing profiles fall back to coded defaults with a warning; missing refs warn once.
/// </summary>
[DefaultExecutionOrder(-50)] // after the -200/-150 managers, before default-order scripts
public class AtmosphereDirector : MonoBehaviour
{
    public static AtmosphereDirector Instance { get; private set; }

    [Serializable]
    public class AmbienceProfile
    {
        public WeatherState state;

        [Header("Light & grade")]
        [Tooltip("Directional light intensity while this weather is active (director-owned channel).")]
        public float lightIntensity = 1f;
        public Color lightColor = Color.white;
        [Tooltip("VolumeProfile (Color Adjustments etc.) swapped into the single scene Global Volume " +
                 "while this weather is active. Author one profile asset per weather; blend " +
                 "post-exposure/saturation/temperature there.")]
        public VolumeProfile grade;

        [Header("Cloud shadows")]
        [Range(0f, 1f)]
        [Tooltip("How dark the scrolling cloud shadows get. Cloudy ~0.30, Sunny ~0.05, Rainy ~0.15, Stormy ~0.20.")]
        public float cloudShadowStrength = 0.1f;
        [Tooltip("Cloud drift speed in world units/second BEFORE time-lapse scaling.")]
        public float cloudSpeed = 0.4f;
    }

    [Header("Scene refs")]
    [SerializeField] private Light directionalLight;
    [SerializeField] private Camera mainCamera;
    [Tooltip("The single scene 'Global Volume'. This director swaps the active weather's grade " +
             "profile into it and fades its weight — one volume, one profile at a time.")]
    [SerializeField] private Volume gradeVolume;

    [Header("Ambience (one profile per WeatherState — coded defaults fill gaps, with a warning)")]
    [SerializeField] private List<AmbienceProfile> profiles = new List<AmbienceProfile>();
    [Tooltip("Seconds for the light/Volume crossfade after a weather change.")]
    [SerializeField] private float transitionSeconds = 1.5f;

    [Header("Horizon & backdrop (two independent colors)")]
    [Tooltip("Fog color — the fully-fogged pixels of the FogOfWar quad render in this (via the " +
             "_HorizonColor shader global). This is the 'vision ends here' color at the world's edge.")]
    [SerializeField] private Color horizonColor = new Color(0.76f, 0.78f, 0.80f);
    [Tooltip("Camera background (clear) color — the sky/backdrop behind everything, seen above the " +
             "fog horizon. Kept separate from the fog so the two can be tuned apart.")]
    [SerializeField] private Color backgroundColor = new Color(0.76f, 0.78f, 0.80f);

    [Header("Time flow")]
    [Tooltip("Seconds to ease SmoothedSpeedFactor toward the raw factor — the 2× steps read as acceleration.")]
    [SerializeField] private float speedSmoothTime = 0.2f;

    [Header("Cloud shadows (shared shader globals)")]
    [Tooltip("World-to-noise scale. Smaller = bigger cloud blobs.")]
    [SerializeField] private float cloudScale = 0.045f;
    [Tooltip("Drift direction on the world XZ plane (normalized at runtime).")]
    [SerializeField] private Vector2 cloudDirection = new Vector2(1f, 0.35f);

    [Header("Heat haze (Sunny × Dry season only)")]
    [Tooltip("The Full Screen Pass Renderer Feature running HeatHaze.shader. Toggled off entirely " +
             "outside Sunny×Dry so it costs nothing.")]
    [SerializeField] private ScriptableRendererFeature heatHazeFeature;
    [Range(0f, 1f)]
    [SerializeField] private float hazeStrength = 0.5f;
    [Tooltip("Haze multiplier while a drought is active — the drought becomes something you SEE.")]
    [SerializeField] private float droughtHazeMultiplier = 1.5f;
    [SerializeField] private float hazeFadeSeconds = 2f;

    // ── Shader global IDs ─────────────────────────────────────────────────────
    private static readonly int WeatherTimeId         = Shader.PropertyToID("_WeatherTime");
    private static readonly int CloudShadowStrengthId = Shader.PropertyToID("_CloudShadowStrength");
    private static readonly int CloudPhaseId          = Shader.PropertyToID("_CloudPhase");
    private static readonly int CloudDirId            = Shader.PropertyToID("_CloudDir");
    private static readonly int CloudScaleId          = Shader.PropertyToID("_CloudScale");
    private static readonly int HazeStrengthId        = Shader.PropertyToID("_HazeStrength");
    private static readonly int HorizonColorId        = Shader.PropertyToID("_HorizonColor");

    // ── Runtime state ─────────────────────────────────────────────────────────
    private Dictionary<WeatherState, AmbienceProfile> _byState;
    private float _currentIntensity;
    private Color _currentLightColor;
    private float _currentCloudStrength;
    private float _currentCloudSpeed;
    private float _cloudPhase;          // integrated world-units scroll distance
    private float _currentHaze;
    private float _flashBoost;          // lightning, decays every frame
    private float _flashDecayRate;
    private bool  _hazeFeatureActive;
    private bool  _warnedNoLight;
    private VolumeProfile _assignedGrade;   // profile currently on the Global Volume
    private bool  _gradeInit;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildProfileTable();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        // Never leave a stale haze pass running in edit mode / next scene.
        if (heatHazeFeature != null) heatHazeFeature.SetActive(false);
        Shader.SetGlobalFloat(HazeStrengthId, 0f);
    }

    void Start()
    {
        // Snap to the current weather so scene start has no visible crossfade.
        var p = ActiveProfile();
        _currentIntensity     = p.lightIntensity;
        _currentLightColor    = p.lightColor;
        _currentCloudStrength = p.cloudShadowStrength;
        _currentCloudSpeed    = p.cloudSpeed;

        // Snap the Global Volume to the current weather's grade — no start-of-scene fade.
        if (gradeVolume != null)
        {
            _assignedGrade = p.grade;
            gradeVolume.sharedProfile = _assignedGrade;
            gradeVolume.weight = _assignedGrade != null ? 1f : 0f;
            _gradeInit = true;
        }

        ApplyLight();
        PushHorizon();

        if (heatHazeFeature != null) heatHazeFeature.SetActive(false);
        _hazeFeatureActive = false;
        Shader.SetGlobalFloat(HazeStrengthId, 0f);
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // ── 1. Time flow: smooth the raw factor, integrate the shared clock ──
        float smooth = speedSmoothTime > 0.0001f ? 1f - Mathf.Exp(-dt / speedSmoothTime) : 1f;
        TimeFlowSignal.SmoothedSpeedFactor =
            Mathf.Lerp(TimeFlowSignal.SmoothedSpeedFactor, TimeFlowSignal.SpeedFactor, smooth);

        float scaledDt = dt * TimeFlowSignal.SmoothedSpeedFactor;
        TimeFlowSignal.Accumulate(scaledDt);
        Shader.SetGlobalFloat(WeatherTimeId, TimeFlowSignal.WeatherTime);

        // ── 2. Ambience crossfade toward the active profile ──
        var p = ActiveProfile();
        float rate = transitionSeconds > 0.0001f ? dt / transitionSeconds : 1f;

        _currentIntensity     = Mathf.MoveTowards(_currentIntensity, p.lightIntensity, rate * 2f);
        _currentLightColor    = Color.Lerp(_currentLightColor, p.lightColor, rate * 2f);
        _currentCloudStrength = Mathf.MoveTowards(_currentCloudStrength, p.cloudShadowStrength, rate);
        _currentCloudSpeed    = Mathf.MoveTowards(_currentCloudSpeed, p.cloudSpeed, rate * 2f);

        UpdateGradeVolume(p, rate);

        // ── 3. Lightning flash decay (applied on top of the lerped intensity) ──
        if (_flashBoost > 0f)
            _flashBoost = Mathf.MoveTowards(_flashBoost, 0f, _flashDecayRate * dt);

        ApplyLight();

        // ── 4. Cloud globals — phase INTEGRATES speed (continuous through factor steps) ──
        _cloudPhase += _currentCloudSpeed * scaledDt;
        Shader.SetGlobalFloat(CloudShadowStrengthId, _currentCloudStrength);
        Shader.SetGlobalFloat(CloudPhaseId, _cloudPhase);
        Shader.SetGlobalVector(CloudDirId, cloudDirection.normalized);
        Shader.SetGlobalFloat(CloudScaleId, cloudScale);

        // ── 5. Heat haze: Sunny × Dry season, escalated during drought, else fades to off ──
        float hazeTarget = HazeTarget();
        float hazeRate = hazeFadeSeconds > 0.0001f ? dt / hazeFadeSeconds : 1f;
        _currentHaze = Mathf.MoveTowards(_currentHaze, hazeTarget, hazeRate);
        Shader.SetGlobalFloat(HazeStrengthId, _currentHaze);

        bool wantFeature = _currentHaze > 0.001f;
        if (heatHazeFeature != null && wantFeature != _hazeFeatureActive)
        {
            heatHazeFeature.SetActive(wantFeature);
            _hazeFeatureActive = wantFeature;
        }

        // ── 6. Horizon — one source; pushing per frame keeps Inspector tweaks live ──
        PushHorizon();
    }

    /// <summary>Lightning hook (LightningDirector). Boost is added on top of the ambience-lerped
    /// intensity and decays over decaySeconds — safe against the per-frame intensity lerp.</summary>
    public void FlashLight(float intensityBoost, float decaySeconds)
    {
        _flashBoost = Mathf.Max(_flashBoost, intensityBoost);
        _flashDecayRate = intensityBoost / Mathf.Max(0.01f, decaySeconds);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private void ApplyLight()
    {
        if (directionalLight == null)
        {
            if (!_warnedNoLight)
            {
                _warnedNoLight = true;
                Debug.LogWarning("AtmosphereDirector: no directional light assigned — " +
                                 "weather light grading and lightning flashes will not show.", this);
            }
            return;
        }

        directionalLight.intensity = _currentIntensity + _flashBoost;
        directionalLight.color = _currentLightColor;
    }

    private void PushHorizon()
    {
        Shader.SetGlobalColor(HorizonColorId, horizonColor);
        if (mainCamera != null)
        {
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            // Linear color space: Camera.backgroundColor's clear is NOT sRGB-encoded the way a
            // shader's rendered pixel is, so the same Color value clears visibly darker than it
            // renders on a shader quad. Gamma-correct the clear so the Inspector color is what shows.
            mainCamera.backgroundColor = backgroundColor.gamma;
        }
    }

    // A single Volume can hold only ONE profile at a time, so we can't weight-crossfade between
    // two grades on it (that needs two prepared volumes). Instead: hold the active weather's grade
    // up at weight 1; when the weather changes to a DIFFERENT grade, dip the weight to 0, swap the
    // profile in, then let it rise back — a clean transition through neutral grade. Weathers whose
    // grade is null hold the volume at weight 0 (no grading).
    private void UpdateGradeVolume(AmbienceProfile active, float rate)
    {
        if (gradeVolume == null) return;

        VolumeProfile want = active.grade;

        if (!_gradeInit)
        {
            _assignedGrade = want;
            gradeVolume.sharedProfile = want;
            _gradeInit = true;
        }

        if (want == _assignedGrade)
        {
            float up = want != null ? 1f : 0f;
            gradeVolume.weight = Mathf.MoveTowards(gradeVolume.weight, up, rate);
        }
        else
        {
            // Fade the outgoing grade out first, then swap the new one in at weight 0 so it rises.
            gradeVolume.weight = Mathf.MoveTowards(gradeVolume.weight, 0f, rate);
            if (gradeVolume.weight <= 0.0001f)
            {
                _assignedGrade = want;
                gradeVolume.sharedProfile = want;
            }
        }
    }

    private WeatherState CurrentWeather() =>
        WeatherManager.Instance != null ? WeatherManager.Instance.CurrentWeather : WeatherState.Cloudy;

    private AmbienceProfile ActiveProfile() => _byState[CurrentWeather()];

    private float HazeTarget()
    {
        var wm = WeatherManager.Instance;
        var rm = ResourceManager.Instance;
        if (wm == null || rm == null) return 0f;
        if (wm.CurrentWeather != WeatherState.Sunny || rm.CurrentSeason != Season.Dry) return 0f;
        return hazeStrength * (wm.IsDroughtActive ? droughtHazeMultiplier : 1f);
    }

    private void BuildProfileTable()
    {
        _byState = new Dictionary<WeatherState, AmbienceProfile>();
        foreach (var p in profiles)
        {
            if (p == null) continue;

            // Unity zero-initializes NEW Inspector list entries (field initializers don't run),
            // so a freshly added, never-filled profile arrives as intensity 0 + black — which
            // would pin the directional light to invisible and mask the day/night rotation
            // entirely. Treat that as unauthored: refill the numbers from the coded default,
            // keep whatever Volume was assigned.
            if (p.lightIntensity <= 0f && p.lightColor.maxColorComponent <= 0f)
            {
                var d = DefaultProfile(p.state);
                p.lightIntensity      = d.lightIntensity;
                p.lightColor          = d.lightColor;
                p.cloudShadowStrength = d.cloudShadowStrength;
                p.cloudSpeed          = d.cloudSpeed;
                Debug.LogWarning($"AtmosphereDirector: AmbienceProfile for {p.state} was zeroed " +
                                 "(intensity 0, black) — treated as unauthored, coded defaults applied. " +
                                 "Fill it in the Inspector to tune it.", this);
            }

            _byState[p.state] = p;
        }

        // Law 3 — seed unauthored states with coded defaults and warn, never null-ref.
        foreach (WeatherState s in Enum.GetValues(typeof(WeatherState)))
            if (!_byState.ContainsKey(s))
            {
                _byState[s] = DefaultProfile(s);
                Debug.LogWarning($"AtmosphereDirector: no AmbienceProfile authored for {s} — " +
                                 "using coded default. Add one in the Inspector to tune it.", this);
            }
    }

    // Starting grades from ATMOSPHERE_BUILD_PLAN.md §3 (post-exposure/saturation live on the Volumes).
    private static AmbienceProfile DefaultProfile(WeatherState s) => s switch
    {
        WeatherState.Sunny  => new AmbienceProfile { state = s, lightIntensity = 1.30f, lightColor = new Color(1.00f, 0.96f, 0.88f), cloudShadowStrength = 0.05f, cloudSpeed = 0.30f },
        WeatherState.Cloudy => new AmbienceProfile { state = s, lightIntensity = 1.00f, lightColor = new Color(0.95f, 0.96f, 1.00f), cloudShadowStrength = 0.30f, cloudSpeed = 0.45f },
        WeatherState.Rainy  => new AmbienceProfile { state = s, lightIntensity = 0.75f, lightColor = new Color(0.82f, 0.88f, 1.00f), cloudShadowStrength = 0.15f, cloudSpeed = 0.60f },
        WeatherState.Stormy => new AmbienceProfile { state = s, lightIntensity = 0.55f, lightColor = new Color(0.72f, 0.78f, 0.95f), cloudShadowStrength = 0.20f, cloudSpeed = 0.90f },
        _                   => new AmbienceProfile { state = s },
    };
}
