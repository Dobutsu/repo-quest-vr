#!/usr/bin/env bash
# Code-only Quest build after a SteamShim or unity-tools change (Unity -scriptsOnly, ~25-30 min instead of 60-90):
# rebuilds SteamShim, copies it and unity-tools' Runtime, Editor and StreamingAssets into the Unity project and rebuilds
# the player scripts. Everything else (the baked game, RepoXR, scenes,
# assets, shaders) is what the last full build left in the project and its build cache, so this is wrong after any
# change to shaders, assets, patches or the bake (then scripts/build.sh). The builder does not pass -scriptsOnly, so
# this runs QuestBuild.Prepare and QuestBuild.BuildApk directly.
#   scripts/codebuild.sh [<tag>]   -> work/build/repo-quest-vr-<tag>.apk (default: code), logs work/logs/*-<tag>.log
#   DEV=1 scripts/codebuild.sh ... a development build (Unity -developmentBuild; e.g. for GraphicsStateCollection tracing)
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
tag="${1:-code}"
logs=work/logs
project="$root/work/unity/REPOQuest"
unity="$(python3 scripts/lib/paths.py unity-editor)/Unity.app/Contents/MacOS/Unity"
apk="$root/work/build/repo-quest-vr.apk"   # the full build's path, so Unity reuses its build cache
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
# Gradle's cache where the builder keeps it (UnityBuildStep): each build leaves ~2 GB there
export GRADLE_USER_HOME="$root/work/tools/gradle"

[ -f "$apk" ] || { echo "no full build at $apk: run scripts/build.sh first" >&2; exit 1; }
if pgrep -f "projectPath $project" >/dev/null; then echo "Unity already has the project open" >&2; exit 1; fi
mkdir -p "$logs"

echo "== steamshim $(date +%T)"
"$DOTNET_ROOT/dotnet" build -c Release -nologo -v quiet tools/SteamShim > "$logs/steamshim-$tag.log" 2>&1
cp tools/SteamShim/bin/Release/netstandard2.1/Facepunch.Steamworks.Win64.dll "$project/Assets/Plugins/"
# Our runtime and editor scripts and StreamingAssets, as the builder's ProjectStep copies them
cp unity-tools/Runtime/*.cs "$project/Assets/RepoQuest/Runtime/"
cp unity-tools/Editor/*.cs "$project/Assets/RepoQuest/Editor/"
mkdir -p "$project/Assets/StreamingAssets/RepoQuest"
cp unity-tools/StreamingAssets/RepoQuest/* "$project/Assets/StreamingAssets/RepoQuest/"

echo "== unity prepare $(date +%T)"
"$unity" -batchmode -timestamps -projectPath "$project" -buildTarget Android \
    -executeMethod RepoQuest.Editor.QuestBuild.Prepare -addressablesEntries "$root/work/addressables-entries.json" \
    -logFile "$root/$logs/unity-prepare-$tag.log"

echo "== unity build (scripts only) $(date +%T)"
"$unity" -batchmode -timestamps -projectPath "$project" -buildTarget Android \
    -executeMethod RepoQuest.Editor.QuestBuild.BuildApk -apkPath "$apk" -scriptsOnly ${DEV:+-developmentBuild} \
    -versionName "dev-$(git rev-parse --short HEAD)-$tag" -versionCode 1 \
    -logFile "$root/$logs/unity-build-$tag.log"
grep -h "\[QuestBuild\]" "$logs/unity-build-$tag.log" | sed 's/^[^|]*|[^|]*|//'
grep -q "\[QuestBuild\] result: Succeeded" "$logs/unity-build-$tag.log"

cp "$apk" "work/build/repo-quest-vr-$tag.apk"
echo "== done $(date +%T)"
ls -la "work/build/repo-quest-vr-$tag.apk"
