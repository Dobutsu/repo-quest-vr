Shader "REPO/Truck Halloween Sky" {
	{PROPERTIES}
	// RepoQuest: generated stand-in for AssetRipper's dummy shader (scripts/generate_shaders.py), written by hand
	// (unity-tools/ShaderOverrides). The Halloween menus' sky: TruckHalloweenLighting.SkyEnable puts a copy of the
	// material in a Skybox on the main camera, with _FogColor and the cloud noise set, and during lightning sets
	// _FlashColor (alpha is its strength), _FlashDarken and _FlashDirection. The generic stand-in left the sky the
	// camera's flat background colour. Look-alike rather than exact: a gradient from _HorizonColor through _SkyColor
	// to _SkyTopColor, drifting clouds darkened towards _CloudDarkColor, the moon (_MoonTex, _MoonSize across, towards
	// _MoonDirection, tinted by _MoonColor) with a _GlowColor halo, and below the horizon a tree line in _TreeColor
	// under a band of _MistColor fading into the fog.
	SubShader {
		Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
		Cull Off
		ZWrite Off

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			sampler2D _MoonTex;
			sampler2D _NoiseTex;
			float4 _MoonDirection;
			float _MoonSize;
			float4 _MoonColor;
			float4 _SkyTopColor;
			float4 _SkyColor;
			float4 _GlowColor;
			float4 _HorizonColor;
			float4 _CloudDarkColor;
			float4 _TreeColor;
			float4 _MistColor;
			float4 _FogColor;
			float4 _FlashColor;
			float _FlashDarken;
			float4 _FlashDirection;

			struct appdata
			{
				float4 vertex : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
				float3 dir : TEXCOORD0;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			v2f vert(appdata v)
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				o.pos = UnityObjectToClipPos(v.vertex);
				o.dir = v.vertex.xyz;
				return o;
			}

			// Cheap value noise along the horizon for the tree tops
			float hash(float x) { return frac(sin(x * 127.1) * 43758.5453); }
			float treeNoise(float x)
			{
				float i = floor(x), f = frac(x);
				return lerp(hash(i), hash(i + 1), f * f * (3 - 2 * f));
			}

			fixed4 frag(v2f i) : SV_Target
			{
				float3 dir = normalize(i.dir);
				float h = dir.y;

				// Sky gradient
				float3 col = lerp(_HorizonColor.rgb, _SkyColor.rgb, smoothstep(0, 0.22, h));
				col = lerp(col, _SkyTopColor.rgb, smoothstep(0.22, 0.85, h));

				// Clouds: the noise projected on a flat layer above, drifting slowly, thinning out at the horizon
				float2 cloudUV = dir.xz / (h + 0.25) * 0.35 + float2(_Time.x * 0.15, _Time.x * 0.05);
				float clouds = tex2D(_NoiseTex, cloudUV).r * 0.65 + tex2D(_NoiseTex, cloudUV * 2.7 + 0.3).r * 0.35;
				float cloudCover = smoothstep(0.42, 0.75, clouds) * smoothstep(0.0, 0.25, h);
				col = lerp(col, _CloudDarkColor.rgb, cloudCover * 0.8);

				// Moon and its glow, the glow also lighting the clouds' edges
				float3 moonDir = normalize(_MoonDirection.xyz);
				float facing = dot(dir, moonDir);
				float glow = pow(saturate(facing), 24) * 0.55 + pow(saturate(facing), 6) * 0.18;
				col += _GlowColor.rgb * glow * (1 - cloudCover * 0.6);

				float3 right = normalize(cross(float3(0, 1, 0), moonDir));
				float3 up = cross(moonDir, right);
				float2 moonUV = float2(dot(dir, right), dot(dir, up)) / max(_MoonSize, 0.001) + 0.5;
				if (facing > 0 && all(moonUV > 0) && all(moonUV < 1))
				{
					fixed4 moon = tex2D(_MoonTex, moonUV);
					float disc = moon.a * smoothstep(0.5, 0.47, length(moonUV - 0.5));
					col = lerp(col, moon.rgb * _MoonColor.rgb * 1.6, disc * (1 - cloudCover * 0.7));
				}

				// Lightning: the sky darkens, then flashes from the bolt's side
				col *= 1 - _FlashDarken;
				float flashSide = saturate(dot(dir, normalize(_FlashDirection.xyz + float3(0, 0.0001, 0))) * 0.5 + 0.5);
				col += _FlashColor.rgb * _FlashColor.a * (0.35 + 0.65 * flashSide * flashSide);

				// Tree line just above the horizon, mist over it, and the fog below
				float azimuth = atan2(dir.x, dir.z) * 9.55;
				float treeTop = 0.035 + 0.05 * treeNoise(azimuth) + 0.025 * treeNoise(azimuth * 3.1 + 7);
				float trees = 1 - smoothstep(treeTop - 0.004, treeTop + 0.004, h);
				col = lerp(col, _TreeColor.rgb, trees);
				col = lerp(col, _MistColor.rgb, (1 - smoothstep(0, 0.09, abs(h - 0.02))) * 0.45);
				if (dot(_FogColor.rgb, 1) > 0)
					col = lerp(col, _FogColor.rgb, 1 - smoothstep(-0.12, 0.03, h));

				return fixed4(col, 1);
			}
			ENDCG
		}
	}
}
