# Changelog

## 1.0.0 (2026-10-15), first public release

Same app as 1.0.0-rc.2: no changes to the window, the presets or compression. The bundled
`ffmpeg.exe` and `ffprobe.exe` are the same files as in rc.1 and rc.2 (FFmpeg 8.1.3).

- The app now reports version **1.0.0** in the window footer, About, the log and the exe's
  Product version (File version 1.0.0.0, as before).
- The version is defined once, in `Directory.Build.props`; every other version string and the
  release ZIP names are derived from it, and packaging stops if the exe was built as another version.
- Reproducible build: the source ZIP publishes to a byte-identical `ParadisCompress.exe`.

## 1.0.0-rc.2 (2026-10-15)

- Compact 640 × 600 window: preset dropdown with ⓘ help popovers, target size on one row,
  Advanced collapsed on every launch, compact result with Details.
- Decimal MB everywhere (1 MB = 1,000,000 bytes). The finished file is checked against the target in
  exact bytes; above it, the result says so in red and offers **Retry smaller**.
- Audio-only hides video controls; errors return the window to idle first.
- The publish profile is included in the source ZIP (it was missing from rc.1).

See [TEST_REPORT.md](TEST_REPORT.md) for details.

## 1.0.0-rc.1 (2026-10-07)

First release candidate: three presets, optional target size (binary MB), Extract audio (MP3),
two-pass progress and safe Cancel, bundled FFmpeg 8.1.3.
