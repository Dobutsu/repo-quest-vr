"""Standard and Standard (Specular setup) with per-pixel lamps, for the Quest VR project.

Most of the game's level materials use Unity's built-in Standard shader. These are copies of it, made from Unity's own
built-in shader source for the editor version (MIT; downloaded to work/downloads, not kept in the repository), with
one change: the base forward pass also lights the pixel with the lamps RepoXR's LampLighting hands to shaders
(unity-tools/Runtime/Shaders/RepoQuestLamps.cginc). Everything else - Unity's own lights, reflections, fog, blending,
the additive pass for flashlights, shadow casting - is Unity's code. scripts/retarget_standard.py points the
materials at them.
Usage: python scripts/make_lamp_shaders.py <unity project>
"""

import io
import re
import sys
import urllib.request
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent / "lib"))
from paths import WORK  # noqa: E402

UNITY_VERSION = "2022.3.62f3"
UNITY_REVISION = "96770f904ca7"
SOURCE = WORK / "downloads" / "builtin_shaders" / "src"

# name in the project -> (Unity source file, shader name, GUID of our copy)
SHADERS = {
    "RepoQuestStandard": ("Standard.shader", "Standard", "7c1d4e2f9a8b4c3d9e0f1a2b3c4d5e6f"),
    "RepoQuestStandardSpecular": ("StandardSpecular.shader", "Standard (Specular setup)", "8d2e5f3a0b9c4d4e8f1a2b3c4d5e6f70"),
}

BASE_PASS = re.compile(r'#pragma vertex vertBase\n(\s*)#pragma fragment fragBase\n\s*#include "UnityStandardCoreForward.cginc"')

LAMP_PASS = """#pragma vertex vertBaseLamps
{i}#pragma fragment fragBaseLamps
{i}// RepoQuest: the full Standard path always: the per-pixel lamps need the fragment's world position
{i}#define UNITY_STANDARD_SIMPLE 0
{i}#include "UnityStandardCore.cginc"
{i}#include "Assets/RepoQuest/Shaders/RepoQuestLamps.cginc"

{i}VertexOutputForwardBase vertBaseLamps (VertexInput v) {{ return vertForwardBase(v); }}

{i}// fragForwardBaseInternal (UnityStandardCore.cginc) plus the lamps
{i}half4 fragBaseLamps (VertexOutputForwardBase i) : SV_Target
{i}{{
{i}    FRAGMENT_SETUP(s)

{i}    UNITY_SETUP_INSTANCE_ID(i);
{i}    UNITY_APPLY_DITHER_CROSSFADE(i.pos.xy);
{i}    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

{i}    UnityLight mainLight = MainLight ();
{i}    UNITY_LIGHT_ATTENUATION(atten, i, s.posWorld);

{i}    half occlusion = Occlusion(i.tex.xy);
{i}    UnityGI gi = FragmentGI (s, occlusion, i.ambientOrLightmapUV, atten, mainLight);

{i}    half4 c = UNITY_BRDF_PBS (s.diffColor, s.specColor, s.oneMinusReflectivity, s.smoothness, s.normalWorld, -s.eyeVec, gi.light, gi.indirect);
{i}    c.rgb += RepoQuestLamps(s.posWorld, s.normalWorld, -s.eyeVec, s.diffColor, s.specColor, s.oneMinusReflectivity, s.smoothness);
{i}    c.rgb += Emission(i.tex.xy);

{i}    UNITY_EXTRACT_FOG_FROM_EYE_VEC(i);
{i}    UNITY_APPLY_FOG(_unity_fogCoord, c.rgb);
{i}    return OutputForward (c, s.alpha);
{i}}}"""

META = """fileFormatVersion: 2
guid: {guid}
ShaderImporter:
  externalObjects: {{}}
  defaultTextures: []
  nonModifiableTextures: []
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def ensure_source() -> None:
    if (SOURCE / "DefaultResourcesExtra" / "Standard.shader").exists():
        return
    # The Quest builder downloads (and checks) the archive into work/downloads first
    archive = WORK / "downloads" / f"builtin_shaders-{UNITY_VERSION}.zip"
    if archive.exists():
        data = archive.read_bytes()
    else:
        import ssl

        import certifi
        url = f"https://download.unity3d.com/download_unity/{UNITY_REVISION}/builtin_shaders-{UNITY_VERSION}.zip"
        data = urllib.request.urlopen(url, context=ssl.create_default_context(cafile=certifi.where())).read()
    outer = zipfile.ZipFile(io.BytesIO(data))
    inner = zipfile.ZipFile(io.BytesIO(outer.read("build/BuiltinShaders/builtin_shaders.zip")))
    SOURCE.mkdir(parents=True, exist_ok=True)
    inner.extractall(SOURCE)


def main() -> int:
    ensure_source()
    target = Path(sys.argv[1]) / "Assets" / "RepoQuest" / "Shaders"
    target.mkdir(parents=True, exist_ok=True)
    for name, (source, shader, guid) in SHADERS.items():
        text = (SOURCE / "DefaultResourcesExtra" / source).read_text()
        text, n = re.subn(rf'^Shader "{re.escape(shader)}"', f'Shader "RepoQuest/{shader}"', text, count=1, flags=re.M)
        if n != 1:
            raise SystemExit(f"{source}: shader name not found")
        text, n = BASE_PASS.subn(lambda m: LAMP_PASS.format(i=m.group(1)), text)
        if n == 0:
            raise SystemExit(f"{source}: base forward pass not found")
        header = (f"// RepoQuest: Unity's built-in {source} ({UNITY_VERSION}, MIT, Copyright (c) 2016 Unity Technologies)\n"
                  f"// with per-pixel lamps in the base forward pass (scripts/make_lamp_shaders.py)\n")
        (target / f"{name}.shader").write_text(header + text, newline="\n")
        (target / f"{name}.shader.meta").write_text(META.format(guid=guid), newline="\n")
        print(f"{name}: {n} base passes with lamps")
    return 0


if __name__ == "__main__":
    sys.exit(main())
