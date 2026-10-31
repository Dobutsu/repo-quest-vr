Shader "Semiwork/Knowledge Glyph Ray" {
	{PROPERTIES}
	// RepoQuest: generated stand-in for AssetRipper's dummy shader (scripts/generate_shaders.py), written by hand
	// (unity-tools/ShaderOverrides). Light shining out of a picture: the pumpkin face on the Halloween menu truck
	// (TruckPumpkinFace), the glyphs of the Cube of Knowledge and the knowledge platforms. The mesh is
	// KnowledgeGlyphRayMeshUtility's volume, no UVs: a 1x1 square at z=0 widening to 1+_Spread at z=1, scaled to the
	// picture and the ray length. The generic stand-in filled the whole volume with _Color (a lit box, a green square
	// over the pumpkin). Here each pixel marches through the volume and adds up the picture projected along the rays,
	// so the light keeps the picture's shape and fades out with distance from it.
	SubShader {
		Tags { "Queue"="Transparent+1" "RenderType"="Transparent" "IgnoreProjector"="True" }
		Blend One One
		ColorMask RGB
		ZWrite Off
		ZTest LEqual
		// Back faces: the march runs from where the view ray enters the volume (or the camera, when inside) to them
		Cull Front

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#include "UnityCG.cginc"

			#define STEPS 16

			sampler2D _MainTex;
			float4 _MainTex_ST;
			fixed4 _Color;
			float _Intensity;
			float _Fade;
			float _Spread;
			float _FlipX;
			float _NearFadeStart;
			float _NearFadeEnd;

			struct appdata
			{
				float4 vertex : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
				float3 object : TEXCOORD0;
				float3 camera : TEXCOORD1;
				float distance : TEXCOORD2;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			v2f vert(appdata v)
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				o.pos = UnityObjectToClipPos(v.vertex);
				o.object = v.vertex.xyz;
				// Per eye here (the stereo camera position needs the instance/view id the fragment stage lacks)
				o.camera = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz;
				o.distance = distance(mul(unity_ObjectToWorld, v.vertex).xyz, _WorldSpaceCameraPos);
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				// Where the view ray enters the volume's bounding box: t runs from the camera (0) to this back face (1)
				float3 ray = i.object - i.camera;
				float wide = 0.5 * (1 + max(0, _Spread));
				float3 inverse = 1 / (abs(ray) < 1e-5 ? (ray >= 0 ? 1e-5 : -1e-5) : ray);
				float3 t1 = (float3(-wide, -wide, 0) - i.camera) * inverse;
				float3 t2 = (float3(wide, wide, 1) - i.camera) * inverse;
				float3 nearest = min(t1, t2);
				float enter = saturate(max(max(nearest.x, nearest.y), nearest.z));

				float stride = (1 - enter) / STEPS;
				float light = 0;
				for (int s = 0; s < STEPS; s++)
				{
					float3 p = i.camera + ray * (enter + (s + 0.5) * stride);
					// The picture grows with the volume: at depth z the square is 1 + _Spread * z wide
					float2 uv = p.xy / (1 + _Spread * saturate(p.z)) + 0.5;
					uv.x = lerp(uv.x, 1 - uv.x, _FlipX);
					float inside = all(uv == saturate(uv)) && p.z >= 0 && p.z <= 1;
					fixed4 picture = tex2Dlod(_MainTex, float4(TRANSFORM_TEX(uv, _MainTex), 0, 1));
					float falloff = (1 - p.z) * (1 - p.z);
					light += inside * picture.a * max(picture.r, max(picture.g, picture.b)) * falloff;
				}
				// Light gathered over the ray's length in the volume's units (ray length 1 = the whole volume deep)
				light *= length(ray) * stride * 4;

				float near = saturate((i.distance - _NearFadeStart) / max(1e-3, _NearFadeEnd - _NearFadeStart));
				float3 colour = _Color.rgb * _Intensity * _Fade * near * light;
				// No bloom on the Quest: a soft knee instead of clipping where many rays overlap
				return fixed4(1 - exp(-colour), 1);
			}
			ENDCG
		}
	}
}
