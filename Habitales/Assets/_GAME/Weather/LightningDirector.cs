using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// LightningDirector — occasional lightning flashes during storms (ATMOSPHERE_BUILD_PLAN.md §5b).
///
/// Owns NO weather logic: put it on the storm VFX GameObject and add that object to the
/// Stormy binding's enableObjects in WeatherVFXController — OnEnable starts the strike loop,
/// OnDisable kills it. A strike is 1–3 rapid light pulses routed through
/// AtmosphereDirector.FlashLight so the flash and the ambience intensity lerp never fight.
///
/// Cadence is REAL-time on purpose (not scaled by TimeFlowSignal) — strikes at 16× would strobe.
/// While a deluge is active the interval is multiplied down (storm feels like it's peaking).
///
/// Wire-up (Law 3): wire thunder SFX (FMOD) to <see cref="onStrike"/> in the Inspector.
/// </summary>
public class LightningDirector : MonoBehaviour
{
    [Header("Cadence (~4–5 strikes/min at 10–18s)")]
    [SerializeField] private Vector2 strikeIntervalRange = new Vector2(10f, 18f);
    [Tooltip("Interval multiplier while a deluge is active — the storm audibly/visibly peaks.")]
    [SerializeField, Range(0.1f, 1f)] private float delugeIntervalMultiplier = 0.5f;
    [Tooltip("Fire one strike shortly after the storm starts — sells the transition moment.")]
    [SerializeField] private bool strikeOnEnable = true;

    [Header("Flash")]
    [SerializeField] private Vector2Int pulsesPerStrike = new Vector2Int(1, 3);
    [Tooltip("Directional-light intensity added at the peak of a pulse.")]
    [SerializeField] private float pulseIntensityBoost = 2.5f;
    [Tooltip("Seconds for one pulse to decay back to ambience level.")]
    [SerializeField] private float pulseDecaySeconds = 0.15f;
    [SerializeField] private Vector2 pulseGapRange = new Vector2(0.06f, 0.14f);

    [Tooltip("Fired once per strike (before the first pulse) — hook thunder SFX here.")]
    public UnityEvent onStrike = new UnityEvent();

    private Coroutine _loop;
    private float _intervalMultiplier = 1f;

    void OnEnable()
    {
        var wm = WeatherManager.Instance;
        if (wm != null)
        {
            wm.OnDelugeStarted += HandleDelugeStarted;
            wm.OnDelugeEnded += HandleDelugeEnded;
            _intervalMultiplier = wm.IsDelugeActive ? delugeIntervalMultiplier : 1f;
        }

        _loop = StartCoroutine(StrikeLoop());
    }

    void OnDisable()
    {
        var wm = WeatherManager.Instance;
        if (wm != null)
        {
            wm.OnDelugeStarted -= HandleDelugeStarted;
            wm.OnDelugeEnded -= HandleDelugeEnded;
        }

        if (_loop != null) { StopCoroutine(_loop); _loop = null; }
    }

    private void HandleDelugeStarted() => _intervalMultiplier = delugeIntervalMultiplier;
    private void HandleDelugeEnded()   => _intervalMultiplier = 1f;

    private IEnumerator StrikeLoop()
    {
        if (strikeOnEnable)
        {
            yield return new WaitForSeconds(Random.Range(0.5f, 2f));
            yield return Strike();
        }

        while (true)
        {
            yield return new WaitForSeconds(
                Random.Range(strikeIntervalRange.x, strikeIntervalRange.y) * _intervalMultiplier);
            yield return Strike();
        }
    }

    private IEnumerator Strike()
    {
        onStrike?.Invoke();

        int pulses = Random.Range(pulsesPerStrike.x, pulsesPerStrike.y + 1);
        for (int i = 0; i < pulses; i++)
        {
            if (AtmosphereDirector.Instance != null)
                AtmosphereDirector.Instance.FlashLight(pulseIntensityBoost, pulseDecaySeconds);
            if (i < pulses - 1)
                yield return new WaitForSeconds(Random.Range(pulseGapRange.x, pulseGapRange.y));
        }
    }
}
