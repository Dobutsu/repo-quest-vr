"""Give the game's Assembly-CSharp(-firstpass) DLLs non-reserved names inside the Unity project.

Unity reserves Assembly-CSharp* for the assemblies it compiles from loose scripts; the player build then treats every
script class from a precompiled DLL with such a name as missing and ships those objects' data unreadable (the Quest
crash "sharedassets0.assets is corrupted! [Position out of bounds!]"). Renaming keeps every scene/prefab reference
(they point at the DLL's GUID + class hash), but three things name the assembly as text and are rewritten too:
  - assembly references in the other DLLs (tools/AssemblyRenamer)
  - UnityEvent targets: m_TargetAssemblyTypeName: <Type>, Assembly-CSharp
  - Easy Save 3's list of assemblies to search for saved types (ES3Defaults.asset)
Usage: python scripts/rename_game_assemblies.py <unity project> [extra dlls referencing the game...]
"""

import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent / "lib"))
from paths import GAME, run_tool  # noqa: E402

NAMES = {
    "Assembly-CSharp-firstpass": "REPO.Game.FirstPass",
    "Assembly-CSharp": "REPO.Game",
}


def rename_dlls(dlls: list[Path]) -> None:
    args = [a for old, new in NAMES.items() for a in ("--map", f"{old}={new}")]
    args += ["--search", str(GAME / "REPO_Data" / "Managed")]
    run_tool("assembly-renamer", [*args, *map(str, dlls)])


def main() -> int:
    project = Path(sys.argv[1])
    plugins = project / "Assets" / "Plugins"
    extra = [Path(p) for p in sys.argv[2:]]

    # .meta files travel with the renamed DLLs so the GUID (and every reference to it) stays the same
    metas = {old: plugins / f"{old}.dll.meta" for old in NAMES}
    rename_dlls(sorted(plugins.glob("*.dll")) + extra)
    for old, meta in metas.items():
        if meta.exists():
            meta.rename(plugins / f"{NAMES[old]}.dll.meta")

    # Longest name first so "-firstpass" is not caught by the shorter one
    event_re = re.compile(r"(m_TargetAssemblyTypeName: [^,\n]+, )(Assembly-CSharp-firstpass|Assembly-CSharp)\b")
    touched = 0
    for path in (project / "Assets").rglob("*"):
        if path.suffix not in (".unity", ".prefab", ".asset", ".controller", ".playable") or not path.is_file():
            continue
        text = path.read_text(errors="surrogateescape")
        updated = event_re.sub(lambda m: m.group(1) + NAMES[m.group(2)], text)
        if path.name == "ES3Defaults.asset":
            for old, new in NAMES.items():
                updated = updated.replace(f"    - {old}\n", f"    - {old}\n    - {new}\n", 1)
        if updated != text:
            path.write_text(updated, errors="surrogateescape", newline="\n")
            touched += 1

    print(f"renamed {', '.join(f'{o} -> {n}' for o, n in NAMES.items())}; {touched} asset files updated")
    return 0


if __name__ == "__main__":
    sys.exit(main())
