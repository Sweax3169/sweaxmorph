# Third-party notices

SweaxMorph includes the following components. Their licenses apply to those parts.

| Component | License | Link |
|---|---|---|
| Magick.NET / ImageMagick | Apache 2.0 / ImageMagick License | https://github.com/dlemstra/Magick.NET |
| SharpCompress | MIT | https://github.com/adamhathcock/sharpcompress |
| ClosedXML | MIT | https://github.com/ClosedXML/ClosedXML |
| Open XML SDK | MIT | https://github.com/dotnet/Open-XML-SDK |
| ExcelDataReader | MIT | https://github.com/ExcelDataReader/ExcelDataReader |
| PdfPig | Apache 2.0 | https://github.com/UglyToad/PdfPig |
| YamlDotNet | MIT | https://github.com/aaubry/YamlDotNet |
| Tomlyn | BSD 2-Clause | https://github.com/xoofx/Tomlyn |
| Nunito (font, by Vernon Adams) | SIL Open Font License 1.1 | https://github.com/googlefonts/nunito |

## Engines downloaded at run time (not included)

When a conversion first needs them, SweaxMorph downloads these from their official releases into
`%LOCALAPPDATA%\SweaxMorph\engines`. They are separate programs under their own licenses:

- **FFmpeg** essentials build by gyan.dev (GPL v3), https://github.com/GyanD/codexffmpeg
- **Pandoc** (GPL v2 or later), https://github.com/jgm/pandoc
- **7-Zip** console `7zr.exe` (GNU LGPL), https://www.7-zip.org

Microsoft Office, LibreOffice, Calibre and Microsoft Edge are used only when already installed on the PC.
