Shader "REPO/Truck Halloween Fog" {
	{PROPERTIES}
	// RepoQuest: generated stand-in for AssetRipper's dummy shader (scripts/generate_shaders.py), written by hand
	// (unity-tools/ShaderOverrides). The mist around the truck in the Halloween menus: TruckHalloweenLighting.FogCreate
	// makes a particle system of 64 billboards, 14-22 m across, in the truck's space, coloured by its particleColor
	// (alpha 0.12) faded in and out over their life, and lit by up to two of the scene's lights through
	// _FogLightPositions (xyz, range in w) and _FogLightColors. The generic stand-in drew each billboard as a flat
	// square. Here each one is a soft round puff broken up by the noise texture, lit by _BaseLight plus the lights
	// (times _LightBoost), and faded out close to the camera
	// (_NearFade: none nearer than x metres, all of it from y metres) so the puffs never fill the view: in VR they
	// also turn with the head's roll, which a soft round puff hides.
	SubShader {
		Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off
		ZTest LEqual
		Cull Off

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog
			#include "UnityCG.cginc"

			sampler2D _NoiseTex;
			float _LightBoost;
			float _NoiseScale;
			float _NoiseSpeed;
			float4 _BaseLight;
			float4 _NearFade;
			float4 _FogLightPositions[2];
			float4 _FogLightColors[2];

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
				float3 world : TEXCOORD1;
				UNITY_FOG_COORDS(2)
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
				o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
				UNITY_TRANSFER_FOG(o, o.pos);
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				// A soft round puff instead of the quad
				float2 centred = i.uv * 2 - 1;
				float puff = saturate(1 - dot(centred, centred));
				puff *= puff;

				// Drifting noise in world space, so neighbouring puffs share their clumps
				float2 drift = float2(_Time.y, _Time.y * 0.6) * _NoiseSpeed * 0.1;
				float noise = tex2D(_NoiseTex, i.world.xz * _NoiseScale * 0.1 + drift).r;
				noise = 0.35 + 1.3 * noise * tex2D(_NoiseTex, i.world.xz * _NoiseScale * 0.27 - drift * 1.7).r * 2;

				// None right at the camera, where a puff would cover the whole view
				float distance = length(i.world - _WorldSpaceCameraPos);
				float near = _NearFade.y > _NearFade.x ? smoothstep(_NearFade.x, _NearFade.y, distance) : 1;

				float3 light = _BaseLight.rgb;
				for (int l = 0; l < 2; l++)
				{
					float range = max(_FogLightPositions[l].w, 0.001);
					float falloff = saturate(1 - length(_FogLightPositions[l].xyz - i.world) / range);
					light += _FogLightColors[l].rgb * falloff * falloff * _LightBoost;
				}

				fixed4 c = fixed4(i.color.rgb * light, saturate(i.color.a * puff * noise * near));
				UNITY_APPLY_FOG(i.fogCoord, c);
				return c;
			}
			ENDCG
		}
	}
}
