Shader "Semiwork/Mirror" {
	{PROPERTIES}
	// RepoQuest: generated stand-in for AssetRipper's dummy shader (scripts/generate_shaders.py), written by hand
	// (unity-tools/ShaderOverrides). The shop mirror next to the cosmetic machine (MirrorReflection): the game renders
	// the reflection into _ReflectionTex (a property block) from a camera at the reflected eye, turned rather than
	// mirrored, with the eye's frustum (in VR RepoXR's MirrorPatches does it for each eye just before that eye
	// renders; in single-pass, before the frame, the right eye's into _ReflectionTexRight), so the mirror shows its
	// eye's texture at its own screen position, flipped left to right. The generic stand-in
	// ignored the texture: a white mirror. Surface normal distortion, a fresnel sheen and grime on top, as the
	// properties describe; _MirrorDistanceFade (1 when the game stops rendering a far reflection) fades it out
	SubShader {
		Tags { "Queue"="Geometry" "RenderType"="Opaque" }
		ZWrite On
		ZTest LEqual
		Cull Back

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma multi_compile_fog
			#include "UnityCG.cginc"

			fixed4 _Color;
			sampler2D _ReflectionTex;
			sampler2D _ReflectionTexRight;
			sampler2D _BumpMap;
			float4 _BumpMap_ST;
			float _DistortStrength;
			fixed4 _FresnelColor;
			float _FresnelStrength;
			float _FresnelPower;
			sampler2D _GrimeTex;
			float4 _GrimeTex_ST;
			fixed4 _GrimeColor;
			float _GrimeStrength;
			float _GrimeDistort;
			float _MirrorDistanceFade;

			struct appdata
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				float2 uv : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float4 screen : TEXCOORD1;
				float3 normal : TEXCOORD2;
				float3 view : TEXCOORD3;
				UNITY_FOG_COORDS(4)
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
				o.screen = ComputeNonStereoScreenPos(o.pos);
				o.normal = UnityObjectToWorldNormal(v.normal);
				o.view = WorldSpaceViewDir(v.vertex);
				UNITY_TRANSFER_FOG(o, o.pos);
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
				float grime = tex2D(_GrimeTex, TRANSFORM_TEX(i.uv, _GrimeTex)).r * _GrimeStrength;
				float2 bump = UnpackNormal(tex2D(_BumpMap, TRANSFORM_TEX(i.uv, _BumpMap))).xy;

				float2 uv = i.screen.xy / i.screen.w;
				uv.x = 1 - uv.x;
				uv += bump * (_DistortStrength + grime * _GrimeDistort * 0.05);
				// The right eye's reflection: its own texture in single-pass, the same one in multi-pass
			fixed3 colour = (unity_StereoEyeIndex == 0 ? tex2D(_ReflectionTex, uv) : tex2D(_ReflectionTexRight, uv)).rgb * _Color.rgb;
				colour = lerp(colour, colour * 0.15, saturate(_MirrorDistanceFade));

				float facing = saturate(dot(normalize(i.normal), normalize(i.view)));
				colour += _FresnelColor.rgb * _FresnelStrength * pow(1 - facing, _FresnelPower);
				colour = lerp(colour, _GrimeColor.rgb, saturate(grime * _GrimeColor.a));

				fixed4 c = fixed4(colour, 1);
				UNITY_APPLY_FOG(i.fogCoord, c);
				return c;
			}
			ENDCG
		}
	}
}
