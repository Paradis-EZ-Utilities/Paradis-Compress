# Third-party software in Paradis Compress

Paradis Compress itself is MIT-licensed (see `LICENSE`). The Windows release bundles the
following. Full licence texts are in the `licenses` folder of the release.

## FFmpeg (ffmpeg.exe, ffprobe.exe), GPL version 2 or later

The `ffmpeg` folder holds a static FFmpeg 8.1.3 build made for this app by
`packaging/ffmpeg/build-ffmpeg-windows.sh`. Because it includes x264 and is configured with
`--enable-gpl`, the two executables as distributed are licensed under the **GNU General Public
License, version 2 or later**. Paradis Compress runs them as separate programs; it does not link
to them.

| Component | Version | Source | Licence |
|---|---|---|---|
| FFmpeg | n8.1.3, commit `1041abdc962f4cc4f394aa8de9dc5236c0c3b9e7` | https://github.com/FFmpeg/FFmpeg | LGPL 2.1+, GPL 2+ as built |
| x264 | stable, commit `31e19f92f00c7003fa115047ce50978bc98c3a0d` | https://code.videolan.org/videolan/x264 (fetched from the github.com/mirror/x264 mirror) | GPL 2+ |
| LAME | 3.100 (sha256 `ddfe36ca…a1e`) | https://lame.sourceforge.io | LGPL 2+ |
| dav1d | 1.5.4, commit `54706fc6bc0cdecab7e9593974a4039cc038fca7` | https://code.videolan.org/videolan/dav1d | BSD 2-clause |
| mingw-w64 CRT and GCC support code (statically linked) | Ubuntu 24.04 `mingw-w64` / `gcc-mingw-w64` 13 | https://www.mingw-w64.org | ZPL 2.1 and MIT-style; GCC parts under the GCC Runtime Library Exception. See `licenses/mingw-w64-copyright.txt` and `licenses/gcc-mingw-w64-copyright.txt` |

**Corresponding source.** `ParadisCompress-<version>-ffmpeg-source.zip`, published next to every
release, contains the exact source archives of all four components, the build script, and
`BUILDINFO.txt` with the full configure line. Running that script on those
archives rebuilds the executables. If that file is ever missing, ask the author for it: the offer
to provide source stands for at least three years from each release.

No zlib, OpenSSL, GnuTLS, fontconfig or other libraries are included (`--disable-autodetect`,
`--disable-network`).

## .NET runtime, MIT

The app is published self-contained, so `ParadisCompress.exe` includes the .NET 10 runtime and
WPF (Microsoft.NETCore.App and Microsoft.WindowsDesktop.App 10.0.12). Licensed under MIT by the
.NET Foundation; their third-party notices are in `licenses/dotnet-THIRD-PARTY-NOTICES.txt`.

## Code reused from Paradis Capture

The theme, logger and window conventions were adapted from Paradis Capture (same author, MIT).
