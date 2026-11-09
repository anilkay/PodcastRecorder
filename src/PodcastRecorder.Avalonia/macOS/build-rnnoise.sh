#!/usr/bin/env bash
# Builds librnnoise.dylib (Apple Silicon + Intel in one file) from the RNNoise 0.2 release, for
# the noise reduction on macOS; Windows and Linux get theirs from a NuGet package. Runs on macOS
# with the Xcode command line tools.
#
#   bash src/PodcastRecorder.Avalonia/macOS/build-rnnoise.sh <output .dylib>
#
# Pass the result to the build as RNNOISE_DYLIB (an environment variable or -p:RNNOISE_DYLIB=...):
# PodcastRecorder.Audio then copies it next to the app as librnnoise.dylib. Without it, the app
# still builds and runs, with noise reduction switched off.
set -euo pipefail

out=$1
here=$(cd "$(dirname "$0")" && pwd)
version=0.2
sha256=90fce4b00b9ff24c08dbfe31b82ffd43bae383d85c5535676d28b0a2b11c0d37
work=$(mktemp -d)

curl -fsSL -o "$work/rnnoise.tar.gz" \
    "https://github.com/xiph/rnnoise/releases/download/v$version/rnnoise-$version.tar.gz"
echo "$sha256  $work/rnnoise.tar.gz" | shasum -a 256 -c -
tar -xzf "$work/rnnoise.tar.gz" -C "$work"
src="$work/rnnoise-$version"

# The library's sources (librnnoise_la_SOURCES in Makefile.am, without x86 run-time dispatch, so
# one plain C build runs on every Mac). The model weights are in rnnoise_data.c.
sources=(denoise.c rnn.c pitch.c kiss_fft.c celt_lpc.c nnet.c nnet_default.c
         parse_lpcnet_weights.c rnnoise_data.c rnnoise_tables.c)

for arch in arm64 x86_64; do
    objects=()
    for file in "${sources[@]}"; do
        object="$work/$arch-${file%.c}.o"
        # rnnoise/ holds os_support.h, which the release forgets and the ARM (NEON) code includes.
        clang -arch "$arch" -mmacosx-version-min=11.0 -O2 -fPIC -fvisibility=hidden \
            -DRNNOISE_BUILD -DDISABLE_DEBUG_FLOAT \
            -I"$src/include" -I"$src/src" -I"$here/rnnoise" -w \
            -c "$src/src/$file" -o "$object"
        objects+=("$object")
    done
    clang -arch "$arch" -mmacosx-version-min=11.0 -dynamiclib \
        -install_name @rpath/librnnoise.dylib -o "$work/librnnoise-$arch.dylib" "${objects[@]}"
done

mkdir -p "$(dirname "$out")"
lipo -create "$work/librnnoise-arm64.dylib" "$work/librnnoise-x86_64.dylib" -output "$out"
lipo -info "$out"
# The four functions the app calls must be exported.
for symbol in rnnoise_create rnnoise_destroy rnnoise_process_frame rnnoise_get_frame_size; do
    nm -gU -arch arm64 "$out" | grep -q " _$symbol\$"
    nm -gU -arch x86_64 "$out" | grep -q " _$symbol\$"
done
rm -rf "$work"
