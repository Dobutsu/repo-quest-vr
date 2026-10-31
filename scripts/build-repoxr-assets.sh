#!/usr/bin/env bash
# Build RepoXR's AssetBundle + Addressables for Android from external/RepoXR-Unity (commit matching RepoXR v1.2.x,
# the last one on Unity 2022.3, opened with the kit's Unity 6). Output: work/repoxr-android/{repoxrassets, Addressables/}
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
unity="${REPOQUEST_UNITY_EDITOR:-$(python3 "$root/scripts/lib/paths.py" unity-editor)}/Unity.app/Contents/MacOS/Unity"
project="$root/work/unity/RepoXRAssets"
out="$root/work/repoxr-android"

# Work on a copy (APFS clone) so the submodule stays pristine; keep its Library between runs
if [ ! -d "$project" ]; then
    mkdir -p "$(dirname "$project")"
    rsync -a --exclude .git "$root/external/RepoXR-Unity/" "$project/"
fi
mkdir -p "$project/Assets/RepoQuest"
cp -R "$root/unity-tools/RepoXRAssets/Editor" "$project/Assets/RepoQuest/"

# The project is on Unity 2022.3; on Unity 6 its packages move to the game's versions (package_swap.py), TextMesh Pro
# being part of uGUI 2.0. The XR packages stay at the ones RepoXR v1.2.3 is built against (XR Interaction Toolkit 2)
python3 - "$project/Packages" <<'EOF'
import json, sys
from pathlib import Path
packages = Path(sys.argv[1])
manifest = json.loads((packages / "manifest.json").read_text())
dependencies = manifest["dependencies"]
dependencies.pop("com.unity.textmeshpro", None)
dependencies.update({
    "com.unity.ugui": "2.0.0",
    "com.unity.addressables": "2.9.1",
    "com.unity.localization": "1.5.12",
    "com.unity.postprocessing": "3.5.4",
    # Unity 6.3's (as in RepoXR-Unity's own upgrade)
    "com.unity.shadergraph": "17.3.0",
    "com.unity.timeline": "1.8.12",
    "com.unity.feature.development": "1.0.2",
    "com.unity.collab-proxy": "2.12.4",
    "com.unity.visualscripting": "1.9.11",
})
(packages / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
(packages / "packages-lock.json").unlink(missing_ok=True)
EOF

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
