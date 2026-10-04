"""Replace AssetRipper's dummy shaders with working built-in-pipeline surface shaders.

The free AssetRipper cannot decompile shaders; its placeholders declare unity_ObjectToWorld/unity_MatrixVP as plain
globals, which Vulkan never fills, so everything using them is invisible on Quest. Each placeholder keeps its name,
GUID and Properties block (materials keep their values); the body becomes a surface shader whose inputs are picked by
property name (albedo/colour/normal/metallic/smoothness/emission/cutoff/tiling) from the declared properties and from
what the shader's materials actually store. Look-alike rather than exact: the originals are Amplify/Shader Graph shaders.
Fixed-function state (Blend, ColorMask, ZWrite, ZTest, Cull), tags and lit/unlit come from the originals' parsed form
(scripts/shader_states.py) when available.
Usage: python scripts/generate_shaders.py <unity project> [shader-states.json]
"""

import json
import re
import sys
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent / "lib"))
from paths import ROOT, WORK  # noqa: E402

OVERRIDES = ROOT / "unity-tools" / "ShaderOverrides"

ALBEDO_TEX = ["_AlbedoTexture", "_Albedo", "_BaseTexture", "_MainTex", "_Texture", "_Tex", "_Texture0", "_albedo",
              "_Wall", "_TextureSample0", "_TopTexture0"]
ALBEDO_COLOR = ["_AlbedoColor", "_Albedo_Color", "_BaseColor", "_Color", "_ColorTint", "_Tint_Wall", "_TintColor", "_Color1"]
NORMAL_TEX = ["_NormalTexture", "_NormalMap", "_Normal", "_BumpMap"]
NORMAL_STRENGTH = ["_NormalStrength", "_BumpScale"]
EMISSION_TEX = ["_EmissionTexture", "_EmissionMap", "_Emission"]
EMISSION_COLOR = ["_EmissionColor"]

BLEND = ["Zero", "One", "DstColor", "SrcColor", "OneMinusDstColor", "SrcAlpha", "OneMinusSrcColor", "DstAlpha",
         "OneMinusDstAlpha", "SrcAlphaSaturate", "OneMinusSrcAlpha"]
BLEND_OP = ["Add", "Sub", "RevSub", "Min", "Max"]
COMPARE = ["Always", "Never", "Less", "Equal", "LEqual", "Greater", "NotEqual", "GEqual", "Always"]
CULL = ["Off", "Front", "Back"]
FROM_MATERIALS = "// RepoQuest: stored by materials"
# Semiwork's own shaders scale the emission by the colour's alpha (in their compiled code): the Mentalist's body
# stores (1, 1, 1, 0) without an emission map, and players' tinted parts keep the material's alpha as the strength
# of their glow (PlayerMaterial.ColorSet only sets rgb). The Fresnel and Enemy Valuable shaders do not.
EMISSION_ALPHA = ("Hurtable/", "Semiwork/Standard", "Semiwork/Alpha Clip", "RotatingAlbedoShader")
EMISSION_SCALE = {"Hurtable/Hurtable Transparent": "0.294118"}  # a constant in that shader's compiled code
TAG_NAMES = {"queue": "Queue", "rendertype": "RenderType", "ignoreprojector": "IgnoreProjector",
             "previewtype": "PreviewType", "canusespriteatlas": "CanUseSpriteAtlas",
             "disablebatching": "DisableBatching", "forcenoshadowcasting": "ForceNoShadowCasting"}


def parse_properties(text: str) -> tuple[str, dict[str, str]]:
    """Returns the verbatim Properties block and {name: type} of its entries"""
    start = text.index("Properties")
    brace = text.index("{", start)
    depth, i = 0, brace
    while True:
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                break
        i += 1
    block = text[start:i + 1]
    if FROM_MATERIALS in block:  # added by an earlier run: not part of the original declaration
        block = block[:block.index(FROM_MATERIALS)].rstrip() + "\n\t}"
    props = {}
    for m in re.finditer(r'^\s*(?:\[[^\]]*\]\s*)*(_\w+)\s*\("[^"]*",\s*([A-Za-z0-9]+)', block, re.M):
        props[m.group(1)] = m.group(2)
    return block, props


def material_properties(project: Path) -> dict[str, dict[str, set]]:
    """shader guid -> {"tex": names, "float": names, "color": names, "alpha": {colour: alphas}} stored by its materials"""
    result: dict[str, dict] = defaultdict(lambda: {"tex": set(), "float": set(), "color": set(),
                                                   "alpha": defaultdict(set)})
    for mat in (project / "Assets").rglob("*.mat"):
        text = mat.read_text(errors="ignore")
        shader = re.search(r"m_Shader: \{fileID: \d+, guid: (\w+)", text)
        if not shader:
            continue
        sections = {"tex": "m_TexEnvs:", "float": "m_Floats:", "color": "m_Colors:"}
        for kind, header in sections.items():
            if header not in text:
                continue
            chunk = text.split(header, 1)[1]
            chunk = re.split(r"\n    m_\w+:", chunk, maxsplit=1)[0]
            for name in re.findall(r"^\s{4,6}(?:- )?(_\w+):", chunk, re.M):
                result[shader.group(1)][kind].add(name)
            if kind == "color":
                for name, alpha in re.findall(r"^\s{4,6}(?:- )?(_\w+): \{r: [^,]+, g: [^,]+, b: [^,]+, a: ([-\d.eE]+)\}",
                                              chunk, re.M):
                    result[shader.group(1)]["alpha"][name].add(float(alpha))
    return result


def pick(candidates: list[str], available: set[str], preferred: set[str] = frozenset()) -> str | None:
    """First candidate among the declared properties, else among everything available"""
    return next((c for c in candidates if c in preferred), None) or next((c for c in candidates if c in available), None)


def main_pass(state: dict | None) -> dict | None:
    """The pass that draws: skips GrabPasses (type 2) and forward-add/shadow/deferred passes"""
    if not state:
        return None
    passes = [p for p in state["passes"] if p["type"] == 0]
    return next((p for p in passes if p["tags"].get("lightmode", "forwardbase").lower() in ("forwardbase", "always")),
                passes[0] if passes else None)


def is_lit(state: dict) -> bool:
    return any(p["tags"].get("lightmode", "").lower() in ("forwardbase", "deferred") for p in state["passes"])


def state_tags(state: dict, pass_: dict) -> str:
    merged = {**state["tags"], **pass_["tags"]}
    tags = []
    for key, value in merged.items():
        if key in TAG_NAMES:
            value = value[:-2] if value.endswith("+0") else value
            tags.append(f'"{TAG_NAMES[key]}"="{value}"')
    return " ".join(tags)


def render_state(pass_: dict, props: set[str], blend: bool = True) -> list[str]:
    """ShaderLab state lines for a pass; property references only where the stand-in declares the property"""
    def fmt(v, names: list[str], default: int) -> str:
        if isinstance(v, str):
            name = v.strip("[]")
            return v if name in props or name.startswith("unity_") else names[default]
        return names[int(v)]

    lines = []
    src, dst, src_a, dst_a = pass_["blend"]
    if blend and [src, dst, src_a, dst_a] != [1.0, 0.0, 1.0, 0.0]:
        colour = f"{fmt(src, BLEND, 5)} {fmt(dst, BLEND, 10)}"
        alpha = f"{fmt(src_a, BLEND, 5)} {fmt(dst_a, BLEND, 10)}"
        lines.append(f"Blend {colour}" if colour == alpha else f"Blend {colour}, {alpha}")
        if pass_["blendOp"] != 0.0:
            lines.append(f"BlendOp {fmt(pass_['blendOp'], BLEND_OP, 0)}")
    mask = pass_["colorMask"]
    if isinstance(mask, str):
        if mask.strip("[]") in props:
            lines.append(f"ColorMask {mask}")
    elif mask != 15.0:
        m = int(mask)
        lines.append("ColorMask " + ("".join(c for c, bit in zip("RGBA", (8, 4, 2, 1)) if m & bit) or "0"))
    zwrite = pass_["zWrite"]
    if isinstance(zwrite, str):
        lines.append(f"ZWrite {zwrite}" if zwrite.strip("[]") in props else "ZWrite On")
    else:
        lines.append(f"ZWrite {'On' if zwrite else 'Off'}")
    lines.append(f"ZTest {fmt(pass_['zTest'], COMPARE, 4)}")
    lines.append(f"Cull {fmt(pass_['cull'], CULL, 2)}")
    return lines


def state_property_refs(pass_: dict | None, declared: dict[str, str], floats: set) -> list[str]:
    """Properties the original state refers to (`ZWrite [_ZWrite]`) that the placeholder does not declare but its
    materials store: they get declared, so the materials' values drive the state as before"""
    if not pass_:
        return []
    values = pass_["blend"] + [pass_[k] for k in ("blendOp", "colorMask", "zWrite", "zTest", "cull")]
    names = {v.strip("[]") for v in values if isinstance(v, str)}
    return sorted(n for n in names if n not in declared and n in floats and not n.startswith("unity_"))


def declare_properties(props_block: str, declared: dict[str, str], uniforms: list[str]) -> str:
    """Adds the uniforms taken from materials' stored values to the Properties block, so the values bind"""
    extra = []
    for u in uniforms:
        kind, name = u.rstrip(";").split()[:2]
        if name.endswith("_ST") or name in declared:
            continue
        if kind == "sampler2D":
            extra.append(f'{name} ("{name}", 2D) = "white" {{}}')
        elif kind == "fixed4":
            extra.append(f'{name} ("{name}", Color) = (1,1,1,1)')
        else:
            extra.append(f'{name} ("{name}", Float) = 0')
    if not extra:
        return props_block
    body = props_block.rstrip()[:-1].rstrip()
    return body + f"\n\t\t{FROM_MATERIALS}\n" + "".join(f"\t\t{e}\n" for e in extra) + "\t}"


def has_scale_offset(props_block: str, name: str) -> bool:
    """Whether a texture property is declared with a tiling/offset (i.e. without [NoScaleOffset])"""
    m = re.search(rf'^\s*((?:\[[^\]]*\]\s*)*){re.escape(name)}\s*\(', props_block, re.M)
    return bool(m) and "NoScaleOffset" not in m.group(1)


def colour_has_alpha(props_block: str, name: str, alphas: set | None = None, clipped: bool = False) -> bool:
    """
    Whether a colour property's alpha means anything. Amplify colours often only contribute rgb (opacity comes from
    the texture): multiplying by the whole colour made every pixel of the Catwalk Floor (a = 0) transparent and
    alpha-clipped away. Judged by what the materials store: an alpha-clipped material with a = 0 would be invisible
    in the original too, so there any 0 means rgb only; elsewhere alpha counts if some material sets it. Without
    stored values: Amplify's Vector colours default to alpha 0, Unity-style colours to 1.
    """
    if alphas:
        return all(a != 0 for a in alphas) if clipped else any(a != 0 for a in alphas)
    m = re.search(rf'{re.escape(name)}\s*\("[^"]*",\s*(?:Color|Vector)\)\s*=\s*\(([^)]*)\)', props_block)
    if not m:
        return True
    parts = [x.strip() for x in m.group(1).split(",")]
    return len(parts) < 4 or float(parts[3]) != 0


def build_unlit_shader(name: str, props_block: str, declared: dict[str, str], tex: set, floats: set, colors: set,
                       transparent: bool, no_fog: bool, is_ui: bool, state: dict | None, alphas: dict | None = None) -> str:
    """UI, particles and effects: a plain vertex/fragment shader, like Unity's UI/Default and legacy particle shaders"""
    albedo = pick(ALBEDO_TEX, tex, set(declared))
    color = pick(ALBEDO_COLOR, colors, set(declared))
    cutoff = "_Cutoff" if "_Cutoff" in floats else None

    uniforms, vert_extra = [], []
    if albedo:
        uniforms += [f"sampler2D {albedo};", f"float4 {albedo}_ST;"]
    if color:
        uniforms.append(f"fixed4 {color};")
    if cutoff:
        uniforms.append("float _Cutoff;")

    frag = [f"fixed4 c = tex2D({albedo}, TRANSFORM_TEX(i.uv, {albedo}))" if albedo else "fixed4 c = fixed4(1, 1, 1, 1)"]
    frag[0] += " * i.color;"
    if color:
        # Legacy particle shaders (_TintColor) double the tint so that grey (0.5) is neutral
        channels = "" if colour_has_alpha(props_block, color, (alphas or {}).get(color), bool(cutoff)) else ".rgb"
        frag.append(f"c{channels} *= {color}{channels}{' * 2' if color == '_TintColor' else ''};")
        if color == "_TintColor":
            frag.append("c.a = saturate(c.a);")
    if cutoff:
        if "_Mode" in declared:
            # Unity's Particles/Standard: the cutoff only applies in Cutout mode (1), not to Fade/Transparent particles
            uniforms.append("float _Mode;")
            frag.append("if (_Mode == 1) clip(c.a - _Cutoff);")
        else:
            frag.append("clip(c.a - _Cutoff);")
    if "_DistortionEnabled" in declared and "_DistortionBlend" in declared:
        # Distortion replaces the particle by the refracted screen behind it (a GrabPass, too costly per eye on the
        # Quest); Blend 1 shows none of the particle's own colour. Without the refraction: fade the colour out by
        # the same amount, and skip pure-distortion particles in the vertex stage (moved outside the clip volume):
        # invisible but still shaded, the cart's large hover haze cost noticeable frame rate
        uniforms += ["float _DistortionEnabled;", "float _DistortionBlend;"]
        frag.append("c.a *= 1 - _DistortionEnabled * _DistortionBlend;")
        vert_extra.append("if (_DistortionEnabled * _DistortionBlend > 0.99) o.pos = float4(2, 2, 2, 1);")
    if not no_fog:
        frag.append("UNITY_APPLY_FOG(i.fogCoord, c);")
    frag.append("return c;")

    pass_ = main_pass(state)
    extra_floats = state_property_refs(pass_, declared, floats)
    if pass_:
        state_lines = ["Lighting Off"] + render_state(pass_, set(declared) | set(extra_floats))
        # A pass drawing a GrabPass texture opaquely (magnifier) has no screen copy here: blend its texture instead
        if any(p["type"] == 2 for p in state["passes"]) and pass_["blend"] == [1.0, 0.0, 1.0, 0.0]:
            state_lines.insert(1, "Blend SrcAlpha OneMinusSrcAlpha")
        tags = state_tags(state, pass_)
    else:
        state_lines = ["Cull Off", "Lighting Off"]
        if transparent:
            state_lines.append("ZWrite Off")
            if "_SrcBlend" in declared and "_DstBlend" in declared:
                state_lines.append("Blend [_SrcBlend] [_DstBlend]")
            else:
                state_lines.append("Blend SrcAlpha OneMinusSrcAlpha")
        if is_ui:
            state_lines.append("ZTest [unity_GUIZTestMode]")
        if "_ColorMask" in declared:
            state_lines.append("ColorMask [_ColorMask]")
        tags = '"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane"' \
            if transparent else '"RenderType"="Opaque"'
    stencil = ""
    if "_Stencil" in declared:
        stencil = ("Stencil\n\t\t{\n\t\t\tRef [_Stencil]\n\t\t\tComp [_StencilComp]\n\t\t\tPass [_StencilOp]\n"
                   "\t\t\tReadMask [_StencilReadMask]\n\t\t\tWriteMask [_StencilWriteMask]\n\t\t}")
    props_block = declare_properties(props_block, declared, uniforms + [f"float {n};" for n in extra_floats])
    nl3 = "\n" + "\t" * 3
    return f"""Shader "{name}" {{
	{props_block}
	// RepoQuest: generated stand-in for AssetRipper's dummy shader (scripts/generate_shaders.py)
	SubShader {{
		Tags {{ {tags} }}
		{stencil}
		{nl3[:-1].join(state_lines)}

		Pass {{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			{'' if no_fog else '#pragma multi_compile_fog'}
			#include "UnityCG.cginc"

			{nl3.join(uniforms)}

			struct appdata
			{{
				float4 vertex : POSITION;
				float4 color : COLOR;
				float2 uv : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			}};

			struct v2f
			{{
				float4 pos : SV_POSITION;
				fixed4 color : COLOR;
				float2 uv : TEXCOORD0;
				UNITY_FOG_COORDS(1)
				UNITY_VERTEX_OUTPUT_STEREO
			}};

			v2f vert(appdata v)
			{{
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				o.pos = UnityObjectToClipPos(v.vertex);
				{nl3.join(vert_extra)}
				o.uv = v.uv;
				o.color = v.color;
				UNITY_TRANSFER_FOG(o, o.pos);
				return o;
			}}

			fixed4 frag(v2f i) : SV_Target
			{{
				{(nl3 + chr(9)).join(frag)}
			}}
			ENDCG
		}}
	}}
}}
"""


def build_shader(name: str, props_block: str, declared: dict[str, str], stored: dict[str, set],
                 state: dict | None) -> str:
    # Materials can carry leftover properties from shaders they used before; trust them only when the placeholder
    # declares next to nothing (e.g. the Ciconia shaders keep their inputs hidden)
    use_stored = len(declared) <= 4
    tex = {n for n, t in declared.items() if t == "2D"} | (stored["tex"] if use_stored else set())
    floats = {n for n, t in declared.items() if t in ("Float", "Range", "Int")} | (stored["float"] if use_stored else set())
    colors = {n for n, t in declared.items() if t in ("Color", "Vector")} | (stored["color"] if use_stored else set())

    lname = name.lower()
    own = set(declared)
    albedo = pick(ALBEDO_TEX, tex, own)
    color = pick(ALBEDO_COLOR, colors, own)
    normal = pick(NORMAL_TEX, tex, own)
    normal_strength = pick(NORMAL_STRENGTH, floats, own)
    emission_tex = pick(EMISSION_TEX, tex, own)
    emission_color = pick(EMISSION_COLOR, colors, own)
    metallic = "_Metallic" if "_Metallic" in floats else None
    smoothness = pick(["_Smoothness", "_Glossiness"], floats, own)
    cutoff = "_Cutoff" if "_Cutoff" in floats and ("clip" in lname or "cutout" in lname or "decal" in lname) else None
    overlay = "_ColorOverlay" if "_ColorOverlay" in colors and "_ColorOverlayAmount" in floats else None
    fresnel = "_FresnelColor" if "_FresnelColor" in colors and "_FresnelPower" in floats else None
    tiling_xy = "_TilingX" in floats and "_TilingY" in floats
    world_floor = "world space" in lname
    scroll = "_Scroll" in floats or "_Scroll" in colors

    unlit_ui = lname.startswith(("ui", "emoji")) or "overlay" in lname
    unlit = unlit_ui or "particles unlit" in lname or lname.startswith("w/") or "projectile" in lname \
        or "magnifying" in lname or "fallbackerror" in lname
    transparent = any(k in lname for k in ("fade", "transparent", "overlay", "particles", "area", "window",
                                           "intersection", "water", "magnifying")) or unlit_ui
    double_sided = "double sided" in lname or "decal" in lname or lname.startswith("w/")
    no_fog = "no fog" in lname or unlit_ui
    cull = "Cull [_Cull]" if "_Cull" in declared else ("Cull Off" if double_sided else "")

    pass_ = main_pass(state)
    extra_floats = state_property_refs(pass_, declared, floats)
    state_lines = [cull, "ZWrite Off" if transparent else ""]
    if pass_:
        # What the original does: lit (forward passes) or not, queue, culling and depth state
        unlit = not is_lit(state)
        queue = {**state["tags"], **pass_["tags"]}.get("queue", "")
        transparent = queue.lower().startswith("transparent")
        unlit_ui = unlit and unlit_ui
        state_lines = [s for s in render_state(pass_, own | set(extra_floats), blend=False)
                       if not s.startswith("ColorMask")]
    # Hurtable (enemies, players, cosmetics) gets its rim, glow and hurt flash as in the compiled original
    hurtable = bool(fresnel and overlay and "_FresnelAmount" in floats)
    two_sided = "Cull Off" in state_lines
    backfaces = "_backfaces" in floats and two_sided

    if unlit:
        return build_unlit_shader(name, props_block, declared, tex, floats, colors, transparent, no_fog, unlit_ui,
                                  state, alphas=stored["alpha"])

    # Effects that glow in their own colour: the original is tagged emissive but declares no emission input (nor
    # normal/metallic/smoothness), so its colour can only have gone to emission. Lit instead, the extraction point's
    # emoji screen was washed out by the lamps next to it (eyes and mouth gone, tears white).
    tags_all = {k: v for p in (state or {}).get("passes", []) for k, v in p["tags"].items()}
    self_lit = tags_all.get("isemissive") == "true" and not any(
        n in declared for n in EMISSION_TEX + EMISSION_COLOR + NORMAL_TEX + ["_Metallic", "_Smoothness", "_Glossiness"])

    uniforms, body = [], []

    def declare(kind: str, n: str | None):
        if n and not any(u.endswith(f" {n};") for u in uniforms):
            uniforms.append(f"{kind} {n};")

    # UVs
    if world_floor:
        # A Vector (0.25, 0.25) in the original; as a float uniform it was not set at all
        if declared.get("_Tiling") in ("Vector", "Color"):
            declare("float4", "_Tiling")
            uv = "IN.worldPos.xz * _Tiling.xy"
        else:
            declare("float", "_Tiling")
            uv = "IN.worldPos.xz * _Tiling"
    elif tiling_xy:
        for n in ("_TilingX", "_TilingY", "_OffsetX", "_OffsetY"):
            declare("float", n)
        uv = "IN.texUV * float2(_TilingX, _TilingY) + float2(_OffsetX, _OffsetY)"
    elif albedo and has_scale_offset(props_block, albedo):
        # Amplify samples with the texture's own tiling/offset unless the property is [NoScaleOffset]: the Catwalk
        # Floor tiles its grating 7x, and without that it showed a sparse frame with the level below visible
        declare("float4", f"{albedo}_ST")
        uv = f"IN.texUV * {albedo}_ST.xy + {albedo}_ST.zw"
    else:
        uv = "IN.texUV"
    if scroll:
        declare("float", "_Scroll")
        uv = f"({uv}) + _Time.y * _Scroll * 0.05"

    body.append(f"float2 uv = {uv};")
    if albedo:
        declare("sampler2D", albedo)
        body.append(f"fixed4 c = tex2D({albedo}, uv);")
    else:
        body.append("fixed4 c = fixed4(1, 1, 1, 1);")
    if color:
        declare("fixed4", color)
        channels = "" if colour_has_alpha(props_block, color, stored["alpha"].get(color), bool(cutoff)) else ".rgb"
        body.append(f"c{channels} *= {color}{channels};")
    if "particles" in lname or unlit_ui:
        body.append("c *= IN.color;")
    if overlay:
        declare("fixed4", "_ColorOverlay")
        declare("float", "_ColorOverlayAmount")
        if unlit or self_lit:
            body.append("c.rgb = lerp(c.rgb, _ColorOverlay.rgb, saturate(_ColorOverlayAmount));")
    if cutoff:
        declare("float", "_Cutoff")
        body.append("clip(c.a - _Cutoff);")

    if unlit or self_lit:
        body.append("o.Albedo = 0;")
        body.append("o.Emission = c.rgb;")
    else:
        body.append("o.Albedo = c.rgb;")
        if normal:
            declare("sampler2D", normal)
            if normal_strength:
                declare("float", normal_strength)
                body.append(f"o.Normal = UnpackScaleNormal(tex2D({normal}, uv), {normal_strength});")
            else:
                body.append(f"o.Normal = UnpackNormal(tex2D({normal}, uv));")
        # The Semiwork shaders (Hurtable, Standard, Fresnel) scale greyscale maps by the slider: enemies set Metallic 1
        # and mark the metal parts in the map. Without the maps the Clown, Trudge and others were metal all over, so
        # black apart from reflections
        if metallic:
            declare("float", "_Metallic")
            if "_MetallicTexture" in tex:
                declare("sampler2D", "_MetallicTexture")
                body.append("o.Metallic = _Metallic * tex2D(_MetallicTexture, uv).r;")
            else:
                body.append("o.Metallic = _Metallic;")
        if smoothness:
            declare("float", smoothness)
            if "_SmoothnessTexture" in tex:
                declare("sampler2D", "_SmoothnessTexture")
                body.append(f"o.Smoothness = {smoothness} * tex2D(_SmoothnessTexture, uv).r;")
            else:
                body.append(f"o.Smoothness = {smoothness};")
        emission = []
        if emission_tex:
            declare("sampler2D", emission_tex)
            emission.append(f"tex2D({emission_tex}, uv).rgb")
        if emission_color:
            declare("fixed4", emission_color)
            emission.append(f"{emission_color}.rgb")
        if emission:
            if emission_color and name.startswith(EMISSION_ALPHA):
                emission.append(f"{emission_color}.a")
            if name in EMISSION_SCALE:
                emission.append(EMISSION_SCALE[name])
            body.append(f"o.Emission = {' * '.join(emission)};")
        if fresnel:
            for n in ("_FresnelBias", "_FresnelScale", "_FresnelPower"):
                declare("float", n)
            declare("fixed4", "_FresnelColor")
            if hurtable:
                # As compiled: the rim follows the mesh normal (not the normal map), is not clamped (the Mentalist's
                # Scale 21 makes a strong blue edge), adds to the albedo and, by Emission, to the glow; the
                # double-sided variants keep it to front faces
                view = "normalize(UnityWorldSpaceViewDir(IN.worldPos))"
                mesh_normal = "WorldNormalVector(IN, float3(0, 0, 1))" if normal else "IN.worldNormal"
                body.append(f"float rim = _FresnelBias + _FresnelScale * "
                            f"pow(max(1 - dot({mesh_normal}, {view}), 1e-4), _FresnelPower);")
                if two_sided:
                    body.append("rim *= IN.facing > 0;")
                declare("float", "_FresnelAmount")
                body.append("float3 rimColour = rim * _FresnelColor.rgb * _FresnelAmount;")
                body.append("o.Albedo += rimColour;")
                if "_FresnelEmission" in floats:
                    declare("float", "_FresnelEmission")
                    body.append("o.Emission += rimColour * _FresnelEmission;")
            else:
                body.append("float rim = saturate(_FresnelBias + _FresnelScale * "
                            "pow(1 - saturate(dot(normalize(IN.viewDir), o.Normal)), _FresnelPower));")
                # The rim is dosed by the original's own controls: Enemy Valuable only has Emission; the Fresnel
                # shaders tint by the colour's alpha and glow with a separate HDR colour. Emitting the full rim colour
                # instead gave players a bright white halo.
                if "_FresnelEmission" in floats:
                    declare("float", "_FresnelEmission")
                    body.append("o.Emission += _FresnelColor.rgb * rim * _FresnelEmission;")
                elif "_FresnelEmissionColor" in colors:
                    declare("fixed4", "_FresnelEmissionColor")
                    body.append("o.Albedo = lerp(o.Albedo, _FresnelColor.rgb, rim * _FresnelColor.a);")
                    body.append("o.Emission += _FresnelEmissionColor.rgb * rim;")
                else:
                    body.append("o.Emission += _FresnelColor.rgb * rim;")
        if overlay:
            # The hurt flash (EnemyHealth) covers the glow too
            body.append("o.Albedo = lerp(o.Albedo, _ColorOverlay.rgb, saturate(_ColorOverlayAmount));")
            body.append("o.Emission = lerp(o.Emission, _ColorOverlay.rgb, saturate(_ColorOverlayAmount));")
    # RepoXR's per-pixel lamps (unity-tools/Runtime/Shaders/RepoQuestLamps.cginc): added as emission, which surface
    # shaders only output in the base forward pass; the lamps no longer light anything through Unity
    lamps = not unlit and not self_lit
    if lamps:
        world_normal = "WorldNormalVector(IN, o.Normal)" if normal else "normalize(IN.worldNormal)"
        body.append("#ifdef UNITY_PASS_FORWARDBASE")
        body.append(f"o.Emission += RepoQuestLampsMetallic(IN.worldPos, {world_normal}, o.Albedo, o.Metallic, o.Smoothness);")
        body.append("#endif")
    if backfaces:
        # Hurtable Fade: back faces at the material's _backfaces opacity (two cosmetic overlays hide them)
        declare("float", "_backfaces")
        body.append("o.Alpha = IN.facing > 0 ? c.a : c.a * _backfaces;")
    else:
        body.append("o.Alpha = c.a;")

    tags = '"Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True"' if transparent else \
        ('"Queue"="AlphaTest" "RenderType"="TransparentCutout"' if cutoff else '"RenderType"="Opaque"')
    if pass_ and not cutoff:
        tags = state_tags(state, pass_)
    props_block = declare_properties(props_block, declared, uniforms + [f"float {n};" for n in extra_floats])
    lighting = "Standard" if not unlit else "Lambert"
    options = ["fullforwardshadows"] if not unlit and not transparent else []
    if transparent:
        options.append("alpha:fade")
    if cutoff:
        options.append("addshadow")
    if no_fog:
        options.append("nofog")
    if unlit:
        options.extend(["noshadow", "nolightmap", "noambient"])
    options.append("vertex:vert")

    inputs = ["float2 texUV;", "float3 worldPos;", "float3 viewDir;", "float4 color : COLOR;"]
    if lamps or hurtable:
        inputs += ["float3 worldNormal;", "INTERNAL_DATA"] if normal else ["float3 worldNormal;"]
    if (hurtable and two_sided) or backfaces:
        inputs.append("float facing : VFACE;")
    include = '#include "Assets/RepoQuest/Shaders/RepoQuestLamps.cginc"' if lamps else ""
    render_lines = "\n\t\t".join(s for s in state_lines if s)

    return f"""Shader "{name}" {{
	{props_block}
	// RepoQuest: generated stand-in for AssetRipper's dummy shader (scripts/generate_shaders.py)
	SubShader {{
		Tags {{ {tags} }}
		LOD 200
		{render_lines}

		CGPROGRAM
		#pragma surface surf {lighting} {' '.join(options)}
		#pragma target 3.0
		#pragma multi_compile_instancing
		{include}

		{chr(10).join(chr(9) * 2 + u for u in uniforms).strip()}

		struct Input
		{{
			{(chr(10) + chr(9) * 3).join(inputs)}
		}};

		void vert(inout appdata_full v, out Input o)
		{{
			UNITY_INITIALIZE_OUTPUT(Input, o);
			o.texUV = v.texcoord.xy;
		}}

		void surf(Input IN, inout SurfaceOutput{'Standard' if lighting == 'Standard' else ''} o)
		{{
			{(chr(10) + chr(9) * 3).join(body)}
		}}
		ENDCG
	}}
	Fallback "{'Legacy Shaders/Transparent/Diffuse' if transparent else 'Diffuse'}"
}}
"""


def main() -> int:
    project = Path(sys.argv[1])
    states_path = Path(sys.argv[2]) if len(sys.argv) > 2 else WORK / "shader-states.json"
    states = json.loads(states_path.read_text()) if states_path.exists() else {}
    if not states:
        print(f"{states_path} missing: render state guessed from shader names (run scripts/shader_states.py)")
    stored = material_properties(project)
    count = 0
    for path in sorted((project / "Assets" / "Shader").glob("*.shader")):
        text = path.read_text(errors="ignore")
        if "DummyShaderTextExporter" not in text and "RepoQuest: generated" not in text:
            continue
        name = re.search(r'Shader "([^"]+)"', text).group(1)
        guid = re.search(r"guid: (\w+)", Path(str(path) + ".meta").read_text()).group(1)
        block, declared = parse_properties(text)
        # Effects the generic stand-in cannot approximate get a hand-written body (keeping the original Properties)
        override = OVERRIDES / (name.replace("/", "_") + ".shader")
        if override.exists():
            path.write_text(override.read_text().replace("{PROPERTIES}", block), newline="\n")
        else:
            path.write_text(build_shader(name, block, declared, stored[guid], states.get(name)), newline="\n")
        count += 1
    print(f"generated {count} shaders")
    return 0


if __name__ == "__main__":
    sys.exit(main())
