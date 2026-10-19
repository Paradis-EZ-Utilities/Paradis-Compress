#!/usr/bin/env bash
# Builds a native Linux ffmpeg/ffprobe from exactly the same pinned sources and options as the
# Windows build, so the automated tests on Linux run the same FFmpeg version and encoders that ship.
# Run build-ffmpeg-windows.sh first (it fetches and verifies the sources).
#
# Usage: packaging/ffmpeg/build-ffmpeg-linux-for-tests.sh [work-dir]   -> <work-dir>/linux/bin/{ffmpeg,ffprobe}
set -euo pipefail
trap 'echo "BUILD FAILED at line $LINENO" >&2' ERR
WORK=$(realpath -m "${1:-$PWD/ffmpeg-build}")
OUT=$WORK/linux
PREFIX=$OUT/prefix
SRC=$OUT/src
mkdir -p "$PREFIX" "$SRC" "$OUT/bin"
for t in "$WORK"/sources/*.tar.gz; do tar -xzf "$t" -C "$SRC"; done
export PKG_CONFIG_PATH=$PREFIX/lib/pkgconfig PKG_CONFIG_LIBDIR=$PREFIX/lib/pkgconfig
J=$(nproc)
(cd "$SRC/x264" && ./configure --prefix="$PREFIX" --enable-static --disable-cli --enable-pic --disable-opencl && make -j$J && make install)
(cd "$SRC/lame-3.100" && sed -i '/lame_init_old/d' include/libmp3lame.sym && ./configure --prefix="$PREFIX" --enable-static --disable-shared --disable-frontend --disable-decoder --disable-gtktest && make -j$J && make install)
(cd "$SRC/dav1d" && meson setup build --prefix="$PREFIX" --libdir=lib --default-library=static --buildtype=release -Denable_tools=false -Denable_tests=false && ninja -C build install)
# A shallow fetch has no tags, so FFmpeg would report a bare commit hash; name the release instead.
FFMPEG_TAG=$(sed -n "s/^FFMPEG_TAG=//p" "$(dirname "$0")/build-ffmpeg-windows.sh")
echo "${FFMPEG_TAG#n}" > "$SRC/ffmpeg/VERSION"
(cd "$SRC/ffmpeg" && ./configure --prefix="$PREFIX" --pkg-config-flags=--static \
    --extra-cflags="-I$PREFIX/include" --extra-ldflags="-L$PREFIX/lib" --extra-libs="-lpthread -lm" \
    --enable-gpl --enable-libx264 --enable-libmp3lame --enable-libdav1d \
    --disable-autodetect --enable-pthreads \
    --disable-shared --enable-static --disable-doc --disable-ffplay --disable-network --disable-debug \
 && make -j$J)
cp "$SRC/ffmpeg/ffmpeg" "$SRC/ffmpeg/ffprobe" "$OUT/bin/"
"$OUT/bin/ffmpeg" -hide_banner -version | head -1
