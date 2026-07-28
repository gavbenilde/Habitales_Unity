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
// (_PingSpeed / _PingMaxRadius / _PingBorderWidth / _PingFadeInTime / _PingColor / _PingPlaneMode /
// _PingOverlayScale).
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
    float4 color;     // per-ping RGBA hue (PingBufferEntry.color) — the winning ring tints this pixel
};

StructuredBuffer<PingData> _PingBuffer;
int   _PingCount;
float _HabitalesClock; // GLOBAL — set once per frame by PingDirector.Update() (Shader.SetGlobalFloat), halts behind popups

// Material-level tunables (_PingSpeed / _PingMaxRadius / _PingBorderWidth / _PingFadeInTime /
// _PingColor / _PingOverlayScale) are EXPOSED Shader Graph Blackboard properties. Shader Graph
// declares them in the UnityPerMaterial cbuffer, which is emitted BEFORE this include in the
// generated pass — so the function below can REFERENCE them by name, but this file must NOT
// (re)declare them.
//
// ⚠ Do NOT add `float _PingSpeed; …` (or any of the six) back here: a file-scope global with the
// same name as the cbuffer member collides and the shader fails to compile with a "redefinition"
// error. Only symbols with NO Blackboard property are declared above — _PingBuffer / _PingCount are
// MaterialPropertyBlock-bound and have no Blackboard-able type; _HabitalesClock is a plain global
// bound by Shader.SetGlobalFloat. See PingRingShaderSpec.md.

// WorldPos:  the fragment's world-space position  — used when _PingPlaneMode < 0.5 (the GROUND quad).
// ObjectPos: the fragment's object-space position — used when _PingPlaneMode >= 0.5 (the OVERLAY quad).
// Color:     RGBA to feed into Emission (or Base Color, for an Unlit target) — see the graph spec.
//
// _PingPlaneMode is a per-material toggle (an exposed Blackboard Float): 0 = measure the ring in world
// XZ (a ground-projected quad), 1 = measure in the quad's LOCAL XY (a quad glued to the camera, whose
// own plane then reads as flat UI — no skew at any camera angle). PingDirector hands each surface its
// ping CENTERS already in the matching space (world for ground, quad-local for overlay), so here we
// only pick which pair of axes to compare.
//
// _PingOverlayScale (exposed Blackboard Float, overlay material only — leave at 1 on the ground
// material, where it's unused): ObjectPos is mesh-local space, which is ALWAYS ~-0.5..0.5 for a unit
// Quad regardless of the GameObject's Transform scale — Shader Graph's "Object" position node reads
// raw mesh data, never the Transform. So when the overlay quad has to be scaled way up (e.g. 60x) to
// blanket the camera frustum, _PingSpeed/_PingMaxRadius/_PingBorderWidth would otherwise have to be
// tuned as tiny quad-fractions (radius ~0.01) instead of real numbers, and that tuning breaks again
// every time the quad gets rescaled for a different camera setup. Multiplying ObjectPos by
// _PingOverlayScale == that Transform's uniform scale factor converts back to world-equivalent units,
// so the other four ring params can be tuned with the SAME numbers as the ground material (e.g. the
// spec's 6.0 / 6.0 / 0.35 / 0.15 defaults) instead of re-derived fractions.
void PingRing_float(float3 WorldPos, float3 ObjectPos, out float4 Color)
{
    bool   objectSpace = _PingPlaneMode >= 0.5;
    float2 fragPos     = objectSpace ? ObjectPos.xy * _PingOverlayScale : WorldPos.xz;

    float  best      = 0.0;
    float4 bestColor = _PingColor; // fallback when no ping contributes to this pixel

    for (int i = 0; i < _PingCount; i++)
    {
        PingData p = _PingBuffer[i];
        float age = _HabitalesClock - p.startTime;
        if (age < 0.0) continue; // hasn't started yet (staggered start offset)

        float radius = age * _PingSpeed;
        if (radius > _PingMaxRadius) continue; // fully expired — analytically invisible, cheap skip

        float2 centerPos = objectSpace ? p.center.xy * _PingOverlayScale : p.center.xz;
        float dist = distance(fragPos, centerPos);

        // Solid border fading IN over _PingFadeInTime from the ping's start...
        float fadeIn = saturate(age / max(_PingFadeInTime, 0.0001));
        // ...ring band expanding small -> big, fading OUT as radius approaches max radius.
        float fadeOut = saturate(1.0 - (radius / max(_PingMaxRadius, 0.0001)));

        // Analytic distance-field ring: 1 at dist == radius, falling off to 0 across
        // _PingBorderWidth on either side of the current radius.
        float ring = 1.0 - saturate(abs(dist - radius) / max(_PingBorderWidth, 0.0001));

        float intensity = ring * fadeIn * fadeOut;

        // Overlapping rings COMBINE via max(), never +, so overlaps read as one coalescing
        // wavefront instead of translucency stacking to blown-out opacity. The winning
        // (max-intensity) ping also supplies this pixel's hue, so differently-colored rings
        // (e.g. onboarding's white crew ping vs a red fatigue ping) don't average into mud.
        if (intensity > best)
        {
            best      = intensity;
            bestColor = p.color;
        }
    }

    // Per-ping hue × the material's global _PingColor tint (keep _PingColor white for true colors;
    // push it HDR-bright for bloom) × the analytic ring intensity.
    Color = bestColor * _PingColor * best;
}

#endif // HABITALES_PING_RING_INCLUDED
