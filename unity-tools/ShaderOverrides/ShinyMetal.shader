Shader "ShinyMetal" {
	{PROPERTIES}
	// RepoQuest: generated stand-in for AssetRipper's dummy shader (scripts/generate_shaders.py), written by hand
	// (unity-tools/ShaderOverrides). Gold valuables (Museum's Golden Swirl, Arctic's small gold items). As compiled:
	// Standard lighting of _Texture with a constant metallic 0.6 and smoothness 0.8, plus a glint in _ShineColor where
	// the reflection cubemap's red, seen along the reflected view ray, meets the bright streaks of the texture's red,
	// each through a smoothstep between the two ends of its remap. The generic stand-in lit the texture matte and had
	// no glint.
	SubShader {
		Tags { "Queue"="Geometry" "RenderType"="Opaque" }
		LOD 200

		CGPROGRAM
		#pragma surface surf Standard fullforwardshadows
		#pragma target 3.0
		#pragma multi_compile_instancing
		#include "Assets/RepoQuest/Shaders/RepoQuestLamps.cginc"

		sampler2D _Texture;
		sampler2D _Normal;
		samplerCUBE _Reflection;
		float4 _Shine_Remap;
		float4 _T_Remap;
		float4 _ShineColor;

		struct Input
		{
			float2 uv_Texture;
			float2 uv_Normal;
			float3 worldPos;
			float3 worldRefl;
			float3 worldNormal;
			INTERNAL_DATA
		};

		void surf(Input IN, inout SurfaceOutputStandard o)
		{
			fixed4 c = tex2D(_Texture, IN.uv_Texture);
			o.Albedo = c.rgb;
			o.Normal = UnpackNormal(tex2D(_Normal, IN.uv_Normal));
			o.Metallic = 0.6;
			o.Smoothness = 0.8;

			float streaks = smoothstep(_T_Remap.x, _T_Remap.y, c.r);
			float glint = smoothstep(_Shine_Remap.x, _Shine_Remap.y,
			                         texCUBE(_Reflection, WorldReflectionVector(IN, o.Normal)).r * streaks);
			o.Emission = _ShineColor.rgb * glint;
			#ifdef UNITY_PASS_FORWARDBASE
			o.Emission += RepoQuestLampsMetallic(IN.worldPos, WorldNormalVector(IN, o.Normal), o.Albedo, o.Metallic, o.Smoothness);
			#endif
			o.Alpha = 1;
		}
		ENDCG
	}
	Fallback "Diffuse"
}
