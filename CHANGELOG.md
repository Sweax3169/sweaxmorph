# Changelog

## 1.1.0 (2026-11-10)

Quick, steady, and in eight flavours. Measured with the new `--bench` against 1.0.5 on the same PC (240 Hz main monitor):
first open 162 → 9 ms, later opens unchanged at ~7 ms, `--toggle` to open 460 → 65 ms (worst
1,197 → 74), layout passes while Morph moves 240/s → 0, longest frame while converting a big photo
97 → 24 ms, 222 → 9 ms for the first open with a UI Automation client around; private memory after start 73 → 87 MB
because the Well is kept ready. Same look, animations and
conversions (103/103 self-test).

- **The shortcut brings Morph to you.** A Morph waiting out of focus (on another monitor, or far off) used to be hidden by
  the shortcut. Now it comes over; the shortcut puts it away only when it is with you. Space-while-dragging and the
  right-click menu bring it to the pointer too.
- **Where Morph opens:** next to the pointer, the middle of the screen, or where you last dragged the cloud. Always on the
  pointer's monitor, at that monitor's scale (the window lands on the monitor first, then the spot is worked out).
- **No layout pass per frame.** Morph and the search wave draw into retained drawings with cached, frozen brushes; the Well
  reports only itself to UI Automation (the touch keyboard, emoji panel and PowerToys listen on this PC).
- **Warm first open.** The Well is built and drawn once at start, cloaked.
- **No memory trim after every close** (only after 10 minutes without input). The keyboard hook allocates nothing,
  re-arms every 20 seconds without a gap, and garbage collection runs in the background.
- **Foreground without freezes:** a no-op input first (as PowerToys does), and never sharing input with a hung window.
  Resting follows where you are: lost focus, another window in front, a click elsewhere, or 20 quiet seconds.
- **`--toggle` and other launches** hand over before WPF starts; commands are delivered at once and in order (1.0 held them
  280 ms and kept only the last); the sender lets Morph take focus. A second start-up in the background no longer opens Morph.
- **Result icons** load on their own thread; opening a result or its folder starts off the UI thread.
- **Eight palettes** in Settings → Look (Candy, Mint, Peach, Lagoon, Berry, Lemonade, Sunset, Cloud), with tiles drawn
  by Morph; switched live.
- **Also over administrator windows** (opt-in): a helper with administrator rights (`--keys-helper`, a sign-in task made
  once with Windows' permission) hears the shortcut while an administrator window is in front.
- Stays on top of full-screen windows; raises itself once if another always-on-top window covers it, and logs what it was.
- Slow opens, opens without focus and covering windows are logged (process and window class, never titles).
- Settings from 1.0 carry over (`settings.json.1.0.bak` keeps the old file).
- Compositor transparency was built and measured as well and drew opening and dragging at 100–125 fps against 185–235 for
  the layered window, so the layered window stays (`--bench --dwm` keeps the comparison).
- Developer modes: `--bench FILE --instance NAME [--uia] [--dwm] [--quick]`, `--palettes DIR`, `--task-xml FILE`.

## 1.0.5 (2026-11-01)

- The shortcut recorder no longer refuses a single ordinary key silently. It says which key arrived and why it can't stand alone; a macro key with nothing recorded on it sends a plain key (F1 on many gaming keyboards), and the note says to record a combination on it first.

## 1.0.4 (2026-11-01)

- **Shortcuts from macro keys.** Keys sent by macro software and keyboard drivers used to be ignored, so a macro key that pressed the shortcut did nothing. They count now; only SweaxMorph's own keys are marked and skipped.
- F13–F24, Pause, Scroll Lock and the launch keys can be the shortcut on their own; a plain letter or digit is no longer accepted alone by the recorder.
- `SweaxMorph.exe --toggle` opens Morph, or puts it away if it is showing, for macro apps and launchers that run programs.
- Settings explains how to put the shortcut on a macro key.

## 1.0.3 (2026-11-01)

- Windows sometimes opens Morph without giving it focus (when you were busy in another window). Morph then never "lost" focus, so it stayed in its focused look: the haze kept catching clicks meant for the windows below and Morph never dozed. Resting now follows focus on every frame, whether or not focus ever arrived.

## 1.0.2 (2026-11-01)

- **Morph waits for you.** Opening Morph and then clicking a file in Explorer to drag it in made Morph leave, because it closed whenever it lost focus. Now it stays on top: its haze fades so the windows under it can be clicked, comes back while a drag approaches, and Morph opens its mouth as before. It closes with the close button, Esc, the shortcut again, or a few seconds after a finished conversion.
- **Settings and close buttons** on Morph's top-right. Settings has the shortcut, the Explorer options, where results go, quality and the engines; until now it was only in the tray menu.
- When nothing happens for a few seconds, Morph closes its eyes and stops drawing frames, so leaving it open costs no CPU. It wakes when the pointer comes near.

## 1.0.1 (2026-10-31)

- LibreOffice conversions (PDF → Word, Pages, Numbers, Keynote, Visio…) no longer hang on the second use: every run gets a fresh LibreOffice profile.
- Resting in the tray now gives memory back (about 5 MB working set while idle).
- Settings are saved with readable shortcuts ("Win+Alt+F").

## 1.0.0 (2026-10-31)

First release.

- Morph, the drop well: drag files near it, drop on Morph or straight on a petal.
- Three ways in: Win + Alt + F (recordable), Space while dragging in Explorer, right-click / Send to.
- 126 formats, 229 file extensions: images, video, audio, documents, spreadsheets, slides, PDF, e-books, data, archives, subtitles, fonts.
- Route planning across engines (up to three hops), Shrink, Combine into one file, three quality levels.
- Built-in converters for images, PDF, OCR, data, archives and web fonts; FFmpeg, Pandoc and 7-Zip downloaded once when needed; Office, LibreOffice, Calibre and Edge used when installed.
- English and Turkish, light and dark.
