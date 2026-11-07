#!/bin/bash
# Quick full build without export and project steps: kit -> bake -> stage -> rename -> copy into the Unity project -> shaders -> Unity build (~80 min)
set -euo pipefail
cd "/Volumes/Time Machine/REPO VR"
L=work/logs
P=work/unity/REPOQuest/Assets
ST=work/baked/quest-staging

echo "== kit $(date +%T)"; bash scripts/make-kit.sh > $L/kit-b17.log 2>&1
echo "== bake $(date +%T)"; scripts/build.sh --only bake > $L/bake-b17.log 2>&1
python3 - <<'E'
import json; r=json.load(open("work/baked/all-android/bake-report.json"))
print({k: (len(v) if isinstance(v, list) else v) for k, v in r.items() if k in ("applied","failed","lostAnchors")})
E
echo "== stage $(date +%T)"
rm -rf $ST; mkdir -p $ST
for f in 0Harmony Assembly-CSharp PhotonVoice RepoXR Unity.InputSystem UnityEngine.UI; do cp work/baked/all-android/$f.dll $ST/; done
cp work/kit/prebuilt/standalone/RepoXR.Standalone.dll $ST/
DOTNET_ROOT=~/.dotnet builder/QuestBuilder.Cli/bin/Release/net8.0/QuestBuilder.Cli --tool assembly-renamer \
  --map Assembly-CSharp=REPO.Game --map Assembly-CSharp-firstpass=REPO.Game.FirstPass \
  --search work/game/REPO_Data/Managed --search work/deps/BepInEx-5.4.23.5/BepInEx/core \
  $ST/0Harmony.dll $ST/Assembly-CSharp.dll $ST/PhotonVoice.dll $ST/RepoXR.dll $ST/Unity.InputSystem.dll $ST/UnityEngine.UI.dll $ST/RepoXR.Standalone.dll
ls $ST
echo "== copy $(date +%T)"
cp $ST/REPO.Game.dll $ST/PhotonVoice.dll $P/Plugins/
cp $ST/RepoXR.dll $ST/RepoXR.Standalone.dll $ST/0Harmony.dll $P/Plugins/RepoXR/
echo "== shaders $(date +%T)"; python3 scripts/generate_shaders.py work/unity/REPOQuest work/shader-states.json > $L/shaders-b17.log 2>&1
echo "== unity $(date +%T)"
scripts/build.sh --only unity > $L/unity-b17.log 2>&1
echo "== done $(date +%T)"
ls -la work/build/repo-quest-vr.apk
cp work/build/repo-quest-vr.apk work/build/repo-quest-vr-b17.apk
