# X Image Stitch

[简体中文](README.md) | English

A Windows image stitching tool built with .NET 8 + WinForms. Single-file portable, no installation required.

## Features

- Stitch multiple images horizontally or vertically, with auto-computed canvas size
- Import via file dialog or drag & drop, supports JPG / PNG / BMP, async batch import with progress
- Image ordering list: drag to reorder, move up / down, remove, clear; serial number + thumbnail display
- Live preview (background rendering, memory-friendly): mouse-wheel zoom, left-drag pan, double-click reset
- Size prediction: estimates output size based on image content complexity, shown live in the status bar
- Output size limit: auto-compresses when exceeded (lowers JPEG quality first, then downsizes resolution); PNG auto-converts to JPEG when over limit
- Render modes: Normal (sequential, memory-friendly) / Parallel (multi-core decode, faster)
- Status bar shows render cores / threads and real-time process memory usage
- High-DPI aware (PerMonitorV2)

## Usage

1. Download the latest `*-win-x64.zip` from the [Releases](https://github.com/mdhyy008/ImageStitchMaster/releases) page.
2. Extract and run `ImageStitchMaster.exe` (no .NET runtime required).
3. Click "Add Images" or drag images into the window.
4. Drag to reorder images in the left list, choose stitch direction, optionally set an output size limit (in MB; leave blank for no limit).
5. Click "Save Image", review the estimated size, then confirm and choose a save location and format.

## Build from Source

```bash
dotnet build -c Release
```

Publish as a single-file Windows x64 executable (no .NET runtime required):

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## License

[MIT](LICENSE)
