# Paradis Compress

**Video Compressor for Class Recordings**, part of **Paradis EZ Utilities**.

A small Windows app that makes long lecture and screen recordings much smaller while keeping
slides, code and speech clear. It uses FFmpeg (bundled) and never changes the original file.

- Drag and drop a video, or choose one. The app shows its length, resolution, frame rate and size
  on one line.
- A compact window (640 × 600 by default, resizable): output type, a preset list with an ⓘ help
  button, an optional **target size** row, save folder and file name. Advanced settings stay
  collapsed until you open them.
- Three presets, an optional **target file size** in decimal MB, and an **Extract audio (MP3)** mode.
- Progress across both passes of a two-pass encode, elapsed time, an estimate of time left, and a
  safe Cancel.
- A compact result: size change, elapsed time, **Open File**, **Show in Folder**, and **Details**
  with exact byte counts. If the new file isn't smaller, or is above the target, it says so.

## Running it

1. Unzip `ParadisCompress-1.0.0-win-x64.zip` anywhere and keep the `ffmpeg` folder next to
   `ParadisCompress.exe`.
2. Double-click `ParadisCompress.exe`. Nothing is installed; there is no separate .NET to install.
3. The build is not code-signed, so Windows SmartScreen may say "Windows protected your PC".
   Click **More info**, then **Run anyway**.

Requires 64-bit Windows 10 or 11.

## Presets

All presets write MP4 (H.264 video, AAC audio, `+faststart`) with x264 in software. None of them
ever upscales, crops, or raises the frame rate. Aspect ratio is kept, and rotated phone videos
are turned upright.

| | Class Recording (default) | Balanced | Smallest File |
|---|---|---|---|
| Resolution limit (shorter side) | keep original | 1080 | 720 |
| Frame rate limit | 30 fps | 30 fps | 15 fps |
| Quality (x264 CRF) | 23 | 26 | 30 |
| x264 preset | medium | medium | slow |
| Keyframe every | 10 s | 5 s | 10 s |
| Audio (AAC) | 96 kbit/s stereo, 64 mono | 96 kbit/s stereo, 64 mono | 64 kbit/s stereo, 40 mono |

A frame-rate limit applies only when the source is more than 5 % above it. A recording with a
variable frame rate below the limit keeps its own frame timing (so it stays in sync). Mono
sources stay mono; nothing is mixed down unless you tick **Mix to mono**. More than two channels
become stereo, and sample rates above 48 kHz become 48 kHz.

**Advanced settings** let you override the resolution limit, the frame-rate limit and the
quality (CRF) for one job, and mix to mono. The line under them spells out exactly what will be
created.

## Target file size

Tick **Target size** and enter a number of MB. The ⓘ button next to it explains the units, the
two passes and the limits of size targeting; the line under it says what the app will aim for, or
warns when the target is too small or not smaller than the original.

- **Decimal units (since rc.2): 1 MB = 1,000,000 bytes**, the way upload limits are normally
  stated. A 500 MB target means the file may be at most 500,000,000 bytes. Every size the app shows
  is decimal too (KB = 1,000 bytes, GB = 1,000,000,000 bytes). Windows File Explorer counts in
  binary units of 1,048,576 bytes but also calls them "MB", so it shows the same file about 4.6 %
  smaller; where the app mentions binary units it labels them MiB.
  rc.1 used binary MB: a "500 MB" target produced 503,362,712 bytes, under 500 MiB but over a
  500,000,000-byte limit. rc.2 treats that result as over the target.
- The app runs a two-pass encode aimed at 96 % of the target, with the audio and the MP4
  container overhead budgeted first. If x264 still overshoots (it does on near-static screen
  content), the second pass is repeated at a lower bitrate, up to three times. The progress bar
  steps back once and says "fitting the target size" when that happens.
- **The finished file's exact byte count is compared with the target before success is reported.**
  If it is still above the target, the result says "Not within your N MB target: over by …" in red,
  never "Done", and offers **Retry smaller**, which compresses again at a bitrate lowered by the
  measured overshoot plus 5 % (or the next lower MP3 bitrate) and replaces the over-target file
  only once the new one is finished and checked.
- When the bitrate would be very low for the resolution and length, the line under the target
  warns and suggests a size, and starting asks for confirmation if text would probably be
  unreadable. A target too small to hold even the audio is refused.
- This is approximate size targeting: meeting a size can't promise that a website or service
  will accept the file, and the app doesn't claim to.

## Extract audio

Saves only the sound as a constant-bitrate MP3 (LAME): 96 kbit/s stereo or 64 kbit/s mono by
default, which is plenty for speech. Slides and anything on screen are not included, and the app
says so. Video presets and video-only advanced settings are hidden in this mode. The MP3 quality
can be chosen under Advanced. A target size works here too: it picks the constant MP3 bitrate that
fits ("MP3 lowered to 64 kbit/s to fit"), and can only lower the bitrate, never raise it.

## Safety

- The original is opened read-only and never written. The output can't be the input: the app
  compares the actual files (volume serial number and file ID on Windows), so other spellings,
  a different case, `..`, symbolic links or hard links to the same file are all caught.
- An existing output file is never replaced without asking (**Keep both** / **Replace it** /
  **Cancel**). Replacing happens only after the new file is finished and checked.
- The video is first written to `name.partial-XXXXXXXX.mp4` in the destination folder, checked
  with ffprobe (streams, codecs, size, duration) and a decode of its start and end, and only then
  renamed. On failure or Cancel the partial file and the two-pass logs are deleted.
- FFmpeg is started with an argument list (never through a shell) and every path is passed as a
  `file:` URL, so names with spaces, accents, CJK characters, `&`, `%`, `$`, `#`, quotes or brackets are
  safe. The app only ever stops processes it started itself, and a Windows Job Object makes sure
  they end if the app is closed or crashes.
- The log (`%LocalAppData%\ParadisCompress\Logs`, opened from **Log folder**) records settings,
  file sizes, FFmpeg command lines and errors. It never contains video or audio content.

## Bundled FFmpeg

`ffmpeg\ffmpeg.exe` and `ffprobe.exe` are a static FFmpeg 8.1.3 build with x264, LAME and dav1d
(for AV1 input), made by `packaging/ffmpeg/build-ffmpeg-windows.sh` from pinned commits. That
combination is **GPL version 2 or later**. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
The exact source is published with each release as `ParadisCompress-<version>-ffmpeg-source.zip`.
`ffmpeg\BUILDINFO.txt` has the versions, commits and configure line, and `ffmpeg\SHA256SUMS.txt`
has the checksums.

The app looks for FFmpeg in `PARADIS_FFMPEG_DIR`, then `<app>\ffmpeg\`, then next to the exe,
then `PATH`.

## Building

Everything builds on Linux (that is how the 1.0.0 release was built) or on Windows.

```sh
# 1. FFmpeg for Windows (Ubuntu 24.04: apt install mingw-w64 nasm meson ninja-build pkg-config make git)
packaging/ffmpeg/build-ffmpeg-windows.sh ~/ffbuild           # -> ~/ffbuild/dist, ~/ffbuild/sources

# 2. The app (.NET 10 SDK). A single self-contained exe:
dotnet publish src/ParadisCompress -p:PublishProfile=win-x64-self-contained   # -> publish/win-x64/

# 3. The release zips (app, FFmpeg source, app source) and checksums:
packaging/package-windows.sh ~/ffbuild                      # -> out/
```

On Windows, step 2 is the same command; step 1 needs Linux or WSL.

The version is defined in one place, `VersionPrefix` and `VersionSuffix` in
[Directory.Build.props](Directory.Build.props). The window footer, About, the log, the exe's file
and product version, and the release folder and ZIP names all come from it; step 3 refuses to
package an exe that was published with a different version.

Builds are reproducible: with the same .NET SDK (10.0.112 for 1.0.0), publishing the source ZIP in
any folder gives a `ParadisCompress.exe` byte-identical to the one in the release ZIP.

## Tests

```sh
dotnet test tests/ParadisCompress.Core.Tests                 # unit tests, no FFmpeg needed

# End-to-end tests against real FFmpeg. Fixtures are generated on first run (needs a full ffmpeg
# with drawtext and flite on PATH, e.g. Ubuntu's ffmpeg package, to create them).
packaging/ffmpeg/build-ffmpeg-linux-for-tests.sh ~/ffbuild   # same sources and options, native
PARADIS_FFMPEG_DIR=~/ffbuild/linux/bin tests/run-integration-tests.sh --filter "Category!=Long"
PARADIS_FFMPEG_DIR=~/ffbuild/linux/bin tests/run-integration-tests.sh --filter "Category=Long"   # 1 h recording
```

As root, the script also mounts a tiny and a read-only tmpfs for the disk-full and
unwritable-folder tests. Frames for visual inspection, metrics and a `results.jsonl` go to
`PARADIS_TEST_ARTIFACTS`. See [CHANGELOG.md](CHANGELOG.md) for what changed in each version, [TEST_REPORT.md](TEST_REPORT.md) for the latest results and
[ACCEPTANCE_CHECKLIST.md](ACCEPTANCE_CHECKLIST.md) for the manual checks on Windows.

## Project layout

```
src/ParadisCompress.Core     platform-neutral engine: probing, planning, FFmpeg arguments, jobs, safety
src/ParadisCompress          WPF app (theme and logger adapted from Paradis Capture)
tests/...Core.Tests          unit tests
tests/...IntegrationTests    end-to-end tests with generated fixtures
packaging/                   FFmpeg build scripts, release packaging, end-user README.txt
```

## Limitations

- Windows only, x64. Software encoding only (no GPU). Speed depends on the CPU: on the 4-core
  build machine a 40 s 1080p screen recording took about 8 s with Class Recording, and a 1 h
  360p recording (665 MB) about 2 minutes. Camera footage and two-pass targets take longer.
- Only the first audio track is kept; subtitles, chapters and data tracks are dropped.
- Odd widths or heights get one black pixel column or row so H.264 can encode them; nothing is
  cropped.
- HDR sources are converted to 8-bit without tone mapping, so colours may look washed out (not
  tested).
- Speech is encoded with AAC/MP3 at the bitrates above; no noise reduction or loudness changes.

## Licence

MIT for Paradis Compress itself (see [LICENSE](LICENSE)). The bundled FFmpeg is GPL v2 or later.
