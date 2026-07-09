// HinterlandTile — monochrome ghost tiles beyond the playable region (ATMOSPHERE_BUILD_PLAN.md §2).
// Drawn by HinterlandRenderer via Graphics.DrawMeshInstanced.
//
// Samples the SAME tile albedo art the gameplay tiles use (assign e.g. _ART/_Tiles/Tile_0.tiff
// to _MainTex) and desaturates it — a greyscale version of the real tile, not a silhouette.
//
// Deliberately UNLIT with a fixed fake shade direction: the scene's directional light spins 360°
// during action time-lapses (DayNightCycleHandler), and the hinterland should stay a calm,
// stable backdrop rather than strobe with it.
//
// CONSTANT COLOR AT ANY DISTANCE — no per-ring fade. The hinterland is just "more world";
// cutting off vision is the FogOfWar layer's job (FogOfWarRenderer + FogOfWar.shader), which
// draws a world-anchored fog quad ON TOP of these tiles. The old per-instance _Fade dissolve
// toward _HorizonColor is gone: two systems painting the same distance cue fought each other.
//
// OPAQUE (Queue "Geometry", 2000). No transparent sort fights, and the tiles land in the depth
// buffer/texture like real geometry (DepthOnly pass below), so any SceneDepth-reading effect
// sees the hinterland.
Shader "Habitales/HinterlandTile"
{
    Properties
    {
        _MainTex      ("Tile Albedo (use the gameplay tile texture)", 2D) = "grey" {}
        _Saturation   ("Residual Saturation (0 = full mono)", Range(0, 1)) = 0.0
        _BaseColor    ("Tint", Color) = (0.62, 0.63, 0.66, 1)
        _ShadeStrength("Fake Shade Strength", Range(0, 1)) = 0.5
        _ValueJitter  ("Per-Tile Value Jitter", Range(0, 0.2)) = 0.06
        [Toggle] _DebugNormals ("DEBUG: show normals as color", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite On   // opaque solid field — writes depth like real geometry
        Cull Back

        Pass
        {
            Name "HinterlandForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _BaseColor;
                half  _Saturation;
                half  _ShadeStrength;
                half  _ValueJitter;
                half  _DebugNormals;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1; // for the derivative-normal fallback
                float2 uv          : TEXCOORD2;
                float2 tileOrigin  : TEXCOORD3; // instance world XZ, for stable per-tile jitter
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionHCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.tileOrigin = float2(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m23);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                // Mesh normal, with a derivative fallback: if the interpolated normal is
                // degenerate (mesh imported without usable normals), reconstruct the geometric
                // face normal from world-position derivatives so bevels ALWAYS shade.
                float3 n = i.normalWS;
                if (dot(n, n) < 1e-4)
                {
                    n = cross(ddy(i.positionWS), ddx(i.positionWS));
                    if (n.y < 0) n = -n; // fallback orientation: this is a ground field, face up
                }
                n = normalize(n);

                if (_DebugNormals > 0.5)
                    return half4(n * 0.5 + 0.5, 1); // flat single color ⇒ mesh normals are broken

                // The real tile art, desaturated — the mesh keeps its surface detail.
                half3 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                half  grey = dot(tex, half3(0.299, 0.587, 0.114));
                half3 mono = lerp(grey.xxx, tex, _Saturation);

                // Fixed fake light — NOT the scene light (it rotates during time-lapse).
                const float3 shadeDir = normalize(float3(0.5, 1.0, 0.25));
                half shade = lerp(1.0 - _ShadeStrength, 1.0,
                                  saturate(dot(n, shadeDir)));

                // Stable per-tile value jitter so the field doesn't read as a flat sheet.
                half jitter = (Hash21(floor(i.tileOrigin)) - 0.5) * 2.0 * _ValueJitter;

                half3 col = saturate(mono * _BaseColor.rgb * shade + jitter);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // DepthOnly — puts the hinterland into _CameraDepthTexture so any SceneDepth-reading
        // effect (heat haze, depth-based graphs) sees it. Robust whether URP uses a depth
        // prepass or copy-depth.
        Pass
        {
            Name "HinterlandDepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthVaryings depthVert(DepthAttributes v)
            {
                DepthVaryings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half depthFrag(DepthVaryings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                return 0;
            }
            ENDHLSL
        }
    }
}
