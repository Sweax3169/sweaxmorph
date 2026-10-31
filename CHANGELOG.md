# Changelog

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
