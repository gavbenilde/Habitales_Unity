using System.Collections.Generic;
using ArtificeToolkit.Attributes;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// WeatherVFXController — the COSMETIC half of weather (arch S3: cosmetics live in their own
/// component, never in the manager). WeatherManager owns the *state + simulation effects*; this
/// listens to <see cref="WeatherManager.OnWeatherChanged"/> and drives the *look*: toggles
/// rain/godray/storm objects on and off, and fires a per-state UnityEvent for bespoke effects
/// (a thunder-flash coroutine, a shader-param swap, an audio cue). RunManager never touches this —
/// it runs purely off the event, so adding/removing weather visuals never edits gameplay code.
///
/// Wire-up (Law 3): drop this on a "WeatherVFX" GameObject, add one binding per WeatherState,
/// drag the particle systems / godray volumes / storm-cloud objects into <c>enableObjects</c>,
/// and (optionally) wire <c>onWeatherEntered</c> to a bespoke effect in the Inspector — no code.
///
/// <para><b>[ArtificeIgnore]</b> forces Unity's stock inspector for this component. ArtificeToolkit's
/// custom list drawer assumes every list element is a <c>Component</c> and calls
/// <c>GetComponent(elementType)</c> on anything dropped onto the list — dropping a GameObject onto the
/// <c>bindings</c> list (whose element <see cref="WeatherVFXBinding"/> is a plain [Serializable] class,
/// not a Component) throws <c>ArgumentException: GetComponent requires … derives from Component</c>.
/// The default inspector handles nested-class lists correctly, so we opt this one component out.</para>
/// </summary>
[ArtificeIgnore]
public class WeatherVFXController : MonoBehaviour
{
    [System.Serializable]
    public class WeatherVFXBinding
    {
        public WeatherState state;

        [Tooltip("Objects shown while this weather is active (rain particles, godrays, storm " +
                 "clouds). Everything in OTHER bindings is hidden when this state is entered.")]
        public List<GameObject> enableObjects = new List<GameObject>();

        [Tooltip("Fired once when this weather becomes active — wire bespoke FX here in the " +
                 "Inspector: a thunder-flash, a fullscreen shader toggle, an ambient SFX.")]
        public UnityEvent onWeatherEntered = new UnityEvent();
    }

    [Tooltip("One binding per WeatherState. States without a binding simply show nothing.")]
    [SerializeField] private List<WeatherVFXBinding> bindings = new List<WeatherVFXBinding>();

    void OnEnable()
    {
        if (WeatherManager.Instance == null)
        {
            // WeatherManager initializes first (DefaultExecutionOrder -200), so this should not
            // happen at scene start; only on a late re-enable with no WeatherManager present.
            Debug.LogWarning($"{name}: no WeatherManager in scene — weather visuals will not update.", this);
            return;
        }

        WeatherManager.Instance.OnWeatherChanged += Apply;
        Apply(WeatherManager.Instance.CurrentWeather); // sync to whatever is already active
    }

    void OnDisable()
    {
        if (WeatherManager.Instance != null)
            WeatherManager.Instance.OnWeatherChanged -= Apply;
    }

    private void Apply(WeatherState state)
    {
        foreach (var b in bindings)
        {
            bool active = b.state == state;
            foreach (var go in b.enableObjects)
                if (go != null) go.SetActive(active);

            if (active) b.onWeatherEntered?.Invoke();
        }
    }
}
