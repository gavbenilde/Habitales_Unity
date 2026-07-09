// HeatHaze — fullscreen shimmer for Sunny × Dry season (ATMOSPHERE_BUILD_PLAN.md §6).
//
// Runs on URP 14's Full Screen Pass Renderer Feature (Habitales_URP_Renderer.asset →
// "Requirements: Color" must be ticked so the camera color lands in _BlitTexture).
// AtmosphereDirector toggles the feature off entirely outside Sunny×Dry — zero cost when idle —
// and drives _HazeStrength (escalated while a drought is active).
//
// The wobble is deliberately tiny (a few pixels) and masked to the lower two-thirds of the
// screen so the UI-heavy top edge stays crisp. It should be FELT, not seen.
Shader "Habitales/HeatHaze"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZWrite Off
        Cull Off
        ZTest Always

        Pass
        {
            Name "HeatHaze"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag

            // GLOBALS — set by AtmosphereDirector.
            float _HazeStrength; // 0..1 (feature is disabled entirely when ~0)
            float _WeatherTime;  // accumulated, speed-scaled clock — haze races in time-lapse

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

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

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                // Rising shimmer: two octaves scrolling UPWARD at different rates (heat rises).
                float n = ValueNoise(float2(uv.x * 40.0, uv.y * 25.0 - _WeatherTime * 1.5));
                n += ValueNoise(float2(uv.x * 90.0 + 31.7, uv.y * 60.0 - _WeatherTime * 2.3));
                n -= 1.0; // recenter both octaves to roughly -1..1

                // Strongest low on screen, fading out by the top third (UI stays crisp).
                float mask = smoothstep(0.85, 0.35, uv.y);

                float amp = _HazeStrength * 0.0035 * mask; // ≤ a few pixels at 1080p
                float2 offset = float2(n * amp * 0.4, n * amp); // mostly vertical wobble

                float3 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset).rgb;
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
