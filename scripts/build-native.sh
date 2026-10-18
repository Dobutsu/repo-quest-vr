#!/usr/bin/env bash
# unity-tools/Native/*.cpp -> unity-tools/Plugins/Android/arm64-v8a/lib<name>.so with Unity's NDK (arm64, Android 10+,
# libc++ linked in, so the APK needs no libc++_shared.so). The built libraries are committed: players' builders have no NDK.
#   scripts/build-native.sh
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
unity="${UNITY:-/Applications/Unity/Hub/Editor/2022.3.62f3}"
ndk="$unity/PlaybackEngines/AndroidPlayer/NDK"
clang="$(ls "$ndk"/toolchains/llvm/prebuilt/*/bin/aarch64-linux-android29-clang++ | head -1)"
strip="$(dirname "$clang")/llvm-strip"
out="$root/unity-tools/Plugins/Android/arm64-v8a"
mkdir -p "$out"

for source in "$root"/unity-tools/Native/*.cpp; do
    name="$(basename "$source" .cpp)"
    "$clang" -std=c++17 -O2 -fPIC -shared -fvisibility=hidden -Wall -Werror \
        -I "$unity/Unity.app/Contents/PluginAPI" -static-libstdc++ -Wl,--no-undefined -Wl,--exclude-libs,ALL -Wl,-z,max-page-size=16384 \
        -o "$out/lib$name.so" "$source" -llog
    "$strip" --strip-unneeded "$out/lib$name.so"
    echo "$out/lib$name.so"
done
