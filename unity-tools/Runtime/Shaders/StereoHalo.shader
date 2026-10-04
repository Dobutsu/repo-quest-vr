// A glow like Unity's Halo component, drawn for both eyes with single-pass instanced (multiview) stereo (see
// StereoHalo.cs). The object's scale is the halo's radius; the quad is built in each eye's view space, so it faces
// that eye. It is pulled towards the camera by its radius (and shrunk to keep its apparent size), so the lamp it
// belongs to does not cut it in half, while walls in front still hide it. Like Unity's halo it ignores fog: fogged,
// the headlights in the main menu's thick fog lost their glow entirely.
Shader "RepoQuest/Halo"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 center = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                float radius = length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20));

                float4 view = mul(UNITY_MATRIX_V, float4(center, 1));
                float distance = max(length(view.xyz), 1e-3);
                float pull = min(radius, distance * 0.5);
                float shrink = (distance - pull) / distance;
                view.xyz *= shrink;
                view.xy += v.vertex.xy * radius * shrink;

                o.pos = mul(UNITY_MATRIX_P, view);
                o.uv = v.vertex.xy;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                fixed4 color = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                float falloff = saturate(1 - length(i.uv));
                fixed4 glow = fixed4(color.rgb * color.a * falloff * falloff, 0);
                return glow;
            }
            ENDCG
        }
    }
}
