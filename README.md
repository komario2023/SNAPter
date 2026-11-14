# SNAPter

A screenshot tool for Windows 10 / 11 that lives in the tray. In the spirit of Lightshot, but flexible: select a region, draw on it with real objects (arrows, text, shapes, brush, blur, numbered steps...), then copy or save. Everything stays an object until you copy or save, so nothing is flattened and everything can be edited.

C# + .NET 8 + WPF. Russian and English interface (more languages by copying a file).

## Demo

**Quick tour (about 3 minutes)**: capture, draw, beautiful looks, live settings preview, profiles, panels.

https://github.com/user-attachments/assets/0f64bf5b-8431-478e-b793-29a752b5650a

**Full tour (about 11 minutes)**: every key of the editor, then every page of the settings.

https://github.com/user-attachments/assets/690f9625-014f-4385-9b8a-f667174e1719

In the demos the capture hotkey is shown as `Ctrl+Shift+Alt+F8` (the recording copy could not take the real one). By default it is `Ctrl+Shift+Alt+R`; every hotkey can be changed in Settings - Hotkeys.

## What it does

- **Region capture** on a frozen screen: drag a frame, move it (Space), stretch it by the handles, switch aspect ratios with Tab (free, 16:9, 4:3, 3:2, 1:1, 21:9, 4:5, A4).
- **Tools**: arrow, line, rectangle, ellipse, brush, marker, text, numbered steps, blur, fill, spotlight, magnifier, eraser, color picker. Each tool has a key and three or more presets (keys 1-9).
- **Smart drawing**: Shift, Ctrl and Alt change what you draw (square, rounded, from the center, straight line, double arrow, fill); objects snap to the axes of others (hold Right Alt); text stays editable; shapes and text rotate.
- **Profiles on one key** (the key left of 1): Full, Ascetic, Minimal and your own. Each profile can have its own bottom panel.
- **Panels that adapt**: they follow the frame live, shrink and move inside a very big region (Shift+Tab walks them round the corners).
- **Beautiful screenshot**: eleven looks (Window, Windows, Browser, Polaroid, Glass, Neon, Device bezel, Cinema...), random looks, your own text in the frame.
- **Also**: GIF and video recording, scrolling screenshots, pinning a picture on the screen, upload to a folder (for example one that Google Drive for desktop synchronizes; direct Google Drive upload is not finished, see Status), light / dark and five color themes.
- **Settings** with live panel preview, search, undo / redo of unsaved changes, export / import, base settings.

All keys are listed in [docs/HOTKEYS.txt](docs/HOTKEYS.txt).

## Status

The program works as it is and is used every day. One part is **not finished: the cloud upload to Google Drive** (Settings - Cloud, with your own OAuth client). It is there, but it was never completed or tested well. If you need it, please finish it (pull requests are welcome). Uploading to a normal folder (the "A folder" target) is simpler and works.

## Run

Double-click `Start SNAPter.bat`. The icon appears in the tray near the clock. The first start builds the program (the .NET 8 SDK is needed: `dotnet --list-sdks`); later the program rebuilds itself when its source files change (Settings - General turns this off). Exit: tray icon, right-click, Exit.

`Build SNAPter.bat` builds by hand. A self test: `app\publish\SNAPter.exe --selftest`.

## More

- [README.txt](README.txt) - a longer overview, how it is built, where the files are.
- [docs](docs) - hotkeys, architecture, themes and languages, roadmap.

## License

MIT, see [LICENSE](LICENSE). Written and owned by its author, komario2023. You may use, copy and change the program freely, but you may not present yourself as the author of the original program: keep the author's name and the notice in every copy.
