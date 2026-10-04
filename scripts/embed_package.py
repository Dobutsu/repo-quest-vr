"""Embed a registry package into Packages/ and stop its assemblies from auto-referencing precompiled DLLs.

The game's Assembly-CSharp.dll (a precompiled plugin in the exported project) must stay auto-referenced: the player
build only treats scripts from auto-referenced plugins as valid. But it defines global types such as `Version`, which
shadow System.Version inside package code compiled against it (Input System's InputSystem.cs). Packages that break
this way get their asmdefs switched to explicit precompiled references.
Built-in packages (com.unity.ugui) come from the Unity install instead of the registry cache.
Usage: python scripts/embed_package.py <unity project> <package name> <version> [--builtin <Unity install>]
  <Unity install>: the folder with Unity.app (macOS) or Editor\\ (Windows), or the Unity.app bundle
"""

import json
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent / "lib"))
from paths import WORK, builtin_packages  # noqa: E402


def main() -> int:
    project, name, version = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
    source = WORK / "packages" / f"{name}@{version}" / "package"
    if "--builtin" in sys.argv:
        source = builtin_packages(Path(sys.argv[sys.argv.index("--builtin") + 1])) / name
    if not source.is_dir():
        print(f"{source} missing (registry packages: run scripts/package_swap.py first)", file=sys.stderr)
        return 1

    target = project / "Packages" / name
    if target.exists():
        shutil.rmtree(target)
    shutil.copytree(source, target)

    changed = 0
    for asmdef in target.rglob("*.asmdef"):
        data = json.loads(asmdef.read_text())
        if data.get("overrideReferences"):
            continue
        data["overrideReferences"] = True
        data.setdefault("precompiledReferences", [])
        asmdef.write_text(json.dumps(data, indent=4) + "\n", newline="\n")
        changed += 1

    print(f"embedded {name}@{version}; {changed} asmdefs switched to explicit precompiled references")
    return 0


if __name__ == "__main__":
    sys.exit(main())
