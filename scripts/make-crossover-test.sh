#!/usr/bin/env bash
# Spike 2b: a copy of REPO in the CrossOver "Steam" bottle that runs the baked assemblies with no BepInEx at all.
#   scripts/make-crossover-test.sh [universal|all]   (default: universal = flat-screen test; uses the windows bake)
# The copy is an APFS clone of the Steam install (no extra disk space); the Steam install itself is not touched.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
target="${1:-universal}"
bottle="$HOME/Library/Application Support/CrossOver/Bottles/Steam/drive_c"
game="$root/work/game"
dest="$bottle/REPO-baked"
baked="$root/work/baked/$target-${2:-windows}"
release="$root/work/downloads/RepoXR-1.2.3/BepInEx/plugins/RepoXR"
bepinex="$root/work/downloads/BepInEx5/BepInEx/core"
managed="$dest/REPO_Data/Managed"

[ -f "$baked/Assembly-CSharp.dll" ] || { echo "run scripts/bake.sh $target windows first" >&2; exit 1; }

rm -rf "$dest"
cp -cR "$(readlink "$game" || echo "$game")" "$dest"

# Baked game assemblies replace the originals. RepoXR and its dependencies go into Managed as well: the player
# probes there, and assemblies listed in ScriptingAssemblies.json get their types registered with the native
# serializer, which RepoXR's asset bundle needs (the job FixPluginTypesSerialization does under BepInEx)
cp "$baked"/*.dll "$managed/"
cp -n "$release"/RuntimeDeps/*.dll "$managed/" || true   # never replace a DLL the game already ships
mkdir -p "$managed/RuntimeDeps"   # RepoXR LoadFile()s whatever is here; must stay empty to avoid duplicate assemblies
cp -R "$release/Addressables" "$release/repoxrassets" "$managed/"
cp "$bepinex"/{BepInEx.dll,0Harmony.dll,MonoMod.Utils.dll,MonoMod.RuntimeDetour.dll,Mono.Cecil.dll,Mono.Cecil.Rocks.dll} "$managed/"
# File name must match the assembly name for the player's probing
find "$HOME/.nuget/packages/customdiscoverstatelib" -name 'CustomDiscoverStateLib.dll' -path '*netstandard2.1*' \
    -exec cp {} "$managed/Kistras-CustomDiscoverStateLib.dll" \;

cp "$root/tools/RepoXR.Standalone/bin/Release/netstandard2.1/RepoXR.Standalone.dll" "$managed/"

# Register the bootstrap with the player: load its assembly and call its RuntimeInitializeOnLoad methods
python3 - "$dest/REPO_Data" <<'EOF'
import json, sys, pathlib
data = pathlib.Path(sys.argv[1])

sa_path = data / "ScriptingAssemblies.json"
sa = json.loads(sa_path.read_text())
managed = data / "Managed"
mod_assemblies = ["RepoXR.Standalone.dll", "RepoXR.dll", "BepInEx.dll", "Kistras-CustomDiscoverStateLib.dll"]
mod_assemblies += sorted(p.name for p in managed.glob("Unity.XR.*.dll"))
mod_assemblies += ["Unity.Mathematics.dll", "UnityEngine.SpatialTracking.dll"]
for name in mod_assemblies:
    if name not in sa["names"] and (managed / name).exists():
        sa["names"].append(name)
        sa["types"].append(16)
sa_path.write_text(json.dumps(sa))

ri_path = data / "RuntimeInitializeOnLoads.json"
ri = json.loads(ri_path.read_text())
entries = [
    ("InstallResolver", 4),  # SubsystemRegistration
    ("StartPlugin", 1),      # BeforeSceneLoad
]
for method, load_type in entries:
    entry = {"assemblyName": "RepoXR.Standalone", "nameSpace": "RepoXR.Standalone", "className": "Bootstrap",
             "methodName": method, "loadTypes": load_type, "isUnityClass": False}
    if entry not in ri["root"]:
        ri["root"].append(entry)
ri_path.write_text(json.dumps(ri))
EOF

# Lets SteamAPI initialise for a copy outside steamapps (Steam must be running in the bottle)
echo 3241660 > "$dest/steam_appid.txt"

echo "ready: C:\\REPO-baked\\REPO.exe  (baked set: $target)"
