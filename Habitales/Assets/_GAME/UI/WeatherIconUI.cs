using UnityEngine;
using UnityEngine.UI;

namespace Habitales.UI
{
    /// <summary>
    /// Passive HUD weather icon. Reacts to <see cref="WeatherManager.OnWeatherChanged"/> and swaps
    /// a single <see cref="Image"/> to the sprite authored for the active <see cref="WeatherState"/>.
    /// One interchangeable slot per weather type (Sunny / Cloudy / Rainy / Stormy) — drop final art
    /// into these slots, no code change (mirrors TrendIndicatorUI's sprite-swap pattern and
    /// WeatherVFXController's event subscription).
    ///
    /// Like the other HUD views this is push/react only — it holds no weather logic and never
    /// advances the sim (Law 1). WeatherManager initializes first (DefaultExecutionOrder -200), so
    /// the initial sync in OnEnable always reads a valid CurrentWeather.
    ///
    /// WIRING (human):
    ///   1. This lives on the HUD's "WeatherIcon" GameObject.
    ///   2. Set <c>_icon</c> → the Image that shows the weather sprite (defaults to an Image on this GO).
    ///   3. Assign the four sprite slots (Sunny / Cloudy / Rainy / Stormy).
    /// </summary>
    public class WeatherIconUI : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The Image whose sprite is swapped per weather. Defaults to an Image on this GameObject.")]
        [SerializeField] private Image _icon;

        [Header("Weather sprites (one per WeatherState — swap in final art here)")]
        [SerializeField] private Sprite _sunny;
        [SerializeField] private Sprite _cloudy;
        [SerializeField] private Sprite _rainy;
        [SerializeField] private Sprite _stormy;

        private Vector3 originalScale;
        
        private void Awake()
        {
            if (_icon == null) _icon = GetComponent<Image>();
            if (_icon == null)
            {
                Debug.LogError($"{name}: WeatherIconUI._icon (Image) missing — wire it in the Inspector.", this);
                enabled = false;
            }
            
            originalScale = _icon.rectTransform.localScale;
        }

        private void OnEnable()
        {
            if (WeatherManager.Instance == null)
            {
                // WeatherManager initializes first (DefaultExecutionOrder -200), so this should not
                // happen at scene start; only on a late re-enable with no WeatherManager present.
                Debug.LogWarning($"{name}: no WeatherManager in scene — weather icon will not update.", this);
                return;
            }

            WeatherManager.Instance.OnWeatherChanged += Apply;
            Apply(WeatherManager.Instance.CurrentWeather); // sync to whatever is already active
        }

        private void OnDisable()
        {
            if (WeatherManager.Instance != null)
                WeatherManager.Instance.OnWeatherChanged -= Apply;
        }

        private void Apply(WeatherState state)
        {
            if (_icon == null) return;

            Sprite sprite = SpriteFor(state);
            if (sprite == null)
            {
                Debug.LogWarning($"{name}: no sprite assigned for {state} — assign one in the Inspector.", this);
                return;
            }

            _icon.sprite = sprite;
            
            LeanTween.cancel(_icon.gameObject);

            LeanTween.scale(_icon.rectTransform, originalScale * 0.85f, 0.1f)
                .setEaseOutQuad()
                .setOnComplete(() =>
                {
                    LeanTween.scale(_icon.rectTransform, originalScale, 0.15f)
                        .setEaseOutBack();
                });
        }

        private Sprite SpriteFor(WeatherState state) => state switch
        {
            WeatherState.Sunny  => _sunny,
            WeatherState.Cloudy => _cloudy,
            WeatherState.Rainy  => _rainy,
            WeatherState.Stormy => _stormy,
            _                   => null,
        };
    }
}
