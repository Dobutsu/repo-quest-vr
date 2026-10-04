"""Extract the fixed-function render state of the game's shaders (blend, colour mask, depth, culling, tags).

The compiled shaders keep a parsed form next to the platform bytecode: per pass the blend factors, ColorMask, ZWrite,
ZTest and Cull, each either a constant or the name of a property (`Blend [_SrcBlend] ...`), plus subshader/pass tags.
generate_shaders.py uses this to give its stand-ins the originals' exact state instead of guessing it from names.
Output (JSON): {shader name: {"tags": {...}, "passes": [{"tags": {...}, "blend": [src, dst, srcA, dstA],
  "blendOp": x, "colorMask": x, "zWrite": x, "zTest": x, "cull": x}]}}; x = number or "[_Property]".
Usage: python scripts/shader_states.py <REPO_Data> --out work/shader-states.json
"""

import argparse
import json
import sys

import UnityPy


def value(state: dict):
    name = state.get("name", "<noninit>")
    return f"[{name}]" if name and name != "<noninit>" else state["val"]


def tags(block: dict | None) -> dict:
    return {k.lower(): v for k, v in (block or {}).get("tags", [])}


def pass_state(p: dict) -> dict:
    st = p["m_State"]
    rt = st.get("rtBlend0") or st["rtBlend"][0]
    return {
        "type": p.get("m_Type"),
        "tags": tags(st.get("m_Tags")),
        "blend": [value(rt[k]) for k in ("srcBlend", "destBlend", "srcBlendAlpha", "destBlendAlpha")],
        "blendOp": value(rt["blendOp"]),
        "colorMask": value(rt["colMask"]),
        "zWrite": value(st["zWrite"]),
        "zTest": value(st["zTest"]),
        "cull": value(st["culling"]),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("data")
    parser.add_argument("--out", required=True)
    args = parser.parse_args()

    result = {}
    env = UnityPy.load(args.data)
    for obj in env.objects:
        if obj.type.name != "Shader":
            continue
        form = obj.read_typetree().get("m_ParsedForm") or {}
        name = form.get("m_Name")
        if not name or not form.get("m_SubShaders"):
            continue
        sub = form["m_SubShaders"][0]
        result[name] = {"tags": tags(sub.get("m_Tags")), "passes": [pass_state(p) for p in sub["m_Passes"]]}

    with open(args.out, "w", newline="\n") as f:
        json.dump(result, f, indent=1, sort_keys=True)
    print(f"{len(result)} shader states -> {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
