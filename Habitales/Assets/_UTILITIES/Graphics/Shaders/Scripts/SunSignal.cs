using UnityEngine;

/// <summary>
/// Static sun/daylight signal — the unlit half of the day-night cycle. Mirrors
/// <see cref="TimeFlowSignal"/>: <b>DayNightCycleHandler writes it, everyone else reads it.</b>
///
/// WHY THIS EXISTS. The day-night cycle is a real rotating directional light, so anything drawn
/// with a URP <i>Lit</i> shader (the tiles, via TileShader.shadergraph) darkens and brightens for
/// free. Anything drawn <i>unlit</i> does not — and the walkers are unlit: the Spine worker
/// materials run `Spine/Skeleton`, which is `Lighting Off` and outputs `texture * vertexColor`
/// with no light term and no `_Color`/`_BaseColor` uniform to push a tint into. So the workers
/// stayed at full noon brightness while the ground around them went dark. Same story for any
/// SpriteRenderer art (the animal rigs) on the default unlit sprite material.
///
/// The fix is not a shader swap. spine-unity here is 3.8 while the project is URP 14 / Unity
/// 2022.3, so the matching `com.esotericsoftware.spine.urp-shaders` package is a version-mismatch
/// risk for a purely cosmetic win; and `Spine-Skeleton-Lit.shader` shipped in the runtime is a
/// built-in-pipeline surface shader that URP will not render. Instead we publish the light term
/// the tiles are already receiving, and let unlit art multiply it in through whatever tint channel
/// it does have (Spine: skeleton vertex color; sprites: SpriteRenderer.color).
///
/// <see cref="Daylight"/> is deliberately the same quantity a Lambert surface facing straight up
/// receives — `saturate(dot(-lightDirection, up))` — so a walker tinted by this darkens on the
/// same curve as the tile it is standing on, rather than on a curve that merely looks similar.
///
/// Run-scoped: reset by RunRestart (see its static-state audit).
/// </summary>
public static class SunSignal
{
    /// <summary>How lit a horizontal surface is right now: 1 at the sun's zenith, 0 once the
    /// directional light is at or below the horizon. Written by DayNightCycleHandler each frame.</summary>
    public static float Daylight { get; set; } = 1f;

    /// <summary>The colour unlit art should MULTIPLY over itself to sit in the current lighting.
    /// White at full day, the handler's authored night tint at full night, with the directional
    /// light's own colour/intensity (which AtmosphereDirector drives per weather) folded in.
    /// Alpha is always 1 — this is a tint, never an opacity.</summary>
    public static Color Tint { get; set; } = Color.white;

    /// <summary>Back to full daylight. Called by RunRestart so a restart mid-night doesn't leave
    /// the next run's walkers tinted dark for the frames before the handler writes again.</summary>
    public static void Reset()
    {
        Daylight = 1f;
        Tint = Color.white;
    }
}
