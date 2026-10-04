"""Give Resources prefabs back the names the game loads them by.

Photon instantiates networked objects by Resources path: the host sends the path, every client calls
Resources.Load(path). AssetRipper sanitizes some characters in file names (a comma became "_" in
"Module - Arctic - N - 1 - Kitchen, bathroom and small rec room"), so on the Quest that load fails and the room is
missing for the joining player. The game's ResourceManager lists every Resources path; each GameObject entry with no
matching file in the project is matched to the one file in the same folder whose name differs only in punctuation,
and that file (with its .meta, so the GUID stays) is renamed back. Other entries (MonoScripts the original project
kept in Resources folders) are not loadable assets and are skipped.

Usage: python scripts/restore_resource_names.py <REPO_Data> <project>
"""

import argparse
import os
import re
import sys

import UnityPy


def key(name):
    return re.sub(r"[^a-z0-9]", "", name.lower())


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("game_data")
    parser.add_argument("project")
    args = parser.parse_args()

    env = UnityPy.load(os.path.join(args.game_data, "globalgamemanagers"))
    manager = next(o for o in env.objects if o.type.name == "ResourceManager")
    entries = [(path, pptr) for path, pptr in manager.read().m_Container]

    resources = os.path.join(args.project, "Assets", "Resources")
    by_dir = {}
    known = set()
    for folder, _, names in os.walk(resources):
        # Resources paths use "/" on every OS (Windows' relpath gives "\\")
        rel = os.path.relpath(folder, resources).replace(os.sep, "/")
        rel = "" if rel == "." else rel.lower() + "/"
        for name in names:
            if name.endswith(".meta"):
                continue
            stem = os.path.splitext(name)[0]
            known.add(rel + stem.lower())
            by_dir.setdefault(rel, []).append((folder, name))

    renamed = unresolved = 0
    for path, pptr in entries:
        if path in known:
            continue
        target_obj = pptr.deref()
        if target_obj.type.name != "GameObject":
            continue
        folder_key, _, stem = path.rpartition("/")
        folder_key = folder_key + "/" if folder_key else ""
        candidates = [(folder, name) for folder, name in by_dir.get(folder_key, [])
                      if key(os.path.splitext(name)[0]) == key(stem)]
        if len(candidates) != 1:
            print(f"  unresolved: {path} ({len(candidates)} candidates)", file=sys.stderr)
            unresolved += 1
            continue

        folder, name = candidates[0]
        # The container path is lower case; the object's own name has the original spelling
        original = target_obj.read().m_Name
        if key(original) != key(stem) or original.lower() != stem:
            original = stem
        target = original + os.path.splitext(name)[1]
        os.rename(os.path.join(folder, name), os.path.join(folder, target))
        if os.path.exists(os.path.join(folder, name + ".meta")):
            os.rename(os.path.join(folder, name + ".meta"), os.path.join(folder, target + ".meta"))
        print(f"  renamed: {name} -> {target}")
        known.add(path)
        renamed += 1

    print(f"Resources names: {renamed} restored, {unresolved} unresolved")
    return 1 if unresolved else 0


if __name__ == "__main__":
    sys.exit(main())
