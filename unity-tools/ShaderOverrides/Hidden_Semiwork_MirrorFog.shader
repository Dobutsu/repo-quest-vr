Shader "Hidden/Semiwork/MirrorFog" {
	{PROPERTIES}
	// RepoQuest: generated stand-in for AssetRipper's dummy shader (scripts/generate_shaders.py), written by hand
	// (unity-tools/ShaderOverrides). MirrorReflection's fog pass: the reflection camera renders into a raw texture
	// and a depth texture (_ReflectionDepthTex), then this blit fogs the far reflection out. The game gives the
	// inverse of the reflection camera's GPU view-projection (rendered into a texture), the camera's position and
	// the fog colour and distances (_MirrorFogParams: start, end). The generic stand-in copied the raw texture
	SubShader {
		ZWrite Off
		ZTest Always
		Cull Off

		Pass {
			CGPROGRAM
			#pragma vertex vert_img
			#pragma fragment frag
			#include "UnityCG.cginc"

			sampler2D _MainTex;
			sampler2D _ReflectionDepthTex;
			float4x4 _ReflectionInverseViewProjection;
			float4 _ReflectionCameraPosition;
			fixed4 _MirrorFogColor;
			float4 _MirrorFogParams;

			fixed4 frag(v2f_img i) : SV_Target
			{
				fixed4 colour = tex2D(_MainTex, i.uv);
				float depth = tex2D(_ReflectionDepthTex, i.uv).r;
				float2 ndc = i.uv * 2 - 1;
				#if UNITY_UV_STARTS_AT_TOP
				// The GPU projection for a texture is flipped upside down on these APIs
				ndc.y = -ndc.y;
				#endif
				float4 world = mul(_ReflectionInverseViewProjection, float4(ndc, depth, 1));
				float distance = length(world.xyz / world.w - _ReflectionCameraPosition.xyz);
				float fog = saturate((distance - _MirrorFogParams.x) / max(1e-3, _MirrorFogParams.y - _MirrorFogParams.x));
				colour.rgb = lerp(colour.rgb, _MirrorFogColor.rgb, fog);
				return colour;
			}
			ENDCG
		}
	}
}
