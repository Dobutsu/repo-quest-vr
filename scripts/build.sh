#!/usr/bin/env bash
# Our Quest build on the Mac, through the same pipeline the players' builder runs (builder/QuestBuilder.Core): export
# (AssetRipper; only when the game changed), bake, Unity project, import and APK. Steps whose inputs did not change are
# skipped; the kit (scripts/make-kit.sh) is refreshed from this checkout first.
#   scripts/build.sh [<apk name>] [-developmentBuild] [-profiling] [--force] [--only export,bake,project,unity]
# REPOQUEST_VERSION_SUFFIX=-perf appends to the version (diagnostic builds: PerfLog turns on for "-perf")
# -> work/build/<apk name>.apk (default repo-quest-vr); logs in work/logs/ (builder.log, unity-prepare.log, unity-build.log)
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
dotnet="$DOTNET_ROOT/dotnet"

name="repo-quest-vr"
args=()
while [ $# -gt 0 ]; do
    case "$1" in
        -developmentBuild) args+=(--development) ;;
        -profiling) args+=(--profiling) ;;
        --only) args+=(--only "$2"); shift ;;
        --*) args+=("$1") ;;
        *) name="$1" ;;
    esac
    shift
done

"$root/scripts/make-kit.sh" >/dev/null
"$dotnet" build -c Release -v quiet -nologo "$root/builder/QuestBuilder.Cli" | { grep -E "error|warning" || true; }
cd "$root"
# Development builds carry the commit (the main menu shows it) and versionCode 1, below every release
"$root/builder/QuestBuilder.Cli/bin/Release/net8.0/QuestBuilder.Cli" build --dev --apk-name "$name" \
    --version-name "dev-$(git rev-parse --short HEAD)${REPOQUEST_VERSION_SUFFIX:-}" --version-code 1 ${args[@]+"${args[@]}"}
