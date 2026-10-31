Shader "REPO/Truck Halloween Glow" {
	{PROPERTIES}
	// RepoQuest: generated stand-in for AssetRipper's dummy shader (scripts/generate_shaders.py), written by hand
	// (unity-tools/ShaderOverrides). The coloured light pools on the road in the Halloween menus:
	// TruckHalloweenLighting.GlowCreate lays a Unity quad flat on the ground for each of its glowPools, sized to the
	// pool, with the pool's colour in _Color (a property block), added to the scene (Blend One One). The generic stand-in
	// added the colour over the whole quad, a lit rectangle. Here the light fades out from the middle to the quad's
	// edges, mottled by the noise texture like a wet road.
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
			#pragma multi_compile_instancing
			#include "UnityCG.cginc"

			fixed4 _Color;
			sampler2D _NoiseTex;

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float2 world : TEXCOORD1;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			v2f vert(appdata v)
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				o.world = mul(unity_ObjectToWorld, v.vertex).xz;
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				float2 centred = i.uv * 2 - 1;
				float pool = saturate(1 - dot(centred, centred));
				pool *= pool;
				float mottle = 0.6 + 0.8 * tex2D(_NoiseTex, i.world * 0.12 + _Time.x * 0.1).r;
				return fixed4(_Color.rgb * _Color.a * pool * mottle, 1);
			}
			ENDCG
		}
	}
}
