Shader "REPO/Truck Halloween Lightning" {
	{PROPERTIES}
	// RepoQuest: generated stand-in for AssetRipper's dummy shader (scripts/generate_shaders.py), written by hand
	// (unity-tools/ShaderOverrides). The lightning bolts in the Halloween menus: TruckHalloweenLighting draws them with
	// LineRenderers facing the view, texture stretched along the bolt (u along it, v across), in the lightning colour
	// with its strength in the vertex alpha, added to the scene (Blend One One). The generic stand-in added the colour
	// over the whole strip, a flat band. Here a bright core _CoreWidth times narrower than the strip, white-hot in the
	// middle, inside a soft halo of _HaloStrength.
	SubShader {
		Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
		Blend One One
		ZWrite Off
		ZTest LEqual
		Cull Off

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			float _CoreWidth;
			float _HaloStrength;

			struct appdata
			{
				float4 vertex : POSITION;
				fixed4 color : COLOR;
				float2 uv : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
				fixed4 color : COLOR;
				float2 uv : TEXCOORD0;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			v2f vert(appdata v)
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				o.pos = UnityObjectToClipPos(v.vertex);
				o.color = v.color;
				o.uv = v.uv;
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				float across = abs(i.uv.y * 2 - 1);
				float core = saturate(1 - across * max(_CoreWidth, 1));
				float halo = (1 - across) * (1 - across) * _HaloStrength;
				float3 c = i.color.rgb * (core + halo) + core * core * 0.6;
				return fixed4(c * i.color.a, 1);
			}
			ENDCG
		}
	}
}
