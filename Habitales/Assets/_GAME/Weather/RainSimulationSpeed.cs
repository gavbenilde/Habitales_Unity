using UnityEngine;

/// <summary>
/// RainSimulationSpeed — couples particle systems to the action time-lapse
/// (ATMOSPHERE_BUILD_PLAN.md §5a). Drop on the rain/storm VFX root: every ParticleSystem in the
/// children pelts down at TimeFlowSignal's smoothed factor (1× idle → up to 16× mid-action).
///
/// Reads the SMOOTHED factor (written by AtmosphereDirector) so the speed-up reads as
/// acceleration; falls back to the raw factor if no director is in the scene.
/// </summary>
public class RainSimulationSpeed : MonoBehaviour
{
    private ParticleSystem[] _systems;

    void OnEnable()
    {
        _systems = GetComponentsInChildren<ParticleSystem>(includeInactive: true);
        if (_systems.Length == 0)
            Debug.LogWarning("RainSimulationSpeed: no ParticleSystems found in children — " +
                             "nothing to speed-couple.", this);
    }

    void Update()
    {
        float speed = AtmosphereDirector.Instance != null
            ? TimeFlowSignal.SmoothedSpeedFactor
            : TimeFlowSignal.SpeedFactor;

        foreach (var ps in _systems)
        {
            var main = ps.main;
            main.simulationSpeed = speed;
        }
    }
}
