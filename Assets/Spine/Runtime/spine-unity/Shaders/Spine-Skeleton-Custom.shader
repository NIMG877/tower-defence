Shader "Spine/Skeleton-XRotate-Optimized" {
	Properties {
		[NoScaleOffset] _MainTex ("Main Texture", 2D) = "black" {}
		[Toggle(_STRAIGHT_ALPHA_INPUT)] _StraightAlphaInput("Straight Alpha Texture", Int) = 0
		[HideInInspector] _StencilRef("Stencil Reference", Float) = 1.0
		[HideInInspector][Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp("Stencil Comparison", Float) = 8

		// 旋转控制参数
		_RotateAngle("Rotation Angle", Range(-180, 180)) = 0
	}

	SubShader {
		Tags { 
			"Queue"="Transparent" 
			"IgnoreProjector"="True" 
			"RenderType"="Transparent" 
			"PreviewType"="Plane" 
		}

		// 优化后的渲染设置
		Fog { Mode Off }
		Cull Off
		ZWrite Off
		Blend One OneMinusSrcAlpha
		Lighting Off // 禁用光照计算

		Stencil {
			Ref[_StencilRef]
			Comp[_StencilComp]
			Pass Keep
		}

		Pass {
			Name "BASIC"
			Tags { "LightMode" = "ForwardBase" }

			CGPROGRAM
			#pragma shader_feature _ _STRAIGHT_ALPHA_INPUT
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#include "UnityCG.cginc"

			sampler2D _MainTex;
			float _RotateAngle;

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

				// X轴旋转矩阵
				float angleRad = radians(_RotateAngle);
				float cosA = cos(angleRad);
				float sinA = sin(angleRad);
				float4x4 scaleMatrix={
					float4(1,0,0,0),
					float4(0,1/cosA,0,0),
					float4(0,0,1,0),
					float4(0,0,0,1),
				};
				float4x4 rotMatrix = {
					float4(1, 0, 0, 0),
					float4(0, cosA, -sinA, 0),
					float4(0, sinA, cosA, 0),
					float4(0, 0, 0, 1)
				};

				// 应用旋转并转换坐标
				float4 rotatedPos = mul(rotMatrix, mul(scaleMatrix, v.vertex));
				rotatedPos=UnityObjectToClipPos(rotatedPos);
				o.pos = UnityObjectToClipPos(v.vertex);
				o.pos.z=o.pos.w*rotatedPos.z/rotatedPos.w;
				o.uv = v.uv;
				o.vertexColor = v.vertexColor;
				return o;
			}

			half4 frag (VertexOutput i) : SV_Target {
				half4 texColor = tex2D(_MainTex, i.uv);
				return texColor * i.vertexColor;
			}
			ENDCG
		}
		
	}
}