/// <summary>
/// Static time-flow signal shared by every animated atmosphere effect (cloud scroll, rain,
/// heat haze). See ATMOSPHERE_BUILD_PLAN.md §1.
///
/// DayNightCycleHandler WRITES <see cref="SpeedFactor"/> (1 while idle; dayDuration/currentDuration
/// — 1..16 — while an action time-lapse runs). AtmosphereDirector smooths it into
/// <see cref="SmoothedSpeedFactor"/>, integrates <see cref="WeatherTime"/> once per frame, and
/// mirrors it to the `_WeatherTime` shader global.
///
/// Effects must animate off the ACCUMULATED WeatherTime (or read SmoothedSpeedFactor per frame),
/// never `Time.time * speed`: multiplying raw time by a stepping factor makes scrolling textures
/// JUMP the instant the factor changes (4→8 doubles the whole phase); integrating the scaled
/// delta keeps motion continuous through speed changes.
/// </summary>
public static class TimeFlowSignal
{
    /// <summary>Raw time-lapse factor. 1 when idle. Written by DayNightCycleHandler only.</summary>
    public static float SpeedFactor { get; set; } = 1f;

    /// <summary>SpeedFactor smoothed over ~0.2s so the 2× steps read as acceleration, not gear
    /// changes. Written by AtmosphereDirector each frame; per-frame consumers (rain
    /// simulationSpeed) should read THIS one.</summary>
    public static float SmoothedSpeedFactor { get; set; } = 1f;

    /// <summary>Accumulated, speed-scaled clock in seconds. Advanced once per frame by
    /// AtmosphereDirector via <see cref="Accumulate"/>.</summary>
    public static float WeatherTime { get; private set; }

    public static void Accumulate(float scaledDeltaSeconds) => WeatherTime += scaledDeltaSeconds;
}
