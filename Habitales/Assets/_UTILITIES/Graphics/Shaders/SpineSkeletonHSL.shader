// Spine/Skeleton with a Photoshop-style Hue/Saturation adjustment layer folded into the fragment
// stage. Byte-for-byte identical to Spine/Skeleton while _AdjustAmount is 0 (the material default),
// so putting it on a worker material changes nothing until something dials the amount up.
//
// Why a shader at all: the fatigue look the art direction asks for is Hue -150 / Saturation -80 /
// Lightness 0, and a hue ROTATION cannot be expressed as a colour multiply. Spine's two writable
// tint channels — the skeleton's vertex colour and a material _Color — are both multiplies, so
// neither can reach it. The adjustment has to happen per-pixel, after the texture is sampled.
//
// Driven per-worker from WorkerWalker through a MaterialPropertyBlock, so every worker keeps
// sharing these two materials and only the fatigued ones read as fatigued.
Shader "Spine/Skeleton HSL Adjust" {
	Properties {
		_Cutoff ("Shadow alpha cutoff", Range(0,1)) = 0.1
		[NoScaleOffset] _MainTex ("Main Texture", 2D) = "black" {}
		[Toggle(_STRAIGHT_ALPHA_INPUT)] _StraightAlphaInput("Straight Alpha Texture", Int) = 0

		[Header(Hue Saturation Adjustment)]
		// Photoshop's Hue/Saturation dialog, same units: hue in degrees, the other two as the
		// −100..100 percentages that dialog uses. Defaults are the authored fatigue look, so
		// dragging Amount to 1 in the material inspector previews it without entering play mode.
		_HueShift ("Hue (degrees)", Range(-180,180)) = -150
		_Saturation ("Saturation (%)", Range(-100,100)) = -80
		_Lightness ("Lightness (%)", Range(-100,100)) = 0
		_AdjustAmount ("Adjust Amount", Range(0,1)) = 0

		[HideInInspector] _StencilRef("Stencil Reference", Float) = 1.0
		[HideInInspector][Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp("Stencil Comparison", Float) = 8 // Set to Always as default

		// Carried over from Spine/Skeleton so a material switched to this shader (and back) keeps
		// its outline authoring. Nothing here reads them — this shader has no outline pass.
		[HideInInspector] _OutlineWidth("Outline Width", Range(0,8)) = 3.0
		[HideInInspector] _OutlineColor("Outline Color", Color) = (1,1,0,1)
		[HideInInspector] _OutlineReferenceTexWidth("Reference Texture Width", Int) = 1024
		[HideInInspector] _ThresholdEnd("Outline Threshold", Range(0,1)) = 0.25
		[HideInInspector] _OutlineSmoothness("Outline Smoothness", Range(0,1)) = 1.0
		[HideInInspector][MaterialToggle(_USE8NEIGHBOURHOOD_ON)] _Use8Neighbourhood("Sample 8 Neighbours", Float) = 1
		[HideInInspector] _OutlineMipLevel("Outline Mip Level", Range(0,3)) = 0
	}

	SubShader {
		Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }

		Fog { Mode Off }
		Cull Off
		ZWrite Off
		Blend One OneMinusSrcAlpha
		Lighting Off

		Stencil {
			Ref[_StencilRef]
			Comp[_StencilComp]
			Pass Keep
		}

		Pass {
			Name "Normal"

			CGPROGRAM
			#pragma shader_feature _ _STRAIGHT_ALPHA_INPUT
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			sampler2D _MainTex;

			float _HueShift;
			float _Saturation;
			float _Lightness;
			float _AdjustAmount;

			struct VertexInput {
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
				float4 vertexColor : COLOR;
			};

			struct VertexOutput {
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float4 vertexColor : COLOR;
			};

			// HSL, not HSV, deliberately: the numbers above come out of an image editor's
			// Hue/Saturation dialog, which is HSL. Running them through HSV would land on a
			// visibly different colour for the same input.
			float3 RGBtoHSL (float3 c) {
				float maxc = max(c.r, max(c.g, c.b));
				float minc = min(c.r, min(c.g, c.b));
				float l = (maxc + minc) * 0.5;
				float d = maxc - minc;

				float h = 0.0;
				float s = 0.0;
				if (d > 1e-5) {
					s = l < 0.5 ? d / (maxc + minc) : d / (2.0 - maxc - minc);
					if (maxc == c.r)      h = (c.g - c.b) / d + (c.g < c.b ? 6.0 : 0.0);
					else if (maxc == c.g) h = (c.b - c.r) / d + 2.0;
					else                  h = (c.r - c.g) / d + 4.0;
					h /= 6.0;
				}
				return float3(h, s, l);
			}

			float Hue2RGB (float p, float q, float t) {
				t = frac(t);
				if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
				if (t < 0.5)       return q;
				if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
				return p;
			}

			float3 HSLtoRGB (float3 hsl) {
				if (hsl.y <= 1e-5) return hsl.zzz; // grey — no hue to reconstruct
				float q = hsl.z < 0.5 ? hsl.z * (1.0 + hsl.y) : hsl.z + hsl.y - hsl.z * hsl.y;
				float p = 2.0 * hsl.z - q;
				return float3(Hue2RGB(p, q, hsl.x + 1.0 / 3.0),
				              Hue2RGB(p, q, hsl.x),
				              Hue2RGB(p, q, hsl.x - 1.0 / 3.0));
			}

			// Takes and returns a PREMULTIPLIED colour — straight-alpha textures are premultiplied
			// at the top of frag before this runs. HSL is only meaningful on straight colour, so
			// the premultiply is undone around the adjustment.
			float4 AdjustHSL (float4 c) {
				if (_AdjustAmount <= 0.0) return c; // neutral: identical output to Spine/Skeleton

				// a == 0 with rgb > 0 is a legal PMA additive slot; leave those unscaled rather
				// than dividing by zero and blowing the colour out.
				bool opaqueEnough = c.a > 1e-4;
				float3 straight = opaqueEnough ? saturate(c.rgb / c.a) : c.rgb;

				float3 hsl = RGBtoHSL(straight);
				hsl.x = frac(hsl.x + _HueShift / 360.0);
				// Negative is exactly the image editor's formula (scale toward grey / black);
				// positive is the usual approximation of it (blend toward full / white).
				hsl.y = _Saturation >= 0.0 ? lerp(hsl.y, 1.0, _Saturation / 100.0)
				                           : hsl.y * (1.0 + _Saturation / 100.0);
				hsl.z = _Lightness  >= 0.0 ? lerp(hsl.z, 1.0, _Lightness / 100.0)
				                           : hsl.z * (1.0 + _Lightness / 100.0);

				float3 adjusted = lerp(straight, HSLtoRGB(hsl), saturate(_AdjustAmount));
				c.rgb = opaqueEnough ? adjusted * c.a : adjusted;
				return c;
			}

			VertexOutput vert (VertexInput v) {
				VertexOutput o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				o.vertexColor = v.vertexColor;
				return o;
			}

			float4 frag (VertexOutput i) : SV_Target {
				float4 texColor = tex2D(_MainTex, i.uv);

				#if defined(_STRAIGHT_ALPHA_INPUT)
				texColor.rgb *= texColor.a;
				#endif

				// Adjust the ART, then apply the vertex colour — deliberately in that order. The
				// vertex colour is where the day-night sun tint arrives (Walker.ApplyCompositeTint →
				// Skeleton.R/G/B/A), and rotating the hue of the *lighting* is not what anyone means
				// by a fatigued worker at night. This way the two compose the sane way round: the
				// worker's colours are adjusted, and the result is then lit.
				return AdjustHSL(texColor) * i.vertexColor;
			}
			ENDCG
		}

		// Unchanged from Spine/Skeleton — the shadow caster only needs the alpha cutout, and the
		// colour adjustment has nothing to say about it.
		Pass {
			Name "Caster"
			Tags { "LightMode"="ShadowCaster" }
			Offset 1, 1
			ZWrite On
			ZTest LEqual

			Fog { Mode Off }
			Cull Off
			Lighting Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_shadowcaster
			#pragma fragmentoption ARB_precision_hint_fastest
			#include "UnityCG.cginc"
			sampler2D _MainTex;
			fixed _Cutoff;

			struct VertexOutput {
				V2F_SHADOW_CASTER;
				float4 uvAndAlpha : TEXCOORD1;
			};

			VertexOutput vert (appdata_base v, float4 vertexColor : COLOR) {
				VertexOutput o;
				o.uvAndAlpha = v.texcoord;
				o.uvAndAlpha.a = vertexColor.a;
				TRANSFER_SHADOW_CASTER(o)
				return o;
			}

			float4 frag (VertexOutput i) : SV_Target {
				fixed4 texcol = tex2D(_MainTex, i.uvAndAlpha.xy);
				clip(texcol.a * i.uvAndAlpha.a - _Cutoff);
				SHADOW_CASTER_FRAGMENT(i)
			}
			ENDCG
		}
	}
}
