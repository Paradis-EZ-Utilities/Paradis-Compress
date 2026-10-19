#!/usr/bin/env bash
# Assembles the Windows release from already-built pieces:
#
#   publish/win-x64/ParadisCompress.exe      dotnet publish src/ParadisCompress -p:PublishProfile=win-x64-self-contained
#   $FFMPEG_WORK/dist, $FFMPEG_WORK/sources  packaging/ffmpeg/build-ffmpeg-windows.sh $FFMPEG_WORK
#
# Output in out/:
#   ParadisCompress-<ver>-win-x64.zip            the app (unzip anywhere, run ParadisCompress.exe)
#   ParadisCompress-<ver>-ffmpeg-source.zip      GPL corresponding source for the bundled FFmpeg
#   ParadisCompress-<ver>-source.zip             the app's own source (git HEAD)
#   SHA256SUMS.txt
#
# Usage: packaging/package-windows.sh <ffmpeg-work-dir>
set -euo pipefail
cd "$(dirname "$0")/.."
FFMPEG_WORK=${1:?usage: package-windows.sh <ffmpeg-work-dir>}
# The version comes from Directory.Build.props (the only place it is defined), evaluated by MSBuild.
VER=$(dotnet msbuild src/ParadisCompress -getProperty:Version)
[[ $VER =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$ ]] || { echo "unexpected version '$VER'" >&2; exit 1; }
NAME=ParadisCompress-$VER-win-x64
OUT=out
STAGE=$OUT/$NAME
NUGET=${NUGET_PACKAGES:-$HOME/.nuget/packages}
RT=$(ls -d "$NUGET"/microsoft.netcore.app.runtime.win-x64/10.* | sort -V | tail -1)
WD=$(ls -d "$NUGET"/microsoft.windowsdesktop.app.runtime.win-x64/10.* | sort -V | tail -1)

test -f publish/win-x64/ParadisCompress.exe || { echo "publish the app first" >&2; exit 1; }
# The published exe must be a build of this version: its Product version resource is exactly $VER.
python3 -c 'import sys; d=open(sys.argv[1],"rb").read(); sys.exit(("ProductVersion\0".encode("utf-16-le") not in d) or ((sys.argv[2]+"\0").encode("utf-16-le") not in d))' \
  publish/win-x64/ParadisCompress.exe "$VER" \
  || { echo "publish/win-x64/ParadisCompress.exe is not a $VER build; publish again" >&2; exit 1; }
for f in ffmpeg.exe ffprobe.exe BUILDINFO.txt SHA256SUMS.txt; do test -f "$FFMPEG_WORK/dist/$f"; done
(cd "$FFMPEG_WORK/dist" && sha256sum -c --quiet SHA256SUMS.txt)

rm -rf "$STAGE" && mkdir -p "$STAGE/ffmpeg" "$STAGE/licenses"
cp publish/win-x64/ParadisCompress.exe "$STAGE/"
cp "$FFMPEG_WORK"/dist/{ffmpeg.exe,ffprobe.exe,BUILDINFO.txt,SHA256SUMS.txt} "$STAGE/ffmpeg/"

L=$STAGE/licenses
cp LICENSE "$L/ParadisCompress-LICENSE.txt"
cp THIRD_PARTY_NOTICES.md "$L/THIRD_PARTY_NOTICES.md"
cp "$FFMPEG_WORK/dist/FFmpeg-COPYING.GPLv2.txt" "$FFMPEG_WORK/dist/FFmpeg-LICENSE.md" \
   "$FFMPEG_WORK/dist/x264-COPYING.txt" "$FFMPEG_WORK/dist/LAME-COPYING.txt" "$FFMPEG_WORK/dist/dav1d-COPYING.txt" "$L/"
cp /usr/share/doc/mingw-w64-common/copyright "$L/mingw-w64-copyright.txt"
cp /usr/share/doc/gcc-mingw-w64-base/copyright "$L/gcc-mingw-w64-copyright.txt"
cp "$RT/LICENSE.TXT" "$L/dotnet-LICENSE.txt"
cp "$RT/THIRD-PARTY-NOTICES.TXT" "$L/dotnet-THIRD-PARTY-NOTICES.txt"
cp "$WD/LICENSE" "$L/dotnet-windowsdesktop-LICENSE.txt"
sed "s/@VERSION@/$VER/g" packaging/README.txt > "$STAGE/README.txt"
# Windows users open these in Notepad.
for f in "$STAGE/README.txt" "$L"/*.txt "$L"/*.md "$STAGE/ffmpeg/BUILDINFO.txt"; do sed -i 's/\r\?$/\r/' "$f"; done

# Corresponding source for the GPL FFmpeg build.
SRCSTAGE=$OUT/ParadisCompress-$VER-ffmpeg-source
rm -rf "$SRCSTAGE" && mkdir -p "$SRCSTAGE"
cp "$FFMPEG_WORK"/sources/*.tar.gz "$SRCSTAGE/"
cp packaging/ffmpeg/build-ffmpeg-windows.sh packaging/ffmpeg/build-ffmpeg-linux-for-tests.sh "$FFMPEG_WORK/dist/BUILDINFO.txt" "$SRCSTAGE/"
cat > "$SRCSTAGE/README.txt" <<EOF
Corresponding source for the FFmpeg build bundled with Paradis Compress $VER.

ffmpeg.exe and ffprobe.exe in the release were built by build-ffmpeg-windows.sh from exactly
these archives (FFmpeg, x264, dav1d, LAME). BUILDINFO.txt records the versions, commits and the
full configure line. To rebuild on Ubuntu 24.04:

  sudo apt install mingw-w64 nasm meson ninja-build pkg-config make git
  ./build-ffmpeg-windows.sh <empty work folder>

The script fetches the same pinned commits from the internet. To build from these archives
instead (offline), copy the *.tar.gz files into <work folder>/sources/ before running it.
EOF

rm -f "$OUT/$NAME.zip" "$OUT/ParadisCompress-$VER-ffmpeg-source.zip" "$OUT/ParadisCompress-$VER-source.zip"
(cd "$OUT" && zip -qrX9 "$NAME.zip" "$NAME" && zip -qrX "ParadisCompress-$VER-ffmpeg-source.zip" "ParadisCompress-$VER-ffmpeg-source")
git archive --format=zip --prefix="ParadisCompress-$VER-source/" -o "$OUT/ParadisCompress-$VER-source.zip" HEAD
(cd "$OUT" && sha256sum ./*.zip | sed 's: \./: :' > SHA256SUMS.txt && cat SHA256SUMS.txt)
ls -l "$OUT"
