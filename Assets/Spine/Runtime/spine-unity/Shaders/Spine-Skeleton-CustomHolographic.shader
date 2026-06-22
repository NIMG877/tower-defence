Shader "Spine/Custom/Hotographic" {
	Properties{
		_MainTex("Main Texture", 2D) = "black" {}
		_NoiseTex ("Noise Texture", 2D) = "white" {}
        _ScrollSpeed ("Scroll Speed", float) = 1
		_NoiseColor("Noise Color",Color)=(1,1,1,1)
	}

	SubShader{
		Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }

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

		UsePass "Spine/Custom/Skeleton/Normal"

		Pass {
			Name "Hotographic"

			CGPROGRAM
			#pragma shader_feature _ _STRAIGHT_ALPHA_INPUT
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			sampler2D _MainTex;
			sampler2D _NoiseTex;
            float _ScrollSpeed;
			float4 _NoiseColor;

			struct VertexInput {
				float2 uv : TEXCOORD0;
				float4 vertexColor : COLOR;
			};

			struct VertexOutput {
				float2 uv : TEXCOORD0;
				float2 uvNoise :TEXCOORD1;
				float4 vertexColor : COLOR;
			};
			

			VertexOutput vert(VertexInput v) {
				VertexOutput o;
				o.uv = v.uv;
				o.uvNoise=v.uv;
				o.uvNoise.y+=_ScrollSpeed*_Time.y;
				o.vertexColor = v.vertexColor;
				return o;
			}


			float4 frag(VertexOutput i) : SV_Target {
				
				// 原始纹理颜色
                float4 col = tex2D(_MainTex, i.uv);
				// 噪声纹理
				float4 noise = tex2D(_NoiseTex, i.uvNoise);

				if(col.a>0)
				{
					col.rgb=dot(col.rgb, float3(0.299, 0.587, 0.114))*_NoiseColor;
					col.a*=noise.x;
				}
				col.rgb *= noise.rgb;
                return col;
			}
			ENDCG
		}
	}
	//CustomEditor "SpineShaderWithOutlineGUI"
}