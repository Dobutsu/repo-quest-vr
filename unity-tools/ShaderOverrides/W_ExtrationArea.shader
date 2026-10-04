Shader "W/ExtrationArea" {
	{PROPERTIES}
	// RepoQuest: generated stand-in for AssetRipper's dummy shader (scripts/generate_shaders.py), written by hand
	// (unity-tools/ShaderOverrides). The extraction point's danger zone, shown for the three-second countdown.
	// The original (read from its compiled code) draws nothing on the cube itself (9.6 x 15.4 x 9.4 m around the
	// point): from the camera's depth texture it finds the scene surface behind each pixel and lights the surfaces
	// that cross a box of half-size _Size around its centre (0.2 of the unit cube: 3.8 x 3.7 m, just around the
	// platform, its bottom on the floor) with a dashed red line _Width wide, plus a glow of _GradientStr fading out
	// over _GradientWidth outside it; the dashes are _NrOfLines around the centre and turn with _SpinSpeed (0 for
	// photosensitive players). A depth texture costs a second depth pass of the whole scene per eye on the Quest, so
	// this draws the floor part as geometry: the cube's bottom face becomes a quad on the floor at the box's bottom,
	// with the same line and glow; the other faces collapse to nothing. Lines on walls and people crossing the box
	// are not drawn.
	SubShader {
		Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off
		ZTest LEqual
		Cull Off
		Offset -1, -1

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#include "UnityCG.cginc"

			fixed4 _Color;
			float _NrOfLines;
			float _Size;
			float _Smooth;
			float _Width;
			float _GradientStr;
			float _GradientWidth;
			float _SpinSpeed;

			struct appdata
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
				float3 local : TEXCOORD0;
				float3 world : TEXCOORD1;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			v2f vert(appdata v)
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				// Unity's cube: the bottom face (normal -y, corners at +-0.5) spans the box and its glow on the
				// floor; every other vertex goes to the centre, so their triangles have no area
				float bottom = step(v.normal.y, -0.5);
				float extent = 2 * (_Size + _GradientWidth + _Smooth);
				float3 local = float3(v.vertex.x * extent, -_Size, v.vertex.z * extent) * bottom;
				o.pos = UnityObjectToClipPos(float4(local, 1));
				o.local = local;
				o.world = mul(unity_ObjectToWorld, float4(local, 1)).xyz;
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				// Dashes around the vertical axis through the zone's centre, as the original numbers them
				float3 centre = unity_ObjectToWorld._m03_m13_m23;
				float angle = atan2(centre.x - i.world.x, centre.z - i.world.z);
				float phase = frac((angle * 0.31830988 + 1) * _NrOfLines * 0.5 + _SpinSpeed * _Time.y);
				float dash = smoothstep(0, 1, saturate((abs(0.5 - phase) - 0.1) * 10));

				// The box's outline on the floor (inside it the original lights nothing)
				float distance = max(max(abs(i.local.x), abs(i.local.z)), _Size) - _Size;
				float outside = smoothstep(0, _Smooth, distance);
				float laser = outside * (1 - smoothstep(0, _Smooth, distance - _Width)) * dash;
				float glow = (1 - smoothstep(0, _GradientWidth, distance)) * outside * _GradientStr * (0.9 + 0.1 * dash);

				return fixed4(saturate(_Color.rgb), saturate(max(glow, laser)));
			}
			ENDCG
		}
	}
}
