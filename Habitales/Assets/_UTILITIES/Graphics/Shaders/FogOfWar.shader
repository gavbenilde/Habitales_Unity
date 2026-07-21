// FogOfWar — the vision cutoff beyond the hinterland (ATMOSPHERE_BUILD_PLAN.md §2).
// A single world-anchored quad floating above the tiles, drawn by FogOfWarRenderer, that
// occludes the world with distance: clear over the playable region, ramping to fully solid
// past the hinterland's outer rings.
//
// NOT the old vertical Fog_Orthographic backdrop (that hides the void BELOW the island) and
// NOT a SceneDepth effect — opacity comes from _MaskTex, a tiny R8 texture FogOfWarRenderer
// bakes from BFS ring distance (0 = region, 1 = beyond the hinterland). One texel per grid
// cell, bilinear-smoothed; world-anchored, so camera pans never swim the fog.
//
// Fog color is the _HorizonColor GLOBAL (owned by AtmosphereDirector). It is now INDEPENDENT
// of the camera clear color (a separate AtmosphereDirector field), so the edge fog and the
// sky behind it can be tuned apart — a horizon line shows where they differ.
//
// A little value noise, scrolled by the _WeatherTime/_CloudDir globals, erodes the gradient
// band only (mask 0 and 1 are untouched) so the edge reads as drifting fog, not a vignette.
//
// Queue Transparent+100 (3100): over the hinterland/tiles (Geometry), the CloudShadowOverlay
// quad (2950) and entities/sprites (3000). ZWrite Off — it occludes with alpha, not depth.
Shader "Habitales/FogOfWar"
{
    Properties
    {
        _MaskTex       ("Fog Mask (set by FogOfWarRenderer — leave empty)", 2D) = "white" {}
        _MaxOpacity    ("Max Opacity (1 = fully blocks vision)", Range(0, 1)) = 1.0
        _NoiseScale    ("Edge Noise Blob Size (world units)", Range(0.25, 8)) = 2.0
        _NoiseStrength ("Edge Noise Strength", Range(0, 1)) = 0.35
        _DriftSpeed    ("Edge Drift Speed (world units / weather-sec)", Range(0, 2)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off // one flat quad — never vanish on a flipped winding

        Pass
        {
            Name "FogOfWarForward"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MaskTex_ST;
                half _MaxOpacity;
                half _NoiseScale;
                half _NoiseStrength;
                half _DriftSpeed;
            CBUFFER_END

            // GLOBALS (set by AtmosphereDirector) — intentionally not in Properties.
            half4 _HorizonColor;
            float _WeatherTime;
            float2 _CloudDir;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float2 worldXZ     : TEXCOORD1; // for world-anchored edge noise
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Bilinear value noise — cheap, no texture fetch, good enough for a soft fog edge.
            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionHCS = TransformWorldToHClip(positionWS);
                o.uv = v.uv;
                o.worldXZ = positionWS.xz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv).r;

                // Erode ONLY the gradient band: band = mask*(1-mask)*4 peaks at mask 0.5 and is
                // zero at 0 and 1 — the region stays perfectly clear, the far fog perfectly solid.
                float2 drift = _CloudDir * (_WeatherTime * _DriftSpeed);
                float  noise = ValueNoise((i.worldXZ + drift) / max(_NoiseScale, 0.01));
                half   band = mask * (1.0 - mask) * 4.0;
                half   fog = saturate(mask + (noise - 0.5) * _NoiseStrength * band);

                return half4(_HorizonColor.rgb, fog * _MaxOpacity);
            }
            ENDHLSL
        }
    }
}
