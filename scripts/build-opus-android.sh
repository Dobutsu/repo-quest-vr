#!/usr/bin/env bash
# libopus for Android arm64, named the way the game's Photon Voice imports it (opus_egpv): its Windows-only
# opus_egpv.dll exports the plain libopus API, so upstream libopus (BSD-3-Clause, xiph.org) built with Unity's NDK
# serves the same calls on the Quest.
#   scripts/build-opus-android.sh   -> work/opus-android/libopus_egpv.so
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
version="1.5.2"
src="$root/work/downloads/opus-$version"
out="$root/work/opus-android"
ndk="/Applications/Unity/Hub/Editor/2022.3.62f3/PlaybackEngines/AndroidPlayer/NDK/toolchains/llvm/prebuilt/darwin-x86_64/bin"

if [ ! -d "$src" ]; then
    mkdir -p "$root/work/downloads"
    curl -sSfL "https://downloads.xiph.org/releases/opus/opus-$version.tar.gz" | tar xz -C "$root/work/downloads"
fi

cd "$src"
if [ ! -f .libs/libopus.a ]; then
    ./configure --host=aarch64-linux-android \
        CC="$ndk/aarch64-linux-android29-clang" AR="$ndk/llvm-ar" RANLIB="$ndk/llvm-ranlib" STRIP="$ndk/llvm-strip" \
        CFLAGS="-O2 -fPIC" --disable-shared --enable-static --with-pic --disable-doc --disable-extra-programs \
        > "$root/work/opus-configure.log" 2>&1
    make -j8 > "$root/work/opus-make.log" 2>&1
fi

mkdir -p "$out"
"$ndk/aarch64-linux-android29-clang" -shared -o "$out/libopus_egpv.so" \
    -Wl,--whole-archive .libs/libopus.a -Wl,--no-whole-archive \
    -Wl,-soname,libopus_egpv.so -Wl,-z,max-page-size=16384 -lm
"$ndk/llvm-strip" --strip-unneeded "$out/libopus_egpv.so"

# Everything PhotonVoice.API imports from opus_egpv
for fn in opus_encoder_create opus_encoder_init opus_encoder_get_size opus_encoder_ctl opus_encoder_destroy \
          opus_encode opus_encode_float opus_decoder_create opus_decoder_init opus_decoder_get_size opus_decoder_ctl \
          opus_decoder_destroy opus_decode opus_decode_float opus_packet_get_bandwidth opus_packet_get_nb_channels \
          opus_get_version_string opus_strerror; do
    "$ndk/llvm-nm" -D --defined-only "$out/libopus_egpv.so" | grep -q " T $fn$" || { echo "missing export $fn" >&2; exit 1; }
done
echo "built $out/libopus_egpv.so (opus $version)"
