// Lamps lit per pixel in the base forward pass (RepoXR's Rendering.LampLighting).
//
// Built-in forward rendering draws every per-pixel light as one more pass over each object it touches, decides per
// object which lights get a pass (Pixel Light Count) and approximates the rest per vertex or in spherical harmonics:
// costly, and adjacent objects visibly disagree about the same lamp. Instead the lamps nearest the camera are handed
// to every shader as arrays (LampLighting.cs keeps Unity from lighting them itself) and evaluated here, per pixel,
// with the same BRDF Unity uses for its own lights. Lamps cast no shadows this way; flashlights stay Unity lights.
#ifndef REPOQUEST_LAMPS_INCLUDED
#define REPOQUEST_LAMPS_INCLUDED

#include "UnityPBSLighting.cginc"

#define REPOQUEST_MAX_LAMPS 16

float4 _RQLampPosition[REPOQUEST_MAX_LAMPS];  // xyz: position, w: 1 / range²
half4 _RQLampColor[REPOQUEST_MAX_LAMPS];      // rgb: colour × intensity
half4 _RQLampSpot[REPOQUEST_MAX_LAMPS];       // xyz: spot direction, w: 1 / tan(half spot angle); 0 for point lights
int _RQLampCount;

// Unity's point light falloff over range (its attenuation texture): 1 / (1 + 25 x) fading to 0 over the last fifth
half RepoQuestLampFalloff(float x)
{
    return saturate((1 - x) * 5) / (1 + 25 * x);
}

// Unity's default spot cookie: soft towards the edge of the cone; r is the distance from the axis, 1 at the edge
half RepoQuestSpotFalloff(half3 lampToPoint, half4 spot)
{
    half cosAngle = dot(lampToPoint, spot.xyz);
    half tanAngle = sqrt(saturate(1 - cosAngle * cosAngle)) / max(cosAngle, 1e-4);
    half r = tanAngle * spot.w;
    return cosAngle > 0 ? saturate(1 - r * r) : 0;
}

half3 RepoQuestLamps(float3 posWorld, half3 normalWorld, half3 viewDir, half3 diffColor, half3 specColor,
                     half oneMinusReflectivity, half smoothness)
{
    half3 sum = 0;
    UnityIndirect noIndirect;
    noIndirect.diffuse = 0;
    noIndirect.specular = 0;

    for (int k = 0; k < _RQLampCount; k++)
    {
        float3 toLamp = _RQLampPosition[k].xyz - posWorld;
        float x = dot(toLamp, toLamp) * _RQLampPosition[k].w;
        UNITY_BRANCH
        if (x < 1)
        {
            half3 dir = normalize(toLamp);
            half atten = RepoQuestLampFalloff(x);
            half4 spot = _RQLampSpot[k];
            if (spot.w > 0)
                atten *= RepoQuestSpotFalloff(-dir, spot);

            UnityLight lamp;
            UNITY_INITIALIZE_OUTPUT(UnityLight, lamp);
            lamp.color = _RQLampColor[k].rgb * atten;
            lamp.dir = dir;
            sum += UNITY_BRDF_PBS(diffColor, specColor, oneMinusReflectivity, smoothness, normalWorld, viewDir,
                                  lamp, noIndirect).rgb;
        }
    }
    return sum;
}

// For shaders with a metallic setup (surface shaders' SurfaceOutputStandard)
half3 RepoQuestLampsMetallic(float3 posWorld, half3 normalWorld, half3 albedo, half metallic, half smoothness)
{
    half3 specColor;
    half oneMinusReflectivity;
    half3 diffColor = DiffuseAndSpecularFromMetallic(albedo, metallic, specColor, oneMinusReflectivity);
    half3 viewDir = normalize(UnityWorldSpaceViewDir(posWorld));
    return RepoQuestLamps(posWorld, normalWorld, viewDir, diffColor, specColor, oneMinusReflectivity, smoothness);
}

#endif
