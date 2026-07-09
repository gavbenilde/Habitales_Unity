// CloudShadowOverlay — scrolling cloud shadows on the ground (ATMOSPHERE_BUILD_PLAN.md §4).
//
// A single quad hovering just above the tile tops (CloudShadowProjector keeps it covering
// region + hinterland). MULTIPLICATIVE blend (DstColor Zero): outputting 1 leaves the ground
// untouched, <1 darkens it — so this works over the gameplay tiles AND the hinterland without
// touching TileShader.shadergraph at all.
//
// Noise is sampled in WORLD XZ space, which is why the shadows conform to the isometric ground
// plane for free — no screen-space scaling tricks.
//
// All motion comes from AtmosphereDirector globals. _CloudPhase is an ACCUMULATED distance
// (integrated with the time-lapse factor in C#) — never time × speed, so cloud motion stays
// continuous when the action time-lapse steps 1× → 16×.
//
// Queue Transparent-50 (2950): after the hinterland (2900), before entities/sprites (3000) —
// standing plants and buildings are NOT darkened (ground-only shadows, plan §8 Q3).
Shader "Habitales/CloudShadowOverlay"
{
    Properties
    {
        _SoftMin ("Cloud Edge Min", Range(0, 1)) = 0.45
        _SoftMax ("Cloud Edge Max", Range(0, 1)) = 0.80
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-50"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend DstColor Zero  // multiplicative darken
        ZWrite Off
        Cull Off

        Pass
        {
            Name "CloudShadow"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _SoftMin;
                half _SoftMax;
            CBUFFER_END

            // GLOBALS — set every frame by AtmosphereDirector.
            float  _CloudShadowStrength; // 0..1, lerped per weather
            float  _CloudPhase;          // accumulated scroll distance (world units)
            float2 _CloudDir;            // normalized drift direction on world XZ
            float  _CloudScale;          // world-to-noise scale

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionHCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Cheap 2D value noise — world space is unbounded so no tiling texture needed.
            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f); // smoothstep interpolation

                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // Two octaves: big weather masses + smaller raggedness. Second octave drifts at a
            // different rate so the pattern never reads as one rigid sheet.
            float CloudMass(float2 wpos)
            {
                float2 uv = (wpos + _CloudDir * _CloudPhase) * _CloudScale;
                float n = ValueNoise(uv) * 0.65;
                n += ValueNoise(uv * 2.7 + float2(13.7, 71.3) + _CloudDir * (_CloudPhase * _CloudScale * 0.8)) * 0.35;
                return n;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float clouds = smoothstep(_SoftMin, _SoftMax, CloudMass(i.positionWS.xz));
                half mult = 1.0 - clouds * saturate(_CloudShadowStrength);
                return half4(mult, mult, mult, 1);
            }
            ENDHLSL
        }
    }
}
