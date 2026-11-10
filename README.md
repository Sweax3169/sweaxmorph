<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/art/banner-dark-en.svg">
  <img src="docs/art/banner-light-en.svg" width="100%" alt="SweaxMorph: drop any file on Morph, pick a format">
</picture>

[![Download](https://img.shields.io/github/v/release/Sweax3169/sweaxmorph?label=download&color=8B7CFF&style=for-the-badge)](https://github.com/Sweax3169/sweaxmorph/releases/latest)
[![Windows 10 · 11](https://img.shields.io/badge/Windows-10%20%C2%B7%2011-FF6B9A?style=for-the-badge)](#install)
[![Formats](https://img.shields.io/badge/formats-126-2EC4B6?style=for-the-badge)](#every-format)
[![License](https://img.shields.io/badge/license-MIT-4C8DFF?style=for-the-badge)](LICENSE)

[Website](https://sweax3169.github.io/sweaxmorph/) · [Türkçe](README.tr.md) · [Download](https://github.com/Sweax3169/sweaxmorph/releases/latest)

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/media/demo-dark-en.gif">
  <img src="docs/media/demo-light-en.gif" width="760" alt="A photo is dragged onto Morph, Morph swallows it, AVIF is picked and a file 91% smaller appears">
</picture>

</div>

## Meet Morph

Morph is a little gel creature that eats files. Carry a file near it and it opens its mouth; let go and it swallows the
file, takes on its colour, and grows a ring of petals: the formats that file can become. Pick one and Morph fills up
with liquid as it works, then pops out the result next to your original. Drag the result straight into a chat or an email.

<table>
<tr>
<td><img src="docs/media/petals-light-en.png" alt="Petals around Morph for a photo"></td>
<td><img src="docs/media/garden-dark-en.png" alt="Every target format for a video, searchable"></td>
</tr>
<tr>
<td align="center"><sub>Drop a photo: the formats it can become bloom around Morph</sub></td>
<td align="center"><sub><b>More</b> opens the whole garden. Just start typing a format's name</sub></td>
</tr>
</table>

## Eight flavours

Pick Morph's flavour in **Settings → Look**. It changes at once, no restart: the cloud, the words, the buttons and Morph's
own gel. Files still paint Morph in their own colour; a palette changes the world around them. **Candy** is the original look.

<p align="center">
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/media/palettes-dark-en.gif">
  <img src="docs/media/palettes-light-en.gif" width="420" alt="Morph changing palette live: Candy, Mint, Peach, Lagoon, Berry, Lemonade, Sunset, Cloud">
</picture>
</p>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/media/palettes-dark-en.png">
  <img src="docs/media/palettes-light-en.png" width="100%" alt="Morph waiting in each of the eight palettes">
</picture>

| Palette | Gel | Mood |
|---|---|---|
| **Candy** | lavender into pink | the 1.0 look, a plum night |
| **Mint** | fresh mint into sea green | sea glass at night |
| **Peach** | peach into coral | a cocoa night |
| **Lagoon** | sky into periwinkle | deep water |
| **Berry** | lilac into violet | blackcurrant |
| **Lemonade** | lemon into lime | a garden at night |
| **Sunset** | apricot into orchid | dusk |
| **Cloud** | pearl into slate | quiet desks |

<p align="center"><img src="docs/media/look-light-en.png" width="560" alt="Settings → Look: theme and the eight palette tiles, each drawn by Morph itself"></p>

## Three ways in

| | |
|---|---|
| <kbd>Win</kbd> + <kbd>Alt</kbd> + <kbd>F</kbd> | Opens Morph next to your pointer, on whichever monitor it is on. Morph already waiting on another monitor comes over instead of hiding. Change the shortcut in Settings, it records any combination. |
| A macro key | Record a combination such as <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>F9</kbd> on your keyboard's macro key, then click **Change** in Settings and press the macro key. F13–F24, Pause and Scroll Lock work on their own; macro apps that run programs can use `SweaxMorph.exe --toggle`. |
| <kbd>Space</kbd> while dragging | Start dragging files in Explorer, tap Space, and Morph pops up right under them. Drop on Morph, or straight on a petal. |
| Right-click → **Convert with SweaxMorph** | Also in **Send to**. Select ten files and they all arrive together. On Windows 11 it's under **Show more options**. |

### Where Morph opens

Always on the monitor your pointer is on, at that monitor's own scale. In **Settings → Where Morph opens** pick
**next to the pointer** (the default), **the middle of the screen**, or **where I left it**: drag the cloud around Morph to
move it, and it opens there next time. Morph stays on top of full-screen windows and raises itself if another
always-on-top window covers it.

**Administrator windows.** Windows keeps the keys typed into administrator windows (Task Manager, installers, many games)
away from ordinary apps, so a shortcut can't reach Morph there. Turn on **Also over administrator windows** and a tiny
helper with administrator rights listens for your shortcut in those windows too. Windows asks once. SweaxMorph itself
stays an ordinary app, so dragging files from Explorer keeps working.

<p align="center"><img src="docs/media/place-light-en.png" width="560" alt="Settings: where Morph opens, and the administrator-windows option"></p>

## Quick and steady

Version 1.1 was measured before and after with SweaxMorph's own bench (`SweaxMorph.exe --bench`), on the PC it is built on:
Ryzen 5 7600, 240 Hz main monitor plus two more. Same look, same animations, same conversions; all 103 self-test
conversions pass.

| | 1.0.5 | 1.1 |
|---|---:|---:|
| Shortcut → Morph on screen, first time after start | 162 ms | **9 ms** |
| Shortcut → Morph on screen, later | 7 ms | 7 ms (it already was quick) |
| `--toggle` from a macro key or a launcher, to open (median / worst) | 460 / 1,197 ms | **65 / 74 ms** |
| Two `--toggle`s 60 ms apart | 3 of 3 lost one | **none lost** |
| Shortcut while Morph waits on another monitor | hid it | **brings it over** |
| Layout passes per second while Morph moves | 240 | **0** |
| First open with a UI Automation client running (`--uia`) | 222 ms | **9 ms** |
| Longest frame while a 4000 × 2600 photo converts | 97 ms | **24 ms** |
| Frame rate while opening / dragging | 233 / 238 per s | 233 / 231 per s (both at the refresh rate) |
| CPU dozing or waiting in the tray | 0% | 0% |
| Private memory after start | 73 MB | 87 MB (Morph is kept ready) |

What was going on, and what changed:

- **The shortcut sometimes "didn't open".** Morph waits on top after you click elsewhere. Pressing the shortcut then hid
  it, even when it was waiting on another monitor where you couldn't see it. Now the shortcut puts Morph away only when
  it is with you; otherwise it comes over, on the pointer's monitor and at its scale. Space-while-dragging and the
  right-click menu bring it to the pointer too.
- **`--toggle` took ~600 ms and could lose a press.** A second launch started all of WPF before handing over, and every
  request was held 280 ms and merged with the next. It now hands over before WPF starts, and commands go through at once,
  in order. The sender also lets Morph take the focus.
- **A layout pass every frame.** Morph was redrawn in a way that made WPF lay out the whole window each frame. With a UI
  Automation client looking at the window (the touch keyboard, the emoji panel, PowerToys: this PC has one), WPF then
  walks and reports the whole window each time. Morph now draws into a retained drawing with cached brushes; the window
  reports only itself to UI Automation.
- **Slow first open, slow after a break.** The window is now built and drawn once at start, invisibly. 1.0 also emptied
  its memory a few seconds after every close, so the next open, and the keyboard hook on every key you typed, had to page
  it back in; Windows silently drops a hook that answers late even once, and it was re-armed only every 10 minutes. Now
  memory goes back only after 10 minutes of not touching the PC, the hook allocates nothing and is re-armed every 20 s,
  and garbage collection runs in the background.
- **A possible freeze.** Taking the focus could borrow the input of the window in front even when that window was hung,
  which froze Morph with it. It now uses a no-op input first and never touches a window that isn't answering.
- **A hitch when a conversion finished.** File icons for the results were asked of the shell on the UI thread, right
  when Morph cheers. They now load on a thread of their own.
- **Kept the window that measured faster.** Compositor transparency (what SweaxClip's panel uses) was built and measured
  too, on top and underneath: with Morph's cloud and petals it drew opening and dragging at 100–125 frames a second against
  185–235 for the layered window, so Morph keeps its layered window. The bench still has both (`--dwm`).

## Every format

Images, video, audio, documents, spreadsheets, slides, PDF, e-books, data, archives, subtitles and fonts. Anything that
can be read can become anything it makes sense to become. Where no single tool can do it, SweaxMorph plans a route,
like PowerPoint → PDF → PNG, or Pages → DOCX → EPUB.

| Kind | Formats |
|---|---|
| **Image** | PNG · JPG · WEBP · AVIF · HEIC\* · GIF · BMP · TIFF · ICO · SVG · PSD · TGA · JXL · JP2 · DDS · EXR · HDR · QOI · PCX · PPM · EMF\* · CUR\* · RAW\* · XCF\* · APNG |
| **Video** | MP4 · MKV · WEBM · MOV · AVI · WMV · FLV · M4V · MPG · TS · 3GP · OGV |
| **Audio** | MP3 · WAV · FLAC · M4A · AAC · OGG · OPUS · WMA · AIFF · AC3 · M4R · MKA · WV · APE\* · AMR · AU · DTS\* |
| **Subtitles** | SRT · VTT · ASS · TTML · LRC · SUB\* |
| **Document** | DOCX · DOC · DOCM\* · ODT · RTF · TXT · MD · HTML · MHT · TEX · RST · ORG · ADOC · TEXTILE · WIKI · IPYNB · TYP · DOCBOOK · XPS · PAGES\* · WPD\* · PUB\* · VSDX\* · ODG · HWP\* |
| **Spreadsheet** | XLSX · XLS · XLSM\* · XLSB · ODS · CSV · TSV · NUMBERS\* |
| **Slides** | PPTX · PPT · PPSX · ODP · KEY\* |
| **PDF** | PDF |
| **E-book** | EPUB · MOBI · AZW3 · FB2 · CBZ · LIT\* |
| **Data** | JSON · YAML · TOML · XML · JSONL |
| **Archive** | ZIP · 7Z · RAR\* · TAR · TAR.GZ · TAR.BZ2\* · TAR.XZ\* · GZ\* · BZ2\* · XZ\* · ISO\* · CAB\* |
| **Font** | TTF · OTF · WOFF · WOFF2 |

<sub>\* read only: SweaxMorph opens these and turns them into something else.</sub>

### Small things that matter

<table>
<tr>
<td width="50%"><picture><source media="(prefers-color-scheme: dark)" srcset="docs/art/card-shrink-dark-en.svg"><img src="docs/art/card-shrink-light-en.svg" width="100%" alt="shrink"></picture></td>
<td width="50%"><picture><source media="(prefers-color-scheme: dark)" srcset="docs/art/card-combine-dark-en.svg"><img src="docs/art/card-combine-light-en.svg" width="100%" alt="combine"></picture></td>
</tr>
<tr>
<td width="50%"><picture><source media="(prefers-color-scheme: dark)" srcset="docs/art/card-route-dark-en.svg"><img src="docs/art/card-route-light-en.svg" width="100%" alt="route"></picture></td>
<td width="50%"><picture><source media="(prefers-color-scheme: dark)" srcset="docs/art/card-safe-dark-en.svg"><img src="docs/art/card-safe-light-en.svg" width="100%" alt="safe"></picture></td>
</tr>
<tr>
<td width="50%"><picture><source media="(prefers-color-scheme: dark)" srcset="docs/art/card-ocr-dark-en.svg"><img src="docs/art/card-ocr-light-en.svg" width="100%" alt="ocr"></picture></td>
<td width="50%"><picture><source media="(prefers-color-scheme: dark)" srcset="docs/art/card-fast-dark-en.svg"><img src="docs/art/card-fast-light-en.svg" width="100%" alt="fast"></picture></td>
</tr>
</table>

- **Shrink.** Every photo, video and song also gets a "Shrink" petal: the same format, much smaller.
- **Combine.** Drop many files and tick *Combine into one file*: photos → one PDF or animated GIF, PDFs → one merged PDF, clips → one video, anything → one ZIP.
- **Never overwrites.** Results land next to the original (or on the Desktop, or a folder you choose) as `name (2).ext` when the name is taken.
- **Fast when it can be.** MP4 → MKV copies the streams instead of re-encoding and takes a second. Office files go through Office itself when it's installed.
- **Scans become text.** Image → TXT and scanned PDF → TXT use the OCR built into Windows, in your languages.
- **Three qualities.** *Best* keeps everything, *Balanced* is the sensible default, *Small* is for sharing.

## Measured

All 103 conversions of the built-in self-test pass (`SweaxMorph.exe --selftest <folder>`). These are some of them, timed on an AMD Ryzen 5 7600 with 32 GB of RAM. The sample files are small on purpose, so the times mostly show what each engine costs to start.

| Conversion | Through | Time | Size |
|---|---|---:|---|
| `photo.png` → **WEBP** | ImageMagick | 189 ms | 90 KB → 20 KB |
| `photo.png` → **AVIF** | ImageMagick | 696 ms | 90 KB → 11 KB |
| `photo.png` → **ICO** | ImageMagick | 251 ms | 90 KB → 364 KB |
| `photo.png` → **TXT** | Windows OCR | 707 ms | 90 KB → 66 B |
| `clip.mp4` → **MKV** | FFmpeg | 341 ms | 433 KB → 430 KB |
| `clip.mp4` → **WEBM** | FFmpeg | 2.6 s | 433 KB → 490 KB |
| `clip.mp4` → **GIF** | FFmpeg | 2.1 s | 433 KB → 1.4 MB |
| `song.wav` → **MP3** | FFmpeg | 307 ms | 1.0 MB → 36 KB |
| `song.wav` → **MP4** | FFmpeg | 1.6 s | 1.0 MB → 171 KB |
| `notes.md` → **DOCX** | Pandoc | 195 ms | 408 B → 11 KB |
| `notes.md` → **PDF** | Pandoc → Edge | 2.8 s | 408 B → 72 KB |
| `letter.docx` → **PDF** | Word | 4.3 s | 11 KB → 92 KB |
| `deck.pptx` → **PNG** | PowerPoint | 1.1 s | 28 KB → 74 KB |
| `paper.pdf` → **DOCX** | LibreOffice | 10.3 s | 92 KB → 7.9 KB |
| `paper.pdf` → **TXT** | Windows PDF | 571 ms | 92 KB → 336 B |
| `table.csv` → **XLSX** | SweaxMorph | 18 ms | 131 B → 6.6 KB |
| `config.json` → **YAML** | SweaxMorph | 9 ms | 246 B → 202 B |
| `font.ttf` → **WOFF2** | SweaxMorph | 3.0 s | 1021 KB → 464 KB |
| `pack.zip` → **7Z** | SweaxMorph | 128 ms | 48 KB → 46 KB |

## How it works

Most conversions run inside SweaxMorph: images ([Magick.NET](https://github.com/dlemstra/Magick.NET)), PDF pages and OCR
(Windows), data and spreadsheets, archives, and web fonts (written from the WOFF/WOFF2 specs). The heavy engines are used
where they are best:

| Engine | Used for | How you get it |
|---|---|---|
| [FFmpeg](https://ffmpeg.org) | video, audio, subtitles | found on your PC, or downloaded once (~100 MB) the first time you need it |
| [Pandoc](https://pandoc.org) | Markdown, HTML, EPUB, LaTeX, Jupyter, wiki formats | downloaded once (~40 MB) when first needed |
| 7-Zip | creating .7z | downloaded once (600 KB) when first needed |
| Microsoft Office | Word, Excel, PowerPoint, slides → pictures and MP4 | used if installed (hidden, read-only, macros off) |
| [LibreOffice](https://www.libreoffice.org) | Pages, Numbers, Keynote, Visio, Publisher, WordPerfect…, PDF → Word, Office files without Office | used if installed |
| [Calibre](https://calibre-ebook.com) | Kindle MOBI and AZW3 | used if installed |
| Microsoft Edge | HTML and SVG → PDF | already on Windows |

Nothing is uploaded anywhere. The only network traffic is the one-time engine download, from the projects' official
releases.

## Install

1. Download **SweaxMorph-win-x64.zip** from [Releases](https://github.com/Sweax3169/sweaxmorph/releases/latest) and run `SweaxMorph.exe`.
2. It copies itself to `%LOCALAPPDATA%\Programs\SweaxMorph`, adds the right-click entry and starts with Windows. No admin rights needed.
3. Press <kbd>Win</kbd> + <kbd>Alt</kbd> + <kbd>F</kbd> and drop something on Morph.

Morph waits on top while you go and pick a file in Explorer; close it with its × button, <kbd>Esc</kbd> or the shortcut. Settings open from the gear on Morph's top-right or the tray icon's menu. Updating from 1.0 keeps your settings (the old file stays beside the new one as `settings.json.1.0.bak`). To remove SweaxMorph, turn off the right-click entry, start-up and the administrator option in Settings, quit, and delete the folder.

<img src="docs/media/settings-light-en.png" width="720" alt="SweaxMorph settings">

## Build

```bash
dotnet build src/SweaxMorph -c Release
```

.NET 9, WPF. `SweaxMorph.exe --selftest <folder>` makes sample files and runs about a hundred conversions,
writing a report; `--shots <folder>` and `--palettes <folder>` render the pictures and recordings in this README;
`--bench <file> --instance bench` times opening, frame pacing, dragging, converting, idle CPU, memory, lost requests and
placement on every monitor, in a separate copy whose window is never shown (`--uia` adds a UI Automation client).

## Credits

Magick.NET · FFmpeg · Pandoc · SharpCompress · ClosedXML · ExcelDataReader · PdfPig · YamlDotNet · Tomlyn ·
Nunito by Vernon Adams (SIL Open Font License). See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Made by [Sweax Studios](https://github.com/Sweax3169). MIT License.
