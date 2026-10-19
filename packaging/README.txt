Paradis Compress @VERSION@ - Video Compressor for Class Recordings
Part of Paradis EZ Utilities

STARTING IT
  1. Unzip this whole folder anywhere (for example Documents or the Desktop).
     Keep the "ffmpeg" folder next to ParadisCompress.exe.
  2. Double-click ParadisCompress.exe. Nothing is installed; delete the folder to remove it.
  3. The first time, Windows may show "Windows protected your PC" because this build is not
     code-signed. Click "More info", then "Run anyway".

USING IT
  - Drag a video onto the window, or click "Choose a video...".
  - Pick a preset from the list. "Class Recording" is the default and keeps small text
    sharp. The (i) button next to it explains each preset.
  - Optional: tick "Target size" and type a size in MB, for example an upload limit.
    Sizes are decimal: 1 MB = 1,000,000 bytes, so 500 MB means at most 500,000,000 bytes.
    (File Explorer counts 1 MB as 1,048,576 bytes, so it shows the same file about 5%
    smaller.) The app aims a little under the target and checks the finished file's exact
    size. If it is still over, it says so and offers "Retry smaller". No size can promise
    that a website or service will accept the file.
  - Click COMPRESS (or EXTRACT AUDIO). The new file is saved next to the original as
    "<name> (compressed).mp4" unless you choose another folder or name.
  - Details after a job shows exact sizes in bytes. Your original video is never changed.

  "Extract audio" saves only the sound as an MP3. Slides and anything on screen are left out.

IF SOMETHING GOES WRONG
  Click "Log folder" at the bottom of the window. The log lists settings, file sizes and
  error messages. It never contains the video or audio itself.
  Logs: %LocalAppData%\ParadisCompress\Logs

WHAT'S IN THIS FOLDER
  ParadisCompress.exe   the app (includes the .NET runtime; no separate install needed)
  ffmpeg\               FFmpeg 8.1.3, which does the actual compression (GPL v2 or later)
  licenses\             licence texts for the app and everything bundled with it

The FFmpeg source code for this exact build is published next to this release as
ParadisCompress-@VERSION@-ffmpeg-source.zip.

Requires 64-bit Windows 10 or 11.
