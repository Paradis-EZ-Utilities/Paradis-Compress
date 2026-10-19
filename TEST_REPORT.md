# Test report: Paradis Compress 1.0.0

Date: 2026-10-15. 1.0.0 is the first public release. It is **1.0.0-rc.2 with only the version
changed**: same app code, same bundled `ffmpeg.exe`/`ffprobe.exe` (FFmpeg 8.1.3, SHA-256 checked
against `ffmpeg\SHA256SUMS.txt`), same .NET 10.0.12 runtime (SDK 10.0.112). Built on Linux; checked
on Linux and under Wine 9 + Xvfb. **Nothing was run on real Windows.**

## What changed in 1.0.0

- The version is defined once (`VersionPrefix`/`VersionSuffix` in `Directory.Build.props`). Version,
  InformationalVersion, AssemblyVersion and FileVersion are derived from it; `package-windows.sh` reads
  it through MSBuild instead of parsing the file, and refuses to package an exe whose Product version
  is not that version (tested: packaging with the rc.2 exe in `publish/win-x64` stops with "is not a
  1.0.0 build").
- The build is reproducible: `PathMap` and `EnableSourceControlManagerQueries=false` make the same
  source give a byte-identical `ParadisCompress.exe` in any folder, git checkout or not.

## Checks (1.0.0)

| Check | Result |
|---|---|
| Unit tests | **107 / 107 passed** |
| Code identical to rc.2 | rc.2 source (+ the same `PathMap`) and 1.0.0 built side by side: every method body and string literal in `ParadisCompress.dll` and `ParadisCompress.Core.dll` is identical; the other 226 files in the single-file bundle (runtime, WPF) are byte-identical. Only the version attributes and embedded debug info differ. |
| Same output as rc.2 | The packaged 1.0.0 and rc.2 exes compressed the same 8 s 720p clip under Wine with Class Recording: both outputs 2,670,465 bytes, **identical SHA-256** |
| Package contents | Compared file by file with the rc.2 ZIP: only `ParadisCompress.exe` and `README.txt` (version line, FFmpeg source ZIP name) differ. FFmpeg source archives in the FFmpeg source ZIP are byte-identical to rc.1/rc.2. |
| Source ZIP builds the release exe | `ParadisCompress-1.0.0-source.zip` unzipped to a fresh folder and published with the README's command: `ParadisCompress.exe` byte-identical to the one in the win-x64 ZIP (SHA-256 below) |
| Window footer (Wine) | **v1.0.0** |
| About (Wine) | **Version 1.0.0** · Video Compressor for Class Recordings · © 2026 D.Y. Paradis; "ffmpeg version 8.1.3" |
| Startup log (Wine) | `Paradis Compress starting (v1.0.0) on Microsoft Windows NT 10.0.19043.0, .NET 10.0.12` |
| File metadata | Read with `GetFileVersionInfo` under Wine and parsed with pefile: File version **1.0.0.0**, Product version **1.0.0**, Product name Paradis Compress, Company D.Y. Paradis (rc.2 exe for comparison: 1.0.0.0 / 1.0.0-rc.2) |

`ParadisCompress.exe` SHA-256: `2caf226160201a0fa82ff86c78b39716619d94567d46ca6d6b520bd8233323ba`

Not verified: Explorer's Properties → Details tab itself, SmartScreen, and everything in
section 3 below, which still applies unchanged to 1.0.0. Under Wine's substitute fonts the About
line is cut off after "© 2026 D.Y."; this is the same layout as rc.2 and should be checked with
real Segoe UI.

Screenshots and logs: `test-evidence/1.0.0/`.

---

# rc.2 test report (2026-10-15, kept for reference)

Date: 2026-10-15. Same build machine and FFmpeg as rc.1 (Linux x86-64, 4 cores, .NET SDK 10.0.112,
FFmpeg 8.1.3 built natively from the rc.1 FFmpeg source zip for the automated tests). **The Windows
`ffmpeg.exe`/`ffprobe.exe` in the package are the rc.1 files, unchanged** (SHA-256 checked against
`ffmpeg\SHA256SUMS.txt`), so the FFmpeg source zip is the same content under the rc.2 name.

**Everything here ran on Linux, or on Linux under Wine 9 + Xvfb. Nothing was run on real Windows.**

## What changed in rc.2

- **Compact window** (640 × 600 logical px by default, resizable, minimum 540 × 460, clamped to the
  screen's work area): one-line header, one-line input file area with Change… (the whole window
  still accepts a dropped file), a two-option Make selector, a preset dropdown with an ⓘ help
  button, the target size on one row (checkbox, number, MB, ⓘ) with one short status line,
  compact Save to / File name, Advanced collapsed on every launch (scrolls inside when open), and an
  action area that never scrolls away. The result is one headline, one line, and Open File /
  Show in Folder / Details (plus Retry smaller when over target). Footer: About, Log folder,
  **More from Paradis EZ Utilities**, version.
- **Help popovers** open by click, Enter or Space (never hover), close with Esc, ✕ or a click
  elsewhere, return focus to their button, stay inside the window (clamped, scroll if needed), and
  have accessible names ("Help: presets", "Help: target size"). Footer links are now real buttons
  reachable with Tab.
- **Decimal MB**: 1 MB = 1,000,000 bytes for targets and every displayed size. Binary units, where
  mentioned, are labelled MiB.
- **Truthful size result**: the finished file's byte count is compared with the target in exact
  bytes; above it, the result reads "Not within your N MB target: over by … bytes" in red, never
  "Done", and offers **Retry smaller**.
- Audio-only hides the presets and video-only advanced controls and says what a target does to the
  MP3 ("MP3 lowered to 40 kbit/s to fit").
- Errors: the window returns to its idle state before the error dialog appears; a stale
  "Cancelled"/"didn't finish" line clears when settings change. Messages no longer suggest
  Extract audio for a video with no sound.
- Build: the publish profile was missing from the rc.1 source zip (`*.pubxml` was git-ignored), so
  the README's publish command failed from that zip. It is now included.

## 1. Automated checks (rc.2)

| Suite | Result |
|---|---|
| Unit tests (`tests/ParadisCompress.Core.Tests`) | **107 / 107 passed** (86 from rc.1, updated for decimal units, + 21 new) |
| Integration tests, real FFmpeg 8.1.3 (`Category!=Long`) | **55 / 55 passed** (52 from rc.1 + 3 new target cases), 9.0 min |
| One-hour recording (`Category=Long`) | **1 / 1 passed**: 666 MB → 243 MB in 182 s, duration difference 0 ms, all 42 sync markers at 0 ms |

New and changed unit tests cover: 1 / 500 / 99.5 / 0.29 / 25 / 2000 MB → exact bytes (0.29 MB is
290,000, not 289,999); decimal `FormatSize`; `FormatSizePrecise` never rounding up past a limit
(503,362,712 bytes → "503.3 MB"); a 500 MB target = 500,000,000 bytes with the plan aiming at
480,000,000 and budgeting video + audio + container under it; the rc.1 result of 503,362,712 bytes
now counting as **above** a 500 MB target while exactly 500,000,000 does not; Retry smaller giving a
lower video bitrate (or the next lower MP3 bitrate) and never a higher one; every target verdict
having a short message of at most 80 characters.

### Target size, actual bytes (integration tests, FFmpeg 8.1.3; requirement: output bytes ≤ target bytes)

The test now asserts `outputBytes <= targetMB × 1,000,000` exactly (rc.1 allowed +1 %).

| Fixture, preset | Target | Target bytes | Output bytes | Of target | Pass-2 re-runs |
|---|---|---|---|---|---|
| class_code 1080p, Class Recording | 5 MB | 5,000,000 | 4,793,141 | 95.9 % | 1 |
| class_code 1080p, Class Recording | 3.5 MB | 3,500,000 | 3,343,106 | 95.5 % | 1 |
| class_code 1080p, Class Recording | 2 MB | 2,000,000 | 1,942,210 | 97.1 % | 1 |
| motion 720p, Class Recording | 4 MB | 4,000,000 | 3,864,355 | 96.6 % | 0 |
| motion 720p, Smallest File | 4 MB | 4,000,000 | 3,731,648 | 93.3 % | 0 |
| motion 720p, Balanced | 2.5 MB | 2,500,000 | 2,407,283 | 96.3 % | 0 |
| hfr 720p60, Balanced | 3 MB | 3,000,000 | 2,715,502 | 90.5 % | 0 |
| noaudio 720p, Class Recording | 1 MB | 1,000,000 | 966,920 | 96.7 % | 0 |
| slides 720p, Class Recording | 1.5 MB | 1,500,000 | 672,894 | 44.9 % | 0 (static slides need less) |
| slides 720p, Smallest File (too-low verdict) | 0.75 MB | 750,000 | 566,971 | 75.6 % | 0 |
| audio only, class_code | 0.25 MB | 250,000 | ≤ 250,000 (asserted) | | n/a |

The near-static code recording still makes x264 overshoot on the first try; the existing
corrective re-run brought every case under the target. Other target checks still pass: impossible
targets are refused before encoding, progress is ordered, temp files and two-pass logs are gone.

## 2. GUI checks under Wine (rc.2)

The real published `ParadisCompress.exe` with the bundled Windows `ffmpeg.exe`, fonts substituted
(DejaVu Sans stands in for Segoe UI and is wider, so text has less room than on Windows).
Screenshots are in `test-evidence/rc.2/gui-wine/`.

| State | Checked |
|---|---|
| Idle, file loaded (100 %) | Everything from the file line to COMPRESS and the footer visible without scrolling; summary line "MP4 · 1920×1080 · 30 fps · CRF 23" |
| Target 2 MB | "Aims for about 1.9 MB, two-pass." under the row |
| Target 0.3 MB / 0.03 MB | Red "Very small for 1920×1080: text will likely blur. About 2 MB is clearer."; Compress asks "That target is very small" |
| Preset ⓘ, Target ⓘ (mouse) | Popovers open inside the window, scroll if long, ⓘ shows as open |
| Keyboard | Esc closes and returns focus to ⓘ; Tab, Tab reaches the target ⓘ; Space opens it |
| Advanced open | Panel opens below; the settings area scrolls; action area stays fixed |
| Audio only | Presets and video advanced controls hidden; "Sound only…" line; target line "Fits at the chosen MP3 bitrate, 96 kbit/s: about 241 KB" / "MP3 lowered to 40 kbit/s to fit"; a 0.12 MB target gave 100,540 bytes |
| Processing | "Pass 1 of 2: analysing the video", then "Pass 2 of 2: compressing", %, elapsed, time left, Cancel |
| Completion | "Done: 26 MB → 1.92 MB (92% smaller)", "Within the 2 MB target (1,925,099 bytes). Took 0:15."; Details popover lists path, exact bytes, target check, settings used |
| Over target | Forced with a test-only ffmpeg wrapper that pads each finished MP4 with a valid `free` box: 3 corrective re-runs, then red "Not within your 2 MB target: over by 3,464 bytes", "The file is 2,003,464 bytes", Retry smaller. With the padding switched off, Retry smaller replaced that file with 1,607,358 bytes, "Within the 2 MB target" |
| Error | FFmpeg failing: window back to idle, red status line, then the error dialog |
| Cancel | During pass 1: "Cancelled. Nothing was saved…"; no partial file or temp folder left |
| 125 % and 150 % scaling (Wine LogPixels 120/144) | Window 792 × 710 and 950 × 851 physical px; nothing clipped; at 150 % with Advanced open and a result shown, the settings scroll and the action area stays visible |
| Small screen (912 × 512 px work area, like 1366 × 768 at 150 %) | Window clamped to the screen (632 × 462 client); the basic workflow still fits without scrolling |

The final `ParadisCompress-1.0.0-rc.2-win-x64.zip` was unzipped to a fresh folder and run on a file
named `Cours – été 😀.mp4` with a 2 MB target: 1,925,099 bytes, within the target. The rc.2 source
zip was unzipped to a fresh folder, published with the README's command, and its unit tests passed.

A real 20 s 1080p screen-like clip at a 0.3 MB target came out at 327,122 bytes on the first try
(109 %); the corrective re-run gave 287,357 bytes, under the target, and the result said so.

## 3. Not performed (rc.2) — needs your manual testing on Windows

- Everything in rc.1's list below still applies (clean machine, SmartScreen, Defender, drag and
  drop, native dialogs, Open File / Show in Folder, real recordings).
- The compact layout with **real Segoe UI at 100 %, 125 % and 150 %**, and resizing the window by
  its edges (Wine without a window manager can't test the minimum size).
- **Narrator** reading the ⓘ buttons, popovers and the result; keyboard-only use end to end.
- Uploading a result to a real size-limited site.
- A long real recording with a large target (for example 500 MB) to see the actual byte margin.

---

# rc.1 test report (2026-10-07, kept for reference)

Date: 2026-10-07. Build machine: Linux x86-64, 4 cores, .NET SDK 10.0, FFmpeg 8.1.3 built from the
same pinned sources and options as the bundled Windows build (native Linux build for the
automated tests; the Windows `ffmpeg.exe` was exercised separately under Wine).

**Everything here ran on Linux, or on Linux under Wine. Nothing was run on real Windows.**
Section 4 lists what that leaves unverified.

### 1. Automated checks

| Suite | Result |
|---|---|
| Unit tests (`tests/ParadisCompress.Core.Tests`): probe parsing, planning, presets, naming, units, errors, path safety | **86 / 86 passed** |
| Integration tests (`tests/ParadisCompress.IntegrationTests`, real FFmpeg, generated fixtures) | **52 / 52 passed** |
| One-hour recording (`Category=Long`) | **1 / 1 passed** |

The first full integration run found three failures, all fixed before the final run:

- **Target size overshoot.** x264's two-pass rate control came out 7 % and 21 % *over* target on
  the near-static 1080p code recording. The app now re-runs pass 2 at a corrected bitrate when
  the file is too big (one re-run fixed both cases).
- **Odd-size check.** The test itself cropped an odd width in 4:2:0. Fixed in the test.
- **Odd-size fixture.** An earlier version came out at 640x360, because the generator rounded
  odd sizes. The fixture was regenerated at a true 641x361.

### Fixtures (generated by `Fixtures.cs`, cached between runs)

| Fixture | What it stands for |
|---|---|
| class_code_1080p30 (40 s) | IDE with 18 px code plus a 13 px terminal, scrolling, a cursor, spoken audio (flite TTS) |
| slides_720p25 (40 s) | Slide deck with 26 px text and maths symbols, stereo speech |
| precompressed_slides_720p25 | An already heavily compressed file (CRF 32 veryslow, 48 kbit/s) |
| mono_speech_640x360 | Mono speech |
| motion_720p30 (20 s) | Camera-like full-frame motion and noise (132 MB source) |
| sync_640x360_30, hfr_1280x720_60 | White flash and 1 kHz beep every 2 s, for A/V sync |
| vfr_low_sync, vfr_high_sync | Variable frame rate (about 24 and 51 fps average on a 60 fps base) with sync markers |
| noaudio_720p30, surround_51.mkv, odd_641x361.mkv (FFV1 4:4:4, red 1 px border), rotated_90 (display matrix) | Edge cases |
| long_60min_360p15 | 1 hour with sync markers throughout (665 MB) |
| notvideo, empty, truncated (moov missing), picture.png | Invalid inputs |

### Compression with the default preset (Class Recording)

| Fixture | Source → output | Change | Notes |
|---|---|---|---|
| class_code_1080p30 | 4.32 MB → 2.12 MB | −51 % | 8.9 s for 40 s of 1080p |
| motion_720p30 | 126 MB → 10.2 MB | −92 % | |
| hfr_1280x720_60 | 11.8 MB → 4.95 MB | −58 % | 60 → 30 fps |
| vfr_high_sync (51 fps avg) | 9.30 MB → 4.22 MB | −55 % | capped to constant 30 fps |
| vfr_low_sync (24 fps avg) | 5.84 MB → 3.50 MB | −40 % | frame timing kept (24.1 fps avg) |
| slides_720p25 | 0.97 MB → 0.65 MB | −34 % | |
| rotated_90 | 3.99 MB → 2.55 MB | −36 % | output 720x1280 upright, SSIM 0.9955 against the source as players show it |
| odd_641x361 | 1.46 MB → 0.10 MB | −93 % | 642x362, one black column and row added; cropped back, SSIM 0.971 against the source (4:2:0 chroma) |
| surround_51 | 0.67 MB → 0.44 MB | −34 % | 5.1 → stereo |
| noaudio_720p30 | 5.02 MB → 3.08 MB | −39 % | no audio stream, no error |
| mono_speech | 235 KB → 170 KB | −28 % | stays mono, 64 kbit/s |
| precompressed_slides | 379 KB → 494 KB | **+30 %** | reported as "not smaller"; the original is kept |

Every output in every test:

- passes ffprobe validation
- decodes start to end with `-xerror` and no errors
- keeps its duration within 20 ms of the source (1 h file: 0 ms)

### A/V sync

A white square flashes and a 1 kHz beep starts every 2 s, both on the same frame boundary in the
source. Each output was measured with signalstats and silencedetect. The audio is sample-accurate
and the video is accurate to one frame, so a one-frame drift would show as 33 ms (67 ms at
15 fps).

- **sync_640x360_30, hfr 60 → 30 fps, vfr_low, vfr_high → 30 fps:** every marker at **0 ms**. The
  vfr_high source itself has one marker at −16 ms; the output has none.
- **1 hour recording:** 665 MB → 243 MB in 130 s. Duration difference 0 ms. All 42 markers
  measured (14 at the start, 14 in the middle, 14 at the end) are at **0 ms**.

### Target file size (1 MB = 1,048,576 bytes; requirement: never above the target, normally 85–100 %)

| Fixture, preset | Target | Result | Re-runs of pass 2 |
|---|---|---|---|
| class_code 1080p, Class Recording | 5 MB | 96.2 % | 1 (first try 121 %) |
| class_code 1080p, Class Recording | 2 MB | 97.1 % | 1 (first try 107 %) |
| motion 720p, Class Recording | 4 MB | 96.6 % | 0 |
| motion 720p, Smallest File | 4 MB | 94.0 % | 0 |
| hfr 720p60, Balanced | 3 MB | 90.8 % | 0 |
| noaudio 720p | 1 MB | 97.2 % | 0 |
| slides 720p | 1.5 MB | 45.3 % | 0 (static slides need far less, as documented) |
| audio only, class_code | 0.25 MB | 240,532 bytes (48 kbit/s chosen) | n/a |

Other target-size checks:

- A 0.1 MB target for 40 s of 1080p is refused before encoding, and no file is created.
- Progress reports both passes in order.
- Two-pass logs and temporary folders are gone after every run.

### Extract audio (MP3, LAME CBR)

| Source | Result |
|---|---|
| class_code (stereo) | 96.2 kbit/s, 2 channels |
| class_code with Mix to mono | 64.1 kbit/s, 1 channel |
| mono speech | 64.3 kbit/s, 1 channel (mono is not upmixed) |
| 5.1 | 96.5 kbit/s, stereo |
| 44.1 kHz FLAC | 44.1 kHz kept |

All results:

- are within 20 ms of the source duration
- decode cleanly

A silent video gives the message "There's no audio track in this file, so there's nothing to extract."

### Safety and reliability

| Check | Result |
|---|---|
| Paths with spaces, ç é – 出力 一時, `& ' " $HOME %TEMP% ;(1)! # [ ] %d %03d $(echo hi)`; single pass, audio, and two-pass with TMPDIR itself on such a path | Pass. Exact names were produced, and no temp files are left. |
| Output = input, spelled 6 ways (same path, `./`, `sub/../`, no extension, symlink to the file, path through a symlinked folder) | Refused every time. The source's SHA-256 is unchanged. |
| Existing output | Not replaced unless confirmed. With Replace, it is swapped only after validation. |
| Cancel in a single pass, in pass 1 and in pass 2 | Returned within 47–49 ms. No output, partial or log files are left, and no ffmpeg process remains. |
| Read-only destination | "The file couldn't be saved…". Nothing is left. |
| Disk full (3 MB tmpfs) | "Not enough disk space… Nothing was saved." The partial file is removed. |
| Missing input; empty, truncated or non-video file; a PNG | A plain-language title and message, with no "moov"/"ffprobe" jargon in the message. |
| Six jobs in a row in one process (video, audio, target, name collision) | All correct; the collision becomes "a (2).mp4". |
| Progress | 32 updates in a 9 s job and never goes backwards (only a corrective re-run adds a pass and steps back once). The time-left estimate at 25 % was 13.4 s against an actual 9.1 s. |

### Packaging

- The published `ParadisCompress-1.0.0-rc.1-ffmpeg-source.zip` was unpacked into an empty folder,
  and `build-ffmpeg-windows.sh` was run offline from its archives. It built `ffmpeg.exe` and
  `ffprobe.exe` reporting version 8.1.3, the same size as the shipped files. They are not
  byte-identical, because the build is not set up to be reproducible.
- The packaged `win-x64.zip` was unzipped to a fresh folder and run under Wine on a VFR file whose
  name contains an emoji. The output was 4,422,132 bytes, exactly the size of the Linux test
  build's output for the same file.

### 2. Visual checks (done by looking at the frames, not by a metric)

The integration tests save matching source and output frames at 5, 15, 25 and 35 s, plus 1:1
crops of the smallest text (`$PARADIS_TEST_ARTIFACTS/frames`). These were inspected by eye:

- **Class Recording, Balanced, and a 2 MB target on the 1080p code video:** the 13 px terminal text
  and the 18 px code, including `l1I|0O8B`, look the same as the source. There are no visible
  artifacts. SSIM was 0.9995, 0.9991 and 0.9990.
- **Smallest File (1080p → 720p, 15 fps):** the code and terminal are still readable, with
  visibly softer edges and slight ringing around the glyphs. `l1I|0O8B` are still
  distinguishable. SSIM 0.980. This matches the preset's own warning.
- **Slides at Smallest File (720p kept):** 26 px text and Θ/· symbols are crisp.
- **Rotated:** upright, and the same as the source as displayed.
- **Odd size:** the 1 px red border survives on all four sides. The added black column and row
  make the edge pixel darker-red through chroma subsampling. Nothing is cropped.

The GUI was driven under **Wine 9 on a virtual X display** (Xvfb), not on Windows, using the
real published `ParadisCompress.exe` and the bundled Windows `ffmpeg.exe`. Screenshots were
checked for each step:

- launching with a file argument whose path contains `–` and `é`
- file details shown as 0:40 · 1920 × 1080 · 30 fps · 4.3 MB
- presets, the Advanced panel, and the target-size box with its low and too-low warnings
- a 2 MB two-pass job: pass 1 → pass 2 → "Pass 3 of 3: fitting the target size", then
  "Done: 4.3 MB → 1.9 MB (55% smaller)", Target 2.0 MB, Result 2,037,603 bytes
- Cancel during the corrective pass: "Cancelled. Nothing was saved", with no partial file,
  temp folder or ffmpeg.exe left
- Extract audio, the "already exists" dialog with Keep both / Replace it / Cancel, and Replace it
- output named the same as the input refused, and output named as a **hard link** to the input
  refused (Windows file-ID check, as implemented by Wine)
- the About window showing "ffmpeg version 8.1.3"

Fixes made from those screenshots:

- disabled text boxes turned white
- the system scroll bar was light grey on the dark theme
- the "fitting" label was cut off
- the audio-only notice appeared twice
- FFmpeg reported a bare commit hash as its version

### 3. Speech

The fixtures' speech is synthetic (flite). The checks were:

- channels and bitrates (above)
- clean decodes
- sync at 0 ms

**Nobody listened to the output to judge intelligibility.** 96 kbit/s AAC stereo and 64 kbit/s
mono are well above what speech normally needs, but that is reasoning, not a test.

### 4. Not performed

- **Real Windows.** Not tested:
  - a clean Windows 10 or 11 machine without .NET (the exe is self-contained, which is the
    point, but this was not tried)
  - SmartScreen's warning for an unsigned exe
  - high-DPI scaling
  - Windows Defender scanning the exe
- **Real GUI input.** Not tested:
  - drag and drop of a file onto the window (xdotool can't drag into Wine)
  - the native folder picker, Open file and Show in folder (Wine has no Explorer or media player
    to hand them to)
  - keyboard-only use
- **Windows-specific code paths, beyond what Wine emulates.** Not tested:
  - the Job Object that kills ffmpeg if the app crashes
  - case-insensitive and 8.3 short-name spellings of the input path
  - network drives
  - OneDrive placeholder files
  - files locked by another program
- **Real recordings.** No real classroom, OBS, Zoom, Teams, phone or camera files were used. All
  fixtures are synthetic. HDR, 10-bit, interlaced, AV1, HEVC and multi-audio-track inputs were not
  tested.
- Speech intelligibility by listening (section 3).
- Whether any website or service accepts the output (out of scope, and not claimed).
