#!/bin/bash
# Code-only build (Unity -scriptsOnly, ~15-20 min): only the DLLs change, scenes, assets and shaders come from the last
# full build (fastbuild.sh, b17). Wrong after any change to shaders or assets: then fastbuild.sh.
# This one: SteamShim with the auth-ticket fix from SteamKit 3.5.0 (ack by ticket CRC, issue #1567) on top of b17.
#   tasks/codebuild.sh [<tag>]   -> work/build/repo-quest-vr-<tag>.apk (default b18), logs work/logs/*-<tag>.log
set -euo pipefail
cd "/Volumes/Time Machine/REPO VR"
tag="${1:-b18}"
L=work/logs
P=work/unity/REPOQuest
U="/Volumes/Time Machine/Unity/Hub/Editor/6000.3.17f1/Unity.app/Contents/MacOS/Unity"
apk=work/build/repo-quest-vr.apk   # the same path as the full build, so Unity reuses its build cache
export DOTNET_ROOT=~/.dotnet

if pgrep -f "projectPath $PWD/$P" >/dev/null; then echo "Unity already has the project open" >&2; exit 1; fi

echo "== steamshim $(date +%T)"
"$DOTNET_ROOT/dotnet" build -c Release -nologo -v quiet tools/SteamShim > $L/steamshim-$tag.log 2>&1
cp tools/SteamShim/bin/Release/netstandard2.1/Facepunch.Steamworks.Win64.dll $P/Assets/Plugins/

echo "== unity prepare $(date +%T)"
"$U" -batchmode -timestamps -projectPath "$PWD/$P" -buildTarget Android \
    -executeMethod RepoQuest.Editor.QuestBuild.Prepare -addressablesEntries "$PWD/work/addressables-entries.json" \
    -logFile "$PWD/$L/unity-prepare-$tag.log"

echo "== unity build (scripts only) $(date +%T)"
"$U" -batchmode -timestamps -projectPath "$PWD/$P" -buildTarget Android \
    -executeMethod RepoQuest.Editor.QuestBuild.BuildApk -apkPath "$PWD/$apk" -scriptsOnly \
    -versionName "dev-$(git rev-parse --short HEAD)-$tag" -versionCode 1 \
    -logFile "$PWD/$L/unity-build-$tag.log"
grep -h "\[QuestBuild\]" $L/unity-build-$tag.log | sed 's/^[^|]*|[^|]*|//'
grep -q "\[QuestBuild\] result: Succeeded" $L/unity-build-$tag.log

cp $apk work/build/repo-quest-vr-$tag.apk
echo "== done $(date +%T)"
ls -la work/build/repo-quest-vr-$tag.apk
