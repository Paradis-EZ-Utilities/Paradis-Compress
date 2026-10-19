#!/usr/bin/env bash
# Reproducibly cross-compiles the FFmpeg and ffprobe executables that ship with Paradis Compress.
#
#   Host:    Linux (tested on Ubuntu 24.04) with mingw-w64, nasm, meson, ninja, pkg-config, make
#   Target:  Windows x64, fully static .exe files (no DLLs)
#
# Usage:   packaging/ffmpeg/build-ffmpeg-windows.sh [work-dir]
# Output:  <work-dir>/dist/ffmpeg.exe, ffprobe.exe, BUILDINFO.txt, plus <work-dir>/sources/*.tar.gz
#          (the exact corresponding source of every component, for the GPL source offer).
#
# Every input is pinned to an exact git commit or a sha256-verified tarball.
set -euo pipefail
trap 'echo "BUILD FAILED at line $LINENO" >&2' ERR

FFMPEG_TAG=n8.1.3
FFMPEG_COMMIT=1041abdc962f4cc4f394aa8de9dc5236c0c3b9e7   # peeled commit of tag n8.1.3
X264_COMMIT=31e19f92f00c7003fa115047ce50978bc98c3a0d      # x264 "stable" branch
DAV1D_TAG=1.5.4
DAV1D_COMMIT=54706fc6bc0cdecab7e9593974a4039cc038fca7    # peeled commit of tag 1.5.4
LAME_URL=http://archive.ubuntu.com/ubuntu/pool/main/l/lame/lame_3.100.orig.tar.gz   # = upstream lame-3.100.tar.gz
LAME_SHA256=ddfe36cab873794038ae2c1210557ad34857a4b6bdc515785d1da9e175b1da1e

WORK=$(realpath -m "${1:-$PWD/ffmpeg-build}")
HOST=x86_64-w64-mingw32
PREFIX=$WORK/prefix
SRC=$WORK/src
mkdir -p "$PREFIX" "$SRC" "$WORK/dist" "$WORK/sources"
export PKG_CONFIG_PATH=$PREFIX/lib/pkgconfig
export PKG_CONFIG_LIBDIR=$PREFIX/lib/pkgconfig
JOBS=$(nproc)

fetch_git() { # url commit dir
  # Offline rebuild from the published source archives: put them in <work>/sources first.
  if [ ! -d "$SRC/$3" ] && [ -f "$WORK/sources/$3-$2.tar.gz" ]; then
    tar -xzf "$WORK/sources/$3-$2.tar.gz" -C "$SRC"
    return
  fi
  [ -d "$SRC/$3/.git" ] || [ ! -d "$SRC/$3" ] || return 0
  if [ ! -d "$SRC/$3/.git" ]; then
    git init -q "$SRC/$3"
    git -C "$SRC/$3" fetch -q --depth 1 "$1" "$2"
    git -C "$SRC/$3" checkout -q FETCH_HEAD
  fi
  test "$(git -C "$SRC/$3" rev-parse HEAD)" = "$2"
  git -C "$SRC/$3" archive --format=tar.gz --prefix="$3/" -o "$WORK/sources/$3-$2.tar.gz" HEAD
}

echo "== sources"
fetch_git https://github.com/FFmpeg/FFmpeg "$FFMPEG_COMMIT" ffmpeg
fetch_git https://github.com/mirror/x264 "$X264_COMMIT" x264
fetch_git https://github.com/videolan/dav1d "$DAV1D_COMMIT" dav1d
if [ ! -f "$WORK/sources/lame-3.100.tar.gz" ]; then
  curl -fsSL "$LAME_URL" -o "$WORK/sources/lame-3.100.tar.gz"
fi
echo "$LAME_SHA256  $WORK/sources/lame-3.100.tar.gz" | sha256sum -c -
[ -d "$SRC/lame-3.100" ] || tar -xzf "$WORK/sources/lame-3.100.tar.gz" -C "$SRC"

echo "== x264"
if [ ! -f "$PREFIX/lib/libx264.a" ]; then
  (cd "$SRC/x264" && ./configure --host=$HOST --cross-prefix=$HOST- --prefix="$PREFIX" \
      --enable-static --disable-cli --enable-strip --disable-opencl \
   && make -j"$JOBS" && make install)
fi

echo "== lame"
if [ ! -f "$PREFIX/lib/libmp3lame.a" ]; then
  (cd "$SRC/lame-3.100" \
   && sed -i '/lame_init_old/d' include/libmp3lame.sym \
   && ./configure --host=$HOST --prefix="$PREFIX" --enable-static --disable-shared \
      --disable-frontend --disable-decoder --disable-gtktest \
   && make -j"$JOBS" && make install)
fi

echo "== dav1d"
if [ ! -f "$PREFIX/lib/libdav1d.a" ]; then
  cat > "$WORK/cross.ini" <<INI
[binaries]
c = '$HOST-gcc'
cpp = '$HOST-g++'
ar = '$HOST-ar'
strip = '$HOST-strip'
windres = '$HOST-windres'
pkg-config = 'pkg-config'
[host_machine]
system = 'windows'
cpu_family = 'x86_64'
cpu = 'x86_64'
endian = 'little'
INI
  rm -rf "$SRC/dav1d/build"
  (cd "$SRC/dav1d" && meson setup build --cross-file "$WORK/cross.ini" --prefix="$PREFIX" --libdir=lib \
      --default-library=static --buildtype=release -Denable_tools=false -Denable_tests=false \
   && ninja -C build install)
fi

echo "== ffmpeg"
# A shallow fetch has no tags, so FFmpeg would report a bare commit hash; name the release instead.
echo "${FFMPEG_TAG#n}" > "$SRC/ffmpeg/VERSION"
(cd "$SRC/ffmpeg" && ./configure \
    --prefix="$PREFIX" --arch=x86_64 --target-os=mingw32 --cross-prefix=$HOST- \
    --pkg-config=pkg-config --pkg-config-flags=--static \
    --extra-cflags="-I$PREFIX/include" --extra-ldflags="-L$PREFIX/lib -static" \
    --enable-gpl --enable-libx264 --enable-libmp3lame --enable-libdav1d \
    --disable-autodetect --enable-w32threads \
    --disable-shared --enable-static --disable-doc --disable-ffplay --disable-network \
    --disable-debug --enable-stripping \
 && make -j"$JOBS")

cp "$SRC/ffmpeg/ffmpeg.exe" "$SRC/ffmpeg/ffprobe.exe" "$WORK/dist/"
cp "$SRC/ffmpeg/COPYING.GPLv2" "$WORK/dist/FFmpeg-COPYING.GPLv2.txt"
cp "$SRC/ffmpeg/LICENSE.md" "$WORK/dist/FFmpeg-LICENSE.md"
cp "$SRC/x264/COPYING" "$WORK/dist/x264-COPYING.txt"
cp "$SRC/lame-3.100/COPYING" "$WORK/dist/LAME-COPYING.txt"
cp "$SRC/dav1d/COPYING" "$WORK/dist/dav1d-COPYING.txt"
{
  echo "Paradis Compress bundled FFmpeg build"
  echo "Built:    $(date -u +%Y-%m-%dT%H:%MZ) on $(uname -sr), $($HOST-gcc --version | head -1)"
  echo "FFmpeg:   $FFMPEG_TAG ($FFMPEG_COMMIT) https://github.com/FFmpeg/FFmpeg"
  echo "x264:     stable $X264_COMMIT https://github.com/mirror/x264 (mirror of code.videolan.org/videolan/x264)"
  echo "dav1d:    $DAV1D_TAG ($DAV1D_COMMIT) https://github.com/videolan/dav1d"
  echo "LAME:     3.100 sha256 $LAME_SHA256"
  echo "Reports:  \"ffmpeg version ${FFMPEG_TAG#n}\" (VERSION file written by this script)"
  echo "Licence:  the combined ffmpeg.exe / ffprobe.exe are GPL version 2 or later (--enable-gpl, libx264)."
  echo
  echo "configure line:"
  grep -m1 'FFMPEG_CONFIGURATION' "$SRC/ffmpeg/config.h"
} > "$WORK/dist/BUILDINFO.txt"
cp "$0" "$WORK/sources/build-ffmpeg-windows.sh"
(cd "$WORK/dist" && sha256sum ffmpeg.exe ffprobe.exe > SHA256SUMS.txt)
echo "== done: $WORK/dist"
