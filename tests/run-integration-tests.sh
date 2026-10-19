#!/usr/bin/env bash
# Runs the end-to-end tests against a real FFmpeg.
#
#   PARADIS_FFMPEG_DIR      folder with the ffmpeg + ffprobe under test (required; use the build from
#                           packaging/ffmpeg/build-ffmpeg-linux-for-tests.sh so the version matches the release)
#   PARADIS_FIXTURE_FFMPEG  full-featured ffmpeg with drawtext/freetype and flite, to generate fixtures
#                           (default: ffmpeg on PATH; Ubuntu's ffmpeg package works)
#   PARADIS_FIXTURE_DIR     fixture cache (default: <temp>/paradis-compress-fixtures)
#   PARADIS_TEST_ARTIFACTS  where frames, metrics and results.jsonl go (default: <temp>/paradis-compress-artifacts)
#   PARADIS_TEST_READONLY_DIR, PARADIS_TEST_SMALL_DISK_DIR
#                           optional: a read-only folder and a folder on a tiny (few MB) file system, for the
#                           unwritable-destination and disk-full tests. As root, this script creates both with tmpfs.
#
# Usage: tests/run-integration-tests.sh [extra dotnet test args, e.g. --filter Category!=Long]
set -euo pipefail
cd "$(dirname "$0")/.."
: "${PARADIS_FFMPEG_DIR:?set PARADIS_FFMPEG_DIR}"
export PARADIS_FFMPEG_DIR
if [ "$(id -u)" = 0 ] && [ -z "${PARADIS_TEST_SMALL_DISK_DIR:-}" ]; then
  mkdir -p /mnt/paradis-tiny /mnt/paradis-ro
  mountpoint -q /mnt/paradis-tiny || mount -t tmpfs -o size=3m tmpfs /mnt/paradis-tiny
  mountpoint -q /mnt/paradis-ro || mount -t tmpfs -o size=1m,ro tmpfs /mnt/paradis-ro
  export PARADIS_TEST_SMALL_DISK_DIR=/mnt/paradis-tiny PARADIS_TEST_READONLY_DIR=/mnt/paradis-ro
fi
dotnet test tests/ParadisCompress.IntegrationTests --logger "console;verbosity=normal" "$@"
