#!/usr/bin/env bash
# Build RepoXR's AssetBundle + Addressables for Android from external/RepoXR-Unity (commit matching RepoXR v1.2.x,
# the last one on Unity 2022.3). Output: work/repoxr-android/{repoxrassets, Addressables/}
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
unity="/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity"
project="$root/work/unity/RepoXRAssets"
out="$root/work/repoxr-android"

# Work on a copy (APFS clone) so the submodule stays pristine; keep its Library between runs
if [ ! -d "$project" ]; then
    mkdir -p "$(dirname "$project")"
    rsync -a --exclude .git "$root/external/RepoXR-Unity/" "$project/"
fi
mkdir -p "$project/Assets/RepoQuest"
cp -R "$root/unity-tools/RepoXRAssets/Editor" "$project/Assets/RepoQuest/"

# The project's stand-ins for game scripts (MenuButton, MenuPage, ...) compile into Assembly-CSharp, so the bundle
# refers to "Assembly-CSharp::MenuButton"; on Quest the game assembly is REPO.Game (see rename_game_assemblies.py)
cat > "$project/Assets/Scripts/Base/REPO.Game.asmdef" <<'EOF'
{
    "name": "REPO.Game",
    "references": ["Unity.TextMeshPro", "UnityEngine.UI", "Unity.Localization"],
    "autoReferenced": true
}
EOF
# UnityEvent calls name their target type as a string ("MenuTwoOptions, Assembly-CSharp"); retarget those too
python3 - "$project/Assets" <<'EOF'
import re, sys
from pathlib import Path
assets = Path(sys.argv[1])
game_types = {m.group(1) for f in (assets / "Scripts" / "Base").rglob("*.cs")
              for m in re.finditer(r"^(?:public |internal )?(?:abstract |sealed |static )*(?:class|struct|enum) (\w+)", f.read_text(), re.M)}
pattern = re.compile(r"(m_TargetAssemblyTypeName: )(\w+), Assembly-CSharp\b")
changed = 0
for path in assets.rglob("*"):
    if path.suffix not in (".prefab", ".asset", ".unity") or not path.is_file():
        continue
    text = path.read_text(errors="surrogateescape")
    updated = pattern.sub(lambda m: f"{m.group(1)}{m.group(2)}, REPO.Game" if m.group(2) in game_types else m.group(0), text)
    if updated != text:
        path.write_text(updated, errors="surrogateescape")
        changed += 1
print(f"UnityEvent targets retargeted to REPO.Game in {changed} files")
EOF

# RepoXR's own shaders were written for Multi-Pass; add the stereo macros single-pass instanced needs
python3 "$root/scripts/patch_repoxr_shaders.py" "$project"

rm -rf "$out"  # stale bundles from earlier builds would ship in the APK
"$unity" -batchmode -projectPath "$project" -buildTarget Android \
    -executeMethod RepoQuest.Editor.RepoXRAndroidAssets.Build -outDir "$out" \
    -logFile "$root/work/unity-repoxr-assets.log"

ls -la "$out" "$out/Addressables"
