"""Make RepoXR's own shaders work with single-pass instanced (multiview) stereo, in the RepoXR asset project copy.

RepoXR renders Multi-Pass on PC, so two of its shaders never needed Unity's stereo macros. With both eyes drawn by one
instanced draw call, a shader without them only lands in one eye, and a GrabPass texture is an array of both eyes:
  - Dashed Outline: instance id in, stereo eye out of the vertex shader
  - Focus Sphere: the same, plus reading its GrabPass texture per eye
Idempotent: a patched file carries the marker and is left alone.
Usage: python scripts/patch_repoxr_shaders.py <RepoXRAssets project>
"""

import sys
from pathlib import Path

MARKER = "// RepoQuest: single-pass instanced stereo"

STRUCT_EDITS = [
    ("""                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };""",
     """                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };"""),
    ("""                float3 objectScale : TEXCOORD2;
            };""",
     """                float3 objectScale : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };"""),
    ("""                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };""",
     """                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };"""),
    ("""                float3 positionOS : TEXCOORD3;
            };""",
     """                float3 positionOS : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };"""),
]

VERT_SETUP = ("""                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);""",
              f"""                v2f o;
                {MARKER}
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);""")

GRAB_EDITS = [
    ("            sampler2D _GrabTexture;", "            UNITY_DECLARE_SCREENSPACE_TEXTURE(_GrabTexture);"),
    ("""            fixed4 frag(v2f i) : SV_Target
            {""",
     """            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);"""),
    ("tex2D(_GrabTexture, grabUV)", "UNITY_SAMPLE_SCREENSPACE_TEXTURE(_GrabTexture, grabUV)"),
]


def patch(path: Path, edits: list[tuple[str, str]]) -> None:
    text = path.read_text()
    if MARKER in text:
        print(f"already patched: {path.name}")
        return
    for old, new in edits:
        if old not in text:
            continue
        if text.count(old) != 1:
            raise SystemExit(f"{path.name}: expected one '{old.strip()[:40]}'")
        text = text.replace(old, new)
    if VERT_SETUP[0] not in text:
        raise SystemExit(f"{path.name}: vertex function not found")
    text = text.replace(VERT_SETUP[0], VERT_SETUP[1])
    path.write_text(text, newline="\n")
    print(f"patched: {path.name}")


def main() -> int:
    shaders = Path(sys.argv[1]) / "Assets" / "Shaders"
    patch(shaders / "Dashed Outline.shader", STRUCT_EDITS)
    patch(shaders / "Focus Sphere.shader", STRUCT_EDITS + GRAB_EDITS)
    return 0


if __name__ == "__main__":
    sys.exit(main())
