using System.Collections.Generic;
using UnityEngine;

public class TileVisualizer : MonoBehaviour
{
    // ── Refs ──────────────────────────────────────────────────────────────────
    private MeshRenderer meshRenderer;
    private Material     materialInstance;
    private Tile         tile;
    private Color originalColor;
    private TileVisualState currentState = TileVisualState.Default;

    // ── Firebreak ─────────────────────────────────────────────────────────────
    [Header("Overlays")]
    [SerializeField] private GameObject firebreakPrefab;
    private GameObject firebreakInstance;

    // ── Selection Pulse ───────────────────────────────────────────────────────
    [Header("Selection Pulse")]
    [SerializeField] private Color pulseColorA = new Color(0.55f, 0.80f, 1f);    // light blue
    [SerializeField] private Color pulseColorB = new Color(0.10f, 0.45f, 0.85f); // blue
    [SerializeField] private float pulseSpeed  = 1.5f;

    // ── Selection Juice (2026-07-29) ──────────────────────────────────────────
    // Everything about selection used to be COLOUR — the pulse above is a hue lerp, which reads as
    // "glowing", not as "responding". These two add the missing kinetic and negative feedback.
    [Header("Selection Juice")]
    [Tooltip("Peak scale multiplier of the select punch; 1 disables it. Kept small on purpose — " +
             "tiles sit shoulder to shoulder on a grid, so much past ~1.1 visibly overlaps neighbours.")]
    [SerializeField] private float punchScale = 1.07f;
    [Tooltip("Punch duration in seconds, there and back. Short is the point: this should read as a " +
             "press, not an animation.")]
    [SerializeField] private float punchDuration = 0.13f;
    [Tooltip("Colour a tile flashes when an action refuses it — wrong entity for a filtered cleanup " +
             "action, or the workforce budget is spent.")]
    [SerializeField] private Color refusalColor = new Color(0.85f, 0.20f, 0.20f);
    [Tooltip("Refusal flash duration in seconds.")]
    [SerializeField] private float refusalDuration = 0.22f;

    // Captured once so repeated punches can never ratchet the tile's size.
    private Vector3 baseScale = Vector3.one;
    // Elapsed seconds into the refusal flash; negative = not flashing.
    private float refusalT = -1f;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Update()
    {
        if (materialInstance == null) return;

        // The refusal flash owns _BaseColor while it runs, then hands back to the state colour.
        if (refusalT >= 0f)
        {
            refusalT += Time.unscaledDeltaTime;
            float d = Mathf.Max(0.01f, refusalDuration);

            if (refusalT >= d)
            {
                refusalT = -1f;
                UpdateMaterial();   // restore whatever state applies NOW (it may have changed mid-flash)
            }
            else
            {
                // Hard on, ease off — a flash, not a fade in and out.
                materialInstance.SetColor("_BaseColor", Color.Lerp(refusalColor, ComputeStateColor(), refusalT / d));
            }
            return;
        }

        if (currentState != TileVisualState.Selected) return;
        float t = Mathf.PingPong(Time.time * pulseSpeed, 1f);
        materialInstance.SetColor("_BaseColor", Color.Lerp(pulseColorA, pulseColorB, t));
    }

    void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            materialInstance = meshRenderer.material;
            originalColor = materialInstance.GetColor("_BaseColor");
        }

        // Defensive, mirroring RegionManager.AnimateRegionReveal: a zero baseline would make every
        // punch a no-op and could leave the tile invisible if anything ever tweened off it.
        baseScale = transform.localScale;
        if (baseScale.sqrMagnitude < 0.0001f) baseScale = Vector3.one;
    }

    // ── Selection juice, driven by TileSelector ───────────────────────────────

    /// <summary>
    /// Scale punch on select/deselect. <paramref name="amplitude"/> scales the overshoot, so a
    /// deselect can land softer than a select.
    ///
    /// <para>Uses LeanTween rather than a hand-rolled timer specifically so it interops with
    /// <c>RegionManager.AnimateRegionReveal</c>, which tweens this same localScale and calls
    /// <c>LeanTween.cancel</c> to keep two tweens from fighting over it. Sharing the idiom means
    /// a region reveal cleanly kills a mid-flight punch instead of capturing a punched scale as
    /// the tile's "authored" size and leaving it permanently wrong.</para>
    /// </summary>
    public void Punch(float amplitude = 1f)
    {
        if (punchScale <= 1f || amplitude <= 0f) return;

        float peak = 1f + (punchScale - 1f) * amplitude;

        LeanTween.cancel(gameObject);
        transform.localScale = baseScale;

        LeanTween.scale(gameObject, baseScale * peak, Mathf.Max(0.01f, punchDuration) * 0.5f)
                 .setEase(LeanTweenType.easeOutQuad)
                 .setLoopPingPong(1)
                 // Idempotent end state, matching ObjectiveBannerUI: land exactly on baseScale even
                 // if the tween is interrupted, so punches can never accumulate drift.
                 .setOnComplete(() => transform.localScale = baseScale);
    }

    /// <summary>
    /// Flashes the tile toward <see cref="refusalColor"/> — this action cannot be aimed here. Before
    /// this, a refused tile did nothing visible at all, which reads as an unresponsive game rather
    /// than an invalid target.
    /// </summary>
    public void FlashRefusal() => refusalT = 0f;

    public void Initialize(Tile tileData)
    {
        tile = tileData;
        SetVisualState(TileVisualState.Default);
        UpdateVisuals();
    }

    // ── Called by TileManager.UpdateTileVisual ────────────────────────────────
    public void UpdateVisuals()
    {
        if (tile == null || materialInstance == null) return;
        if (currentState == TileVisualState.Default      ||
            currentState == TileVisualState.RegionHighlight ||
            currentState == TileVisualState.RegionDimmed)
            UpdateMaterial();
    }

    // ── Visual state ──────────────────────────────────────────────────────────
    public void SetVisualState(TileVisualState state)
    {
        currentState = state;
        UpdateMaterial();
    }

    void UpdateMaterial()
    {
        if (meshRenderer == null || materialInstance == null || tile == null) return;
        meshRenderer = GetTileRenderer();

        materialInstance.SetColor("_BaseColor", ComputeStateColor());
    }

    /// <summary>
    /// The colour this tile should rest at for its current state. Split out of UpdateMaterial so the
    /// refusal flash can ease back toward the right destination instead of guessing white.
    /// </summary>
    Color ComputeStateColor()
    {
        if (tile == null) return originalColor;

        // Base colour is the material's original tint — health is communicated by the tile
        // SHADER (fed _Soil_Composite/_Vegetation_Cover in GetTileRenderer), never by a C#
        // tint (GetHealthColor deleted 2026-07-08; the shader owns health visuals).
        Color baseColor = originalColor;

        // Contamination tint — blends toward sickly purple above 60
        if (tile.stats.contamination > 60f)
        {
            float t = Mathf.Clamp01((tile.stats.contamination - 60f) / 40f);
            Color contaminationTint = new Color(0.45f, 0.18f, 0.50f);
            baseColor = Color.Lerp(baseColor, contaminationTint, t * 0.65f);
        }

        Color finalColor;
        switch (currentState)
        {
            case TileVisualState.Hover:
                finalColor = Color.Lerp(baseColor, Color.cyan, 0.8f);              break;
            case TileVisualState.Selected:
                finalColor = Color.Lerp(baseColor, new Color(0f, 0.8f, 0.8f, 1f), 0.7f); break;
            case TileVisualState.Adjacent:
                finalColor = Color.Lerp(baseColor, new Color(1f, 1f, 1f, 0.5f), 0.5f);  break;
            case TileVisualState.AdjacentHover:
                finalColor = Color.Lerp(baseColor, new Color(1f, 1f, 1f, 0.7f), 0.7f);  break;
            case TileVisualState.RegionHighlight:
                finalColor = Color.Lerp(baseColor, Color.white, 0.35f);             break;
            case TileVisualState.RegionDimmed:
                finalColor = Color.Lerp(baseColor, Color.black, 0.6f);              break;
            default: // Default
                finalColor = baseColor;                                              break;
        }
        return finalColor;
    }

    // ── Overlay list reader ───────────────────────────────────────────────────
    /// Called by TileManager.UpdateTileVisual after every stat change.
    /// Reads tile.tv and syncs the firebreak model.
    /// Contamination tint is driven by tile.stats.contamination in UpdateMaterial
    /// so TileOverlayType.Contaminated acts as a marker only (e.g. for Examine).
    public void UpdateOverlays(List<TileOverlayType> tv)
    {
        bool hasFirebreak = tv != null && tv.Contains(TileOverlayType.Firebreak);

        if (hasFirebreak && firebreakInstance == null)
        {
            if (firebreakPrefab != null)
            {
                firebreakInstance = Instantiate(firebreakPrefab, transform);
                firebreakInstance.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                firebreakInstance.transform.localRotation = Quaternion.identity;
                firebreakInstance.name = "Firebreak";
            }
            else
                Debug.LogWarning($"TileVisualizer: firebreakPrefab not assigned on {gameObject.name}!");
        }
        else if (!hasFirebreak && firebreakInstance != null)
        {
            Destroy(firebreakInstance);
            firebreakInstance = null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    MeshRenderer GetTileRenderer()
    {
        meshRenderer.material.SetFloat("_Soil_Composite", (tile.GetSoilComposite() / 100f));
        meshRenderer.material.SetFloat("_Vegetation_Cover", (tile.GetVegetationCover() / 100f));
        
        return meshRenderer;
    }

    // White by contract: the tile-flash restore color (Beat1_3JuiceDirector). Identical to the
    // old behavior — GetHealthColor always returned white before it was deleted.
    public Color GetBaseColor() => Color.white;

    public Tile           GetTileData()    => tile;
    public TileVisualState GetCurrentState() => currentState;

    void OnDestroy()
    {
        if (materialInstance != null) Destroy(materialInstance);
        if (firebreakInstance != null) Destroy(firebreakInstance);
    }
}
