// PingRingFunction.hlsl — Azi tier system.
//
// Custom Function node body for the Ping Ring Shader Graph (see PingRingShaderSpec.md).
// Reads the uncapped StructuredBuffer<PingData> PingDirector.cs uploads every time the active
// ping list changes, and analytically evaluates every ring at the given world position in one
// pass, combining overlaps with max() (never +) so a wave of overlapping pings reads as one
// coalescing front instead of stacking to blown-out opacity.
//
// Property names below MUST match PingDirector.cs (_PingBuffer / _PingCount / _HabitalesClock)
// and the material-level Shader Graph exposed properties documented in PingRingShaderSpec.md
// (_PingSpeed / _PingMaxRadius / _PingBorderWidth / _PingColor / _PingFadeInTime).
//
// Platform note: StructuredBuffer sampling in a fragment shader requires an SM 4.5+ target
// (Shader Graph's default URP Lit/Unlit target on PC/console/most mobile GLES3+ is fine; if the
// project ever targets GLES2 this node will not compile there — flag to the orchestrator if that
// becomes a real target).

#ifndef HABITALES_PING_RING_INCLUDED
#define HABITALES_PING_RING_INCLUDED

struct PingData
{
    float3 center;    // world-space ring origin (PingDirector.cs: PingBufferEntry.center)
    float  startTime; // director-clock time this ping began (PingBufferEntry.startTime)
};

StructuredBuffer<PingData> _PingBuffer;
int   _PingCount;
float _HabitalesClock; // GLOBAL — set once per frame by PingDirector.Update() (Shader.SetGlobalFloat), halts behind popups

// Material-level tunables (Shader Graph exposed properties — see PingRingShaderSpec.md for the
// Blackboard setup). Declared here too so this file compiles standalone; Shader Graph's
// generated code will supply the same-named globals from the Blackboard properties at bind time,
// so do NOT also wire these as Custom Function node inputs — leave them as globals on both sides.
float _PingSpeed;       // world units the ring radius grows per second
float _PingMaxRadius;   // radius (world units) at which a ring has fully expanded and faded out
float _PingBorderWidth; // ring band thickness, world units
float _PingFadeInTime;  // seconds for the ring's solid border to fade in from a ping's start
float4 _PingColor;      // ring RGB + base alpha multiplier

// WorldPos: the fragment's world-space position (ground quad, so .xz is what matters).
// Color: RGBA to feed into Emission (or Base Color, for an Unlit target) — see the graph spec.
void PingRing_float(float3 WorldPos, out float4 Color)
{
    float best = 0.0;

    for (int i = 0; i < _PingCount; i++)
    {
        PingData p = _PingBuffer[i];
        float age = _HabitalesClock - p.startTime;
        if (age < 0.0) continue; // hasn't started yet (staggered start offset)

        float radius = age * _PingSpeed;
        if (radius > _PingMaxRadius) continue; // fully expired — analytically invisible, cheap skip

        float dist = distance(WorldPos.xz, p.center.xz);

        // Solid border fading IN over _PingFadeInTime from the ping's start...
        float fadeIn = saturate(age / max(_PingFadeInTime, 0.0001));
        // ...ring band expanding small -> big, fading OUT as radius approaches max radius.
        float fadeOut = saturate(1.0 - (radius / max(_PingMaxRadius, 0.0001)));

        // Analytic distance-field ring: 1 at dist == radius, falling off to 0 across
        // _PingBorderWidth on either side of the current radius.
        float ring = 1.0 - saturate(abs(dist - radius) / max(_PingBorderWidth, 0.0001));

        float intensity = ring * fadeIn * fadeOut;

        // Overlapping rings COMBINE via max(), never +, so overlaps read as one coalescing
        // wavefront instead of translucency stacking to blown-out opacity.
        best = max(best, intensity);
    }

    Color = _PingColor * best;
}

#endif // HABITALES_PING_RING_INCLUDED
