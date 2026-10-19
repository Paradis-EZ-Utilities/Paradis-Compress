# Manual acceptance checklist (real Windows)

These are the checks the automated tests and the Wine run could not cover. Use a Windows 10 or 11
PC, ideally one that has never had .NET installed, and **a copy** of a real class recording.
Tick each box, and note anything odd along with the log (**Log folder** link, bottom left).

## A. First run (do these first)

- [ ] Unzip `ParadisCompress-1.0.0-win-x64.zip` to the Desktop. Double-click
      `ParadisCompress.exe`. If SmartScreen appears, **More info → Run anyway** works and the
      window opens. No ".NET is required" message appears.
- [ ] The window looks right: dark theme, sharp text, nothing cut off. It opens at about
      640 × 600 (scaled with your display setting) and, with a video loaded, everything from the file
      line down to **COMPRESS** and the footer is visible without scrolling.
- [ ] Repeat at **100 %, 125 % and 150 %** display scaling (Settings → System → Display → Scale).
      On a small laptop screen at 150 %, the window fits the screen and the settings scroll while
      COMPRESS, progress and Cancel stay visible.
- [ ] Drag the window edges to make it smaller and larger. It stops at a sensible minimum size;
      controls are never cut off (the settings area scrolls instead).
- [ ] **About** shows version 1.0.0 and "ffmpeg version 8.1.3". The footer at the bottom right
      shows v1.0.0, and the newest log file (**Log folder**) starts with "Paradis Compress starting (v1.0.0)".
      In Explorer, right-click `ParadisCompress.exe` → Properties → Details shows File version
      1.0.0.0 and Product version 1.0.0. **Licences and notices**
      opens the `licenses` folder.

## B. A real recording, default settings (the most important test)

- [ ] **Drag** a real lecture recording (ideally 30 min or more) from Explorer onto the window. The
      length, resolution, fps and size match what Explorer or your player says.
- [ ] Leave **Class Recording** selected and click **COMPRESS**. The progress bar, elapsed time and
      time-left estimate update, and the PC stays usable.
- [ ] When it finishes, the compact result shows sizes, % smaller and the time taken. **Open File**
      plays it in your normal player. **Show in Folder** opens Explorer with the file selected.
      **Details** shows the path and exact byte counts.
- [ ] In the new file, at the start, middle and end, the small text on slides or code is readable
      at 100 % zoom (compare with the original at the same moment).
- [ ] The voice is clear, and lips or pointer movements stay in sync with the speech at the start,
      middle and end. Listen with headphones for a minute or two.
- [ ] The original file's size and date modified are unchanged.

## C. Target size (decimal MB since rc.2)

- [ ] Tick **Target size**, enter a size that is about a third of the original, and compress.
      Progress goes through "Pass 1 of 2" and "Pass 2 of 2" (and maybe "Pass 3 of 3: fitting the
      target size"). In Explorer, **Properties → Size** shows "(N bytes)": N must be at most the
      target × 1,000,000 (for 100 MB: at most 100,000,000 bytes). Explorer's own "MB" figure will look
      about 5 % smaller because it uses binary units; that is expected.
- [ ] If you have an upload limit to meet (for example 25 MB or 500 MB), enter exactly that number,
      compress, and upload the result. Note whether it was accepted. (The app makes no promise
      either way.)
- [ ] Enter a very small target (for example 5 MB for an hour). A short red warning with a suggested
      size appears under the target before you start, and Compress asks for confirmation.
- [ ] Enter a target larger than the original. A warning says it is not smaller than the original.
- [ ] If a result ever comes out above the target, the result says "Not within your … target: over
      by …" in red (never "Done"), and **Retry smaller** produces a file within the target.

## D. Extract audio

- [ ] Choose **Extract audio (MP3)**. The preset list and the video settings under Advanced
      disappear, and a short line says slides and on-screen content are not included.
- [ ] Click **EXTRACT AUDIO**. The MP3 plays, the voice is clear, and the length matches the video.
- [ ] With **Target size** ticked, the line under it names the MP3 bitrate the target leads to.
      The MP3's size in bytes is at most the target × 1,000,000.

## E. Compact layout, help and keyboard

- [ ] Click each ⓘ button (next to Preset and Target size). A readable popover opens inside the
      window; clicking elsewhere, its ✕ button, or **Esc** closes it.
- [ ] Without the mouse: press **Tab** until an ⓘ button is focused (it turns blue), press **Enter**
      or **Space** to open it, **Esc** to close. Focus returns to the ⓘ button. Do the same for
      **Details** after a job and for the footer links (About, Log folder, More from Paradis EZ
      Utilities).
- [ ] With Narrator on (Ctrl+Win+Enter), the ⓘ buttons are read as "Help: presets" and "Help: target
      size".
- [ ] **Show advanced settings** opens the panel below; **Hide** closes it. Next launch it starts
      closed again.
- [ ] **More from Paradis EZ Utilities** opens https://github.com/Paradis-EZ-Utilities.

## F. Safety

- [ ] Compress the same file twice with the same name. The second time asks **Keep both / Replace
      it / Cancel**. **Keep both** creates "(compressed) (2)". **Replace it** replaces the file only
      at the end.
- [ ] Type the original's own name (same folder) as the output name, also with different
      capitalisation. It is refused and the original is untouched.
- [ ] Start a long job and click **Cancel**. Within a second or two it says "Cancelled". In Task
      Manager no `ffmpeg.exe` remains, and the output folder has no `.partial-…` file.
- [ ] Start a long job and close the window. It asks to cancel. Say yes; no `ffmpeg.exe` remains.
- [ ] Start a long job and end `ParadisCompress.exe` in Task Manager. `ffmpeg.exe` disappears too
      (Job Object). A leftover `.partial-…` file is expected here; delete it.
- [ ] Save to a USB stick or a nearly full drive, if you have one. A clear "not enough space"
      message appears and no partial file is left.
- [ ] Use a file in a folder with accents or emoji in its name (for example `Cours – été 😀`).
      It works.
- [ ] Use a file inside OneDrive (if used). It works, or gives a clear message if the file is
      online-only.

## G. Other files you have

- [ ] A phone video recorded in portrait. The result is upright.
- [ ] A Zoom or Teams recording (often variable frame rate). It stays in sync to the end.
- [ ] A file that is already small (for example one you compressed before). The result honestly
      says it is not smaller.
- [ ] A non-video file (a .docx renamed to .mp4). You get a plain-language error.

## Report back

For each failed box, note what you did and what you saw, attach the log file from **Log folder**
(it contains file names and settings, no video or audio), and include a screenshot if it is
visual.
