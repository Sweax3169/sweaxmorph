# Changelog

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
