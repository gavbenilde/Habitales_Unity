using System.Collections;
using UnityEngine;
using UTILITIES.Camera;

/// <summary>
/// Frames the camera on EVERY newly generated region, not just onboarding's phase-16 unlock.
/// (Added 2026-07-28.)
///
/// Hooks <see cref="RegionManager.OnRegionGenerated"/> — the meaning-event that already fires for
/// every region and carries its id (Law 2; the event's own doc-comment has listed "camera pan" as an
/// intended subscriber since it was written). Framing point is
/// <see cref="TileManager.TryGetRegionBounds"/>'s AABB centre, and the move goes through
/// <see cref="EventCameraHandler.PanTo"/>, which resolves the camera's ground focus before moving —
/// so the region lands under the reticle on this tilted ortho rig instead of overshooting.
///
/// Deliberately does NOT return to origin: the player should be left looking at the new land, which
/// is what phase 16 did.
///
/// WIRING: Add Component onto the Main Camera (next to EventCameraHandler). It resolves everything
/// through singletons — there is nothing to drag into the Inspector.
/// </summary>
[DefaultExecutionOrder(50)]   // after the manager singletons have Awoken
public class RegionRevealCameraFocus : MonoBehaviour
{
    /// <summary>Region 1 — spawned by GameManager.SpawnInitialZone during the loading reveal.</summary>
    private const int InitialRegionID = 1;

    [Tooltip("Also frame Region 1 when it generates at boot. OFF by default: Zone 1 spawns under the " +
             "loading screen with its own reveal animation, and the camera's authored start position " +
             "already covers it — panning there fights onboarding phase 1.")]
    [SerializeField] private bool focusOnInitialRegion = false;

    [Tooltip("Seconds to wait after the region generates before panning. 0 = pan immediately, so the " +
             "camera arrives while the tiles are still staggering in.")]
    [SerializeField] private float panDelaySeconds = 0f;

    [Tooltip("Log every framing decision. Leave off outside camera work.")]
    [SerializeField] private bool logFocus = false;

    /// <summary>
    /// True while a focuser is live in the scene. Read by OnboardingDirector so phase 16 doesn't
    /// fire a SECOND pan at the same region — and so phase 16 still pans on its own if this
    /// component was never added to the camera.
    /// </summary>
    public static bool IsActive { get; private set; }

    private RegionManager _regionManager;
    private bool _subscribed;

    void OnEnable()
    {
        IsActive = true;
        TrySubscribe();
    }

    // Retry: at OnEnable, RegionManager.Instance may not be assigned yet depending on scene order.
    // (Same hardening pattern as DayNightCycleHandler — a silently-missed subscription here would
    // look exactly like "the camera feature was never built".)
    void Start() => TrySubscribe();

    void OnDisable()
    {
        IsActive = false;
        Unsubscribe();
    }

    void TrySubscribe()
    {
        if (_subscribed) return;

        _regionManager = RegionManager.Instance;
        if (_regionManager == null) return;   // Start() will retry; loud-fail lands below

        _regionManager.OnRegionGenerated += HandleRegionGenerated;
        _subscribed = true;
    }

    void Unsubscribe()
    {
        if (!_subscribed || _regionManager == null) return;

        _regionManager.OnRegionGenerated -= HandleRegionGenerated;
        _subscribed = false;
    }

    void OnDestroy() => Unsubscribe();

    void HandleRegionGenerated(RegionGenerationResult result)
    {
        if (result == null) return;

        if (!focusOnInitialRegion && result.regionID <= InitialRegionID)
        {
            if (logFocus) Debug.Log($"{name}: skipping Region {result.regionID} (initial region).", this);
            return;
        }

        if (panDelaySeconds > 0f) StartCoroutine(FocusAfterDelay(result.regionID));
        else FocusOn(result.regionID);
    }

    IEnumerator FocusAfterDelay(int regionID)
    {
        yield return new WaitForSeconds(panDelaySeconds);
        FocusOn(regionID);
    }

    /// <summary>
    /// Pans the camera so <paramref name="regionID"/>'s bounding-box centre sits under the framing
    /// point. Public so the debug unlock path (or a future "show me region N" affordance) can reuse
    /// it. Returns false — with a loud reason — if anything needed is missing.
    /// </summary>
    public bool FocusOn(int regionID)
    {
        if (EventCameraHandler.Instance == null)
        {
            Debug.LogWarning($"{name}: no EventCameraHandler in scene — Region {regionID} will not be framed. " +
                             "Add it to the Main Camera.", this);
            return false;
        }

        TileManager tm = TileManager.Instance;
        if (tm == null)
        {
            Debug.LogWarning($"{name}: no TileManager — Region {regionID} will not be framed.", this);
            return false;
        }

        if (!tm.TryGetRegionBounds(regionID, out Bounds bounds))
        {
            Debug.LogWarning($"{name}: Region {regionID} reported no tiles — nothing to frame.", this);
            return false;
        }

        EventCameraHandler.Instance.PanTo(bounds.center);

        if (logFocus)
            Debug.Log($"{name}: framing Region {regionID} at {bounds.center} (footprint {bounds.size.x}×{bounds.size.z}).", this);

        return true;
    }
}
