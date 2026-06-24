Shader "TD/Characters/Spine Arknights Depth" {
	Properties {
		[NoScaleOffset] _MainTex ("Main Texture", 2D) = "black" {}
		[Toggle(_STRAIGHT_ALPHA_INPUT)] _StraightAlphaInput("Straight Alpha Texture", Int) = 0

		// The visible Spine mesh stays camera-facing. Only depth is projected as if
		// the character were standing upright in the 3D stage.
		_DepthPoseAngle ("Upper Body Depth Angle", Range(-89,89)) = -30
		_LowerDepthPoseAngle ("Lower Body Depth Angle", Range(-89,89)) = 0
		_DepthAnchorY ("Depth Anchor Local Y", Float) = 0
		_DepthSplitY ("Depth Split Local Y", Float) = 0
		_LowerDepthBlend ("Lower Body Blend Range", Float) = 0
		_DepthOffset ("Depth Offset", Float) = 0
		_MaxRayDepthCorrection ("Max Ray Depth Correction", Float) = 1

		[HideInInspector] _StencilRef("Stencil Reference", Float) = 1.0
		[HideInInspector][Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp("Stencil Comparison", Float) = 8
	}

	SubShader {
		Tags {
			"Queue"="Transparent"
			"IgnoreProjector"="True"
			"RenderType"="Transparent"
			"PreviewType"="Plane"
		}

		Fog { Mode Off }
		Cull Off
		Lighting Off

		Stencil {
			Ref[_StencilRef]
			Comp[_StencilComp]
			Pass Keep
		}

		Pass {
			Name "Color"
			Tags { "LightMode" = "ForwardBase" }

			ZWrite Off
			ZTest LEqual
			Blend One OneMinusSrcAlpha

			CGPROGRAM
			#pragma shader_feature _ _STRAIGHT_ALPHA_INPUT
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 2.0
			#pragma multi_compile_instancing
			#include "UnityCG.cginc"

			sampler2D _MainTex;
			#include "CGIncludes/TD-Spine-ArknightsDepth.cginc"

			struct VertexInput {
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
				float4 vertexColor : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VertexOutput {
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float4 vertexColor : COLOR;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			VertexOutput vert (VertexInput v) {
				VertexOutput o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				o.pos = TDSpineArknightsDepthClipPosition(v.vertex);
				o.uv = v.uv;
				o.vertexColor = v.vertexColor;
				return o;
			}

			float4 frag (VertexOutput i) : SV_Target {
				float4 texColor = tex2D(_MainTex, i.uv);

				#if defined(_STRAIGHT_ALPHA_INPUT)
				texColor.rgb *= texColor.a;
				#endif

				return texColor * i.vertexColor;
			}
			ENDCG
		}
	}
}
