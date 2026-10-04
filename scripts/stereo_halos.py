"""Replace the game's Halo components with StereoHalo (unity-tools/Runtime/StereoHalo.cs) in the Quest VR project.

Unity's Halo glow is drawn for the first eye only with single-pass instanced (multiview) stereo: headlights and lamps
glowed in the left eye and not in the right. For every Halo in a prefab this adds a StereoHalo with the same colour and
size to its GameObject and sets the Halo's size to 0, so the Halo still exists (the game switches it on and off through
PropLight) but draws nothing; StereoHalo draws the glow for both eyes and follows the Halo's enabled state.
Edits the prefab YAML directly (no Unity reserialization of the game's assets); idempotent. The new components' file IDs
come from a hash of the file and the Halo's ID, so every build writes the same prefabs (Unity reimports changed ones).
Usage: python scripts/stereo_halos.py <unity project>
"""

import hashlib
import re
import sys
from pathlib import Path

SCRIPT_GUID = "5e7a4b2c9d1f4e3a8b6c0d2e4f6a8b1c"  # unity-tools/Runtime/StereoHalo.cs.meta

HALO = re.compile(r"--- !u!122 &(-?\d+)\nHalo:\n(?:  .*\n|    .*\n)*?  m_GameObject: \{fileID: (-?\d+)\}\n"
                  r"(?:  .*\n|    .*\n)*?    rgba: (\d+)\n  m_Size: ([-\d.eE]+)")


def colour(rgba: int) -> str:
    r, g, b, a = (rgba & 0xFF, rgba >> 8 & 0xFF, rgba >> 16 & 0xFF, rgba >> 24 & 0xFF)
    return f"{{r: {r / 255:.4f}, g: {g / 255:.4f}, b: {b / 255:.4f}, a: {a / 255:.4f}}}"


def stable_id(key: str, used: set[str]) -> str:
    """A 16-digit file ID derived from `key`, not yet used in the file"""
    for attempt in range(1000):
        digest = hashlib.sha256(f"{key}:{attempt}".encode()).digest()
        candidate = str(10**15 + int.from_bytes(digest[:8], "little") % (9 * 10**15))
        if candidate not in used:
            return candidate
    raise SystemExit(f"no free file ID for {key}")


def patch(path: Path) -> int:
    text = path.read_text(errors="surrogateescape")
    if SCRIPT_GUID in text or "\nHalo:\n" not in text:
        return 0
    used = set(re.findall(r"^--- !u!\d+ &(-?\d+)", text, re.M))
    added = []
    for halo_id, go_id, rgba, size in HALO.findall(text):
        if float(size) == 0:
            continue
        new_id = stable_id(f"{path.name}:{halo_id}", used)
        used.add(new_id)
        # The GameObject lists its components; add ours right after the Halo
        text, n = re.subn(rf"(--- !u!1 &{go_id}\nGameObject:\n(?:  .*\n)*?  - component: \{{fileID: {halo_id}\}}\n)",
                          rf"\1  - component: {{fileID: {new_id}}}\n", text, count=1)
        if n != 1:
            raise SystemExit(f"{path.name}: GameObject {go_id} of Halo {halo_id} not found")
        added.append(f"""--- !u!114 &{new_id}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {go_id}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {SCRIPT_GUID}, type: 3}}
  m_Name:
  m_EditorClassIdentifier:
  color: {colour(int(rgba))}
  size: {size}
""")
    if not added:
        return 0
    # The Halo keeps existing (and being toggled by the game) but draws nothing
    text = re.sub(r"(--- !u!122 &-?\d+\nHalo:\n(?:  .*\n|    .*\n)*?  m_Size: )[-\d.eE]+", r"\g<1>0", text)
    if not text.endswith("\n"):
        text += "\n"
    path.write_text(text + "".join(added), errors="surrogateescape", newline="\n")
    return len(added)


def main() -> int:
    assets = Path(sys.argv[1]) / "Assets"
    files = total = 0
    for path in list(assets.rglob("*.prefab")) + list(assets.rglob("*.unity")):
        n = patch(path)
        if n:
            files += 1
            total += n
    print(f"stereo halos: {total} Halo components replaced in {files} files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
