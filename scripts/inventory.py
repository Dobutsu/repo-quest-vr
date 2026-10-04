"""Inventory of a R.E.P.O. install: what has to change to run it on Android/Quest.

Usage: work/.venv/bin/python scripts/inventory.py work/game [--json out.json]
Reads only; never writes into the game directory.
"""

import argparse
import collections
import json
import re
import sys
from pathlib import Path

import UnityPy

GUID_RE = re.compile(rb"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")

# Shader names that ship with the engine or with packages whose source we can recompile
BUILTIN_PREFIXES = (
    "Standard", "Hidden/", "Legacy Shaders/", "Mobile/", "Nature/", "Particles/", "Skybox/",
    "Sprites/", "UI/", "Unlit/", "GUI/", "TextMeshPro/", "Autodesk Interactive",
)


def unity_version(data_dir: Path) -> str:
    head = (data_dir / "globalgamemanagers").read_bytes()[:128]
    match = re.search(rb"\d{4}\.\d+\.\d+[abfp]\d+", head)
    return match.group(0).decode() if match else "unknown"


def game_version(data_dir: Path) -> str:
    # Same trick as RepoXR's preloader: the BuildManager Version asset has "Version - <branch>" then the title
    raw = (data_dir / "sharedassets0.assets").read_bytes()
    idx = raw.rfind(b"Version - ")
    if idx < 0:
        return "unknown"
    name_len = int.from_bytes(raw[idx - 4:idx], "little")
    pos = idx + name_len
    pos = (pos + 3) & ~3
    title_len = int.from_bytes(raw[pos:pos + 4], "little")
    return raw[pos + 4:pos + 4 + title_len].decode(errors="replace")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("game_dir", type=Path)
    parser.add_argument("--json", type=Path)
    args = parser.parse_args()

    data_dir = args.game_dir / "REPO_Data"
    report: dict = {
        "unity_version": unity_version(data_dir),
        "game_version": game_version(data_dir),
        "native_plugins": sorted(p.name for p in (data_dir / "Plugins" / "x86_64").glob("*")),
        "managed_assemblies": sorted(p.name for p in (data_dir / "Managed").glob("*.dll")),
    }

    env = UnityPy.load(str(data_dir))
    type_counts: collections.Counter = collections.Counter()
    tex_formats: collections.Counter = collections.Counter()
    tex_bytes = 0
    shaders: list[str] = []
    photon_settings = {}

    for obj in env.objects:
        type_name = obj.type.name
        type_counts[type_name] += 1
        try:
            if type_name == "Texture2D":
                tex = obj.read()
                fmt = getattr(tex.m_TextureFormat, "name", str(tex.m_TextureFormat))
                tex_formats[fmt] += 1
                tex_bytes += tex.m_CompleteImageSize
            elif type_name == "Shader":
                shader = obj.read()
                name = getattr(shader.m_ParsedForm, "m_Name", None) if hasattr(shader, "m_ParsedForm") else None
                shaders.append(name or getattr(shader, "m_Name", "?"))
            elif type_name == "MonoBehaviour" and not photon_settings:
                raw = obj.get_raw_data()
                if b"PhotonServerSettings" in raw[:200]:
                    guids = [g.decode() for g in GUID_RE.findall(raw)]
                    photon_settings = {"asset": "PhotonServerSettings", "guids_in_order": guids}
        except Exception as exc:  # noqa: BLE001 - inventory must keep going
            type_counts[f"!read_error:{type_name}"] += 1
            print(f"warn: {type_name} {obj.path_id}: {exc}", file=sys.stderr)

    shaders = sorted(set(shaders))
    custom = [s for s in shaders if not s.startswith(BUILTIN_PREFIXES)]
    report.update({
        "object_counts": dict(type_counts.most_common()),
        "texture_formats": dict(tex_formats.most_common()),
        "texture_mbytes": round(tex_bytes / 2**20, 1),
        "shaders_total": len(shaders),
        "shaders_custom": custom,
        "shaders_builtin_like": [s for s in shaders if s not in custom],
        "photon_server_settings": photon_settings,
    })

    text = json.dumps(report, indent=2, ensure_ascii=False)
    if args.json:
        args.json.write_text(text)
    print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
