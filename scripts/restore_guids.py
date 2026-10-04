"""Give exported assets back the GUIDs they had in the original project, where the game depends on them.

AssetRipper assigns new GUIDs. Localization refers to string tables by the SharedTableData's GUID (its table collection
GUID is the asset GUID of the original project), so the addressable localization assets must keep their original
GUIDs, which the game's Addressables catalog still lists as keys (see addressables_catalog.py). Every reference to the
exported GUID in the project is rewritten along with the .meta.
Usage: python scripts/restore_guids.py <unity project> <addressables-entries.json>
"""

import json
import re
import sys
from pathlib import Path

GUID_RE = re.compile(r"guid: ([0-9a-f]{32})")
YAML_SUFFIXES = {".unity", ".prefab", ".asset", ".mat", ".controller", ".overrideController", ".anim", ".playable",
                 ".mixer", ".lighting", ".spriteatlas", ".physicMaterial", ".preset", ".signal"}


def main() -> int:
    project = Path(sys.argv[1])
    entries = json.loads(Path(sys.argv[2]).read_text())

    all_guids = {}
    for meta in (project / "Assets").rglob("*.meta"):
        m = GUID_RE.search(meta.read_text(errors="ignore"))
        if m:
            all_guids[m.group(1)] = meta

    remap = {}
    for entry in entries:
        original = entry.get("guid")
        meta = project / (entry["path"] + ".meta")
        if not original or not meta.exists():
            continue
        current = GUID_RE.search(meta.read_text()).group(1)
        if current == original:
            continue
        if original in all_guids and all_guids[original] != meta:
            print(f"skip {entry['path']}: GUID {original} already used by {all_guids[original]}")
            continue
        remap[current] = original
        meta.write_text(meta.read_text().replace(f"guid: {current}", f"guid: {original}"), newline="\n")

    if not remap:
        print("all GUIDs already original")
        return 0

    pattern = re.compile("|".join(remap))
    touched = 0
    for path in list((project / "Assets").rglob("*")) + list((project / "ProjectSettings").glob("*.asset")):
        if path.suffix not in YAML_SUFFIXES or not path.is_file():
            continue
        text = path.read_text(errors="surrogateescape")
        updated = pattern.sub(lambda m: remap[m.group(0)], text)
        if updated != text:
            path.write_text(updated, errors="surrogateescape", newline="\n")
            touched += 1

    print(f"restored {len(remap)} original GUIDs; {touched} files referenced them")
    return 0


if __name__ == "__main__":
    sys.exit(main())
