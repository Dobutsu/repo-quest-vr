// Application SpaceWarp's motion vectors for the built-in pipeline (unity-tools/Runtime/QuestSpaceWarp.cs): one
// full-screen triangle per eye slice of the runtime's motion vector image. Each pixel's NDC goes through
// _RepoQuestSWReproject (last frame's view-projection times the inverse of this frame's) to where that point was a
// frame ago; the motion is now - then, in NDC (as URP's XRMotionVector.shader writes it), y up (left-handed NDC).
// The point sits at _RepoQuestSWParams.z (the far plane's NDC z); with the translation taken out of the matrices
// (QuestSpaceWarp mode rotation) only the camera's turn is left.
//   _RepoQuestSWParams: x the image rows' y (+1 the clip y, -1 flipped), y the values' y sign, z far NDC z, w mode
//   (3: a test gradient, x = the pixel's NDC y / 2; 4: zero motion, for writing the depth alone)
// _RepoQuestSWZWrite / _RepoQuestSWDepth: write the clip z _RepoQuestSWDepth as depth (QuestSpaceWarp "depth near"),
// else the depth stays as cleared (far)
Shader "Hidden/RepoQuest/SpaceWarpMotion"
{
    Properties
    {
        _RepoQuestSWZWrite ("ZWrite", Float) = 0
        _RepoQuestSWDepth ("Clip z", Float) = 0.5
    }
    SubShader
    {
        Pass
        {
            ZTest Always
            ZWrite [_RepoQuestSWZWrite]
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            float4x4 _RepoQuestSWReproject;
            float4 _RepoQuestSWParams;
            float _RepoQuestSWDepth;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 ndc : TEXCOORD0;
                float4 prev : TEXCOORD1;
            };

            v2f vert(uint id : SV_VertexID)
            {
                float2 uv = float2((id << 1) & 2, id & 2);
                float2 clip = uv * 2.0 - 1.0;
                v2f o;
                o.pos = float4(clip, _RepoQuestSWDepth, 1.0);
                o.ndc = float4(clip.x, clip.y * _RepoQuestSWParams.x, _RepoQuestSWParams.z, 1.0);
                // Linear in the NDC, so interpolating the homogeneous result is exact
                o.prev = mul(_RepoQuestSWReproject, o.ndc);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                if (_RepoQuestSWParams.w > 3.5)
                    return float4(0.0, 0.0, 0.0, 0.0);
                if (_RepoQuestSWParams.w > 2.5)
                    return float4(i.ndc.y * 0.5, 0.0, 0.0, 0.0);
                float3 prev = i.prev.xyz / i.prev.w;
                float3 motion = i.ndc.xyz - prev;
                motion.y *= _RepoQuestSWParams.y;
                return float4(motion, 0.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
