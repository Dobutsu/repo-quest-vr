"""Print the game's compiled (DX11) code of a shader: the ground truth for a hand-written or generated stand-in.

AssetRipper's free export has no shader code, but the game ships DXBC bytecode for every variant. This prints each
pass with its tags, the material properties behind the $Globals registers, and the disassembled fragment programs
(fewest keywords first): enough to read formulas such as Hurtable's rim and glow or W/ExtrationArea's depth-based
outline. A minimal SM4/SM5 disassembler: no declarations, operands as registers with swizzles.
Usage: python scripts/shader_disasm.py "<shader name>" [--pass N] [--programs N] [--vertex] [--data <REPO_Data>]
"""

import argparse
import struct
import sys
from pathlib import Path

import UnityPy
import lz4.block

ROOT = Path(__file__).resolve().parent.parent
OPS = """add and break breakc call callc case continue continuec cut default deriv_rtx deriv_rty discard div dp2 dp3 dp4
else emit emitthencut endif endloop endswitch eq exp frc ftoi ftou ge iadd if ieq ige ilt imad imax imin imul ine ineg
ishl ishr itof label ld ld_ms log loop lt mad min max customdata mov movc mul ne nop not or resinfo ret retc round_ne
round_ni round_pi round_z rsq sample sample_c sample_c_lz sample_l sample_d sample_b sqrt switch sincos udiv ult uge
umul umad umax umin ushr utof xor dcl_resource dcl_constantbuffer dcl_sampler dcl_index_range dcl_gs_output_topology
dcl_gs_input_primitive dcl_max_output_vertex_count dcl_input dcl_input_sgv dcl_input_siv dcl_input_ps dcl_input_ps_sgv
dcl_input_ps_siv dcl_output dcl_output_sgv dcl_output_siv dcl_temps dcl_indexable_temp dcl_global_flags reserved0 lod
gather4 sample_pos sample_info reserved1 hs_decls hs_control_point_phase hs_fork_phase hs_join_phase emit_stream
cut_stream emitthencut_stream interface_call bufinfo deriv_rtx_coarse deriv_rtx_fine deriv_rty_coarse deriv_rty_fine
gather4_c gather4_po gather4_po_c rcp f32tof16 f16tof32 uaddc usubb countbits firstbit_hi firstbit_lo firstbit_shi
ubfe ibfe bfi bfrev swapc""".split()
REGISTERS = {0: "r", 1: "v", 2: "o", 3: "x", 6: "s", 7: "t", 8: "cb", 9: "icb", 12: "oDepth", 13: "null"}
CONDITIONAL = ("if", "breakc", "discard", "retc", "continuec", "callc")


def chunks(data: bytes) -> dict[str, bytes]:
    count = struct.unpack_from("<I", data, 28)[0]
    result = {}
    for k in range(count):
        off = struct.unpack_from("<I", data, 32 + 4 * k)[0]
        size = struct.unpack_from("<I", data, off + 4)[0]
        result[data[off:off + 4].decode()] = data[off + 8:off + 8 + size]
    return result


def signature(buf: bytes) -> str:
    entries = []
    for k in range(struct.unpack_from("<I", buf, 0)[0]):
        name_off, index, _, _, reg, mask = struct.unpack_from("<5IB", buf, 8 + 24 * k)
        name = buf[name_off:buf.index(b"\0", name_off)].decode()
        entries.append(f"{name}{index}=>{reg}/{mask:x}")
    return ", ".join(entries)


def operand(tok: tuple, pos: int) -> tuple[str, int]:
    t = tok[pos]
    pos += 1
    modifier = ""
    if t >> 31:
        ext = tok[pos]
        pos += 1
        if ext & 0x3F == 1:
            modifier = {1: "-", 2: "abs", 3: "-abs"}.get((ext >> 6) & 0xFF, "")
    kind = (t >> 12) & 0xFF
    if kind == 4:  # immediate
        n = 1 if t & 3 == 1 else 4
        values = [struct.unpack("<f", struct.pack("<I", v))[0] for v in tok[pos:pos + n]]
        raw = tok[pos:pos + n]
        pos += n
        return "l(" + ", ".join(f"{f:g}" if 1e-6 < abs(f) < 1e7 or r == 0 else f"0x{r:x}"
                                for f, r in zip(values, raw)) + ")", pos
    index = []
    for d in range((t >> 20) & 3):
        rep = (t >> (22 + 3 * d)) & 7
        if rep == 0:
            index.append(str(tok[pos]))
            pos += 1
        elif rep in (2, 3):
            imm = ""
            if rep == 3:
                imm = f" + {tok[pos]}"
                pos += 1
            sub, pos = operand(tok, pos)
            index.append(sub + imm)
        else:
            raise ValueError(f"index representation {rep}")
    name = REGISTERS.get(kind, f"T{kind}")
    if len(index) == 2:
        text = f"{name}{index[0]}[{index[1]}]"
    elif index and index[0].isdigit():
        text = name + index[0]
    else:
        text = name + "".join(f"[{i}]" for i in index)
    if t & 3 == 2:
        selection, bits = (t >> 2) & 3, (t >> 4) & 0xFF
        if selection == 0 and bits & 0xF != 0xF:
            text += "." + "".join(c for c, b in zip("xyzw", (1, 2, 4, 8)) if bits & b)
        elif selection == 1:
            text += "." + "".join("xyzw"[(bits >> (2 * k)) & 3] for k in range(4))
        elif selection == 2:
            text += "." + "xyzw"[bits & 3]
    return {"-": f"-{text}", "abs": f"|{text}|", "-abs": f"-|{text}|"}.get(modifier, text), pos


def disassemble(data: bytes) -> list[str]:
    ch = chunks(data)
    lines = [f"// in: {signature(ch['ISGN'])}", f"// out: {signature(ch['OSGN'])}"]
    code = ch.get("SHEX") or ch["SHDR"]
    tok = struct.unpack_from(f"<{len(code) // 4}I", code)
    pos = 2
    while pos < len(tok):
        t = tok[pos]
        op = t & 0x7FF
        if op == 53:  # customdata: length in the next token
            pos += tok[pos + 1]
            continue
        end = pos + ((t >> 24) & 0x7F)
        name = OPS[op] if op < len(OPS) else f"op{op}"
        if name.startswith("dcl") or name == "hs_decls":
            pos = end
            continue
        p = pos + 1
        extended = t >> 31
        while extended:
            extended = tok[p] >> 31
            p += 1
        if name in CONDITIONAL:
            name += "_nz" if (t >> 18) & 1 else "_z"
        elif t & 0x2000:
            name += "_sat"
        operands = []
        while p < end:
            text, p = operand(tok, p)
            operands.append(text)
        lines.append(f"{name} {', '.join(operands)}")
        pos = end
    return lines


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("shader")
    parser.add_argument("--pass", dest="pass_index", type=int)
    parser.add_argument("--programs", type=int, default=1)
    parser.add_argument("--vertex", action="store_true")
    parser.add_argument("--data", default=str(ROOT / "work" / "game" / "REPO_Data"))
    args = parser.parse_args()

    files = [str(p) for p in Path(args.data).glob("*.assets")]
    for obj in UnityPy.load(*files).objects:
        if obj.type.name != "Shader":
            continue
        shader = obj.read_typetree()
        form = shader["m_ParsedForm"]
        if form["m_Name"] != args.shader:
            continue
        blob = bytes(shader["compressedBlob"])
        off, size = shader["offsets"][0][0], shader["compressedLengths"][0][0]
        data = lz4.block.decompress(blob[off:off + size], uncompressed_size=shader["decompressedLengths"][0][0])
        entries = []
        for k in range(struct.unpack_from("<I", data, 0)[0]):
            entry_off, entry_len, _ = struct.unpack_from("<III", data, 4 + 12 * k)
            entries.append(data[entry_off:entry_off + entry_len])
        keywords = form["m_KeywordNames"]
        for si, sub in enumerate(form["m_SubShaders"]):
            for pi, pass_ in enumerate(sub["m_Passes"]):
                if args.pass_index is not None and pi != args.pass_index:
                    continue
                print(f"### subshader {si} pass {pi}: {dict(pass_['m_State']['m_Tags']['tags'])}")
                if pass_["m_Type"] != 0:
                    print("(grab pass)")
                    continue
                program = pass_["progVertex" if args.vertex else "progFragment"]
                names = {v: k for k, v in pass_["m_NameIndices"]}
                params = program["m_CommonParameters"]
                slots = {names.get(b["m_NameIndex"]): b["m_Index"] for b in params["m_ConstantBufferBindings"]}
                for cb in params["m_ConstantBuffers"]:
                    slot = slots.get(names.get(cb["m_NameIndex"]), "?")
                    for v in cb["m_VectorParams"]:
                        reg, comp = divmod(v["m_Index"], 16)
                        print(f"//   cb{slot}[{reg}].{'xyzw'[comp // 4]} = {names.get(v['m_NameIndex'])}")
                subprograms = sorted((s for group in program["m_PlayerSubPrograms"] for s in group),
                                     key=lambda s: len(s["m_KeywordIndices"]))
                for s in subprograms[:args.programs]:
                    entry = entries[s["m_BlobIndex"]]
                    print(f"--- keywords {[keywords[i] for i in s['m_KeywordIndices'] if i < len(keywords)]}")
                    print("\n".join(disassemble(entry[entry.find(b'DXBC'):])))
        return 0
    print(f"shader {args.shader!r} not found", file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
