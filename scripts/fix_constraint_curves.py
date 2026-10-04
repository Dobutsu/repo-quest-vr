"""Split constraint source-weight curves that AssetRipper merged into one.

A clip that animates several source weights of a constraint (Elsa's head and tail follow either the small or the big
body: m_Sources.Array.data[0].weight and data[1].weight of a ParentConstraint) stores them as bindings with
attributes 15, 31, ... (source index * 16 + 15). AssetRipper 2.0 names all of them data[0].weight and writes their
keys into a single curve, interleaved by time: Elsa's small clips get keys 1, 0 at the start and 1, 0 at the end. In
the player that curve ramps between the two values and ends at 0, data[1] is never animated, and when a clip holds
its last frame (petting) the head's weights add up to 0 and it drops to the constraint's rest position below the
floor. The clip's genericBindings (exported unchanged) keep the original order of the attributes, and the merged keys
come in groups of equal time in that order; checked against the decoded original clips of v0.4.4.3 (27 clips, Elsa
and Trudge). Anything that does not fit that pattern stops the script rather than guessing.
Edits the .anim YAML directly; idempotent.
Usage: python scripts/fix_constraint_curves.py <unity project>
"""

import re
import sys
import zlib
from pathlib import Path

# Position, Rotation, Scale, Aim, Parent, LookAt constraints
CONSTRAINTS = {"1818360608", "1316491299", "895512359", "1183024399", "1773428102", "2083052967"}
MERGED = "m_Sources.Array.data[0].weight"

FLOAT_CURVE = re.compile(
    r"  - serializedVersion: 2\n    curve:\n      serializedVersion: 2\n      m_Curve:\n"
    r"(?P<keys>(?:      - serializedVersion: 3\n(?:        .*\n)+)*)"
    r"(?P<post>      m_PreInfinity: .*\n      m_PostInfinity: .*\n      m_RotationOrder: .*\n)"
    r"    attribute: (?P<attr>.*)\n    path: (?P<path>.*)\n    classID: (?P<cls>\d+)\n(?P<rest>    script: .*\n    flags: .*\n)")
KEY = re.compile(r"      - serializedVersion: 3\n        time: (\S+)\n(?:        .*\n)+")


def weight_attributes(text: str, where: str) -> dict[tuple[int, str], list[int]]:
    """(path hash, class ID) -> source weight attributes in binding order"""
    result: dict[tuple[int, str], list[int]] = {}
    for path, attr, type_id in re.findall(r"      path: (\d+)\n      attribute: (\d+)\n      script: \{fileID: 0\}\n"
                                          r"      typeID: (\d+)", text):
        if type_id in CONSTRAINTS:
            if int(attr) & 15 != 15:
                raise SystemExit(f"{where}: unknown constraint attribute {attr}, extend fix_constraint_curves.py")
            result.setdefault((int(path), type_id), []).append(int(attr))
    return result


def fix(path: Path) -> int:
    text = path.read_text(errors="surrogateescape")
    if MERGED not in text:
        return 0
    attrs = weight_attributes(text, path.name)
    present = {(m["path"], m["attr"]) for m in FLOAT_CURVE.finditer(text)}
    changed = 0

    def split(m: re.Match) -> str:
        nonlocal changed
        if m["attr"] != MERGED or m["cls"] not in CONSTRAINTS:
            return m[0]
        order = attrs.get((zlib.crc32(m["path"].encode()), m["cls"]), [])
        if len(order) < 2 or (m["path"], "m_Sources.Array.data[1].weight") in present:  # nothing merged, or done
            return m[0]
        keys = list(KEY.finditer(m["keys"]))
        per_attr: list[list[str]] = [[] for _ in order]
        i = 0
        while i < len(keys):
            n = 1
            while i + n < len(keys) and keys[i + n][1] == keys[i][1]:
                n += 1
            if n != len(order):
                raise SystemExit(f"{path.name} {m['path']}: {n} keys at time {keys[i][1]} for {len(order)} weights")
            for j in range(n):
                per_attr[j].append(keys[i + j][0])
            i += n
        changed += 1
        return "".join(
            "  - serializedVersion: 2\n    curve:\n      serializedVersion: 2\n      m_Curve:\n" + "".join(attr_keys)
            + m["post"] + f"    attribute: m_Sources.Array.data[{attr >> 4}].weight\n    path: {m['path']}\n"
            + f"    classID: {m['cls']}\n" + m["rest"]
            for attr, attr_keys in zip(order, per_attr))

    text = FLOAT_CURVE.sub(split, text)
    if changed:
        path.write_text(text, errors="surrogateescape", newline="\n")
    return changed


def main() -> int:
    assets = Path(sys.argv[1]) / "Assets"
    files = curves = 0
    for path in sorted(assets.rglob("*.anim")):
        n = fix(path)
        if n:
            files += 1
            curves += n
    print(f"constraint curves: {curves} merged source-weight curves split in {files} clips")
    return 0


if __name__ == "__main__":
    sys.exit(main())
