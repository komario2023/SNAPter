SNAPter - screenshot tool for Windows
=====================================

Author: komario2023. The version is shown in the title bar of the Settings window. A short English overview; more notes are in the docs folder.

What it is
----------
A tray program in the spirit of Lightshot, but flexible: select a region, draw on it with objects (arrows, text, shapes,
brush, blur...), copy or save. Everything is an object until you copy or save, so nothing is flattened and everything can be edited.
Windows 10/11, C# + .NET 8 + WPF. Russian and English interface (more languages by copying a file).

How to run
----------
Double-click "Start SNAPter.bat". The icon appears in the tray near the clock.
- The program REBUILDS ITSELF at the start when its source files changed (whoever edited them).
  A small window says that it is building; then the new version starts. Switch: Settings - General.
  How: app\Services\AutoUpdate.cs compares the fingerprint of the sources with app\publish\build.stamp;
  tools\AutoBuild.ps1 builds, writes the stamp and starts the new exe. Needs the .NET SDK (dotnet --list-sdks).
- The very first start (no exe yet) builds in the bat. "Build SNAPter.bat" builds by hand.
- Exit: tray icon, right-click, Exit.
- Self test: app\publish\SNAPter.exe --selftest   (writes logs\selftest.txt; 361 checks; the screen flashes for a second)
- A picture with the ten looks of the beautiful screenshot: SNAPter.exe --beautify-sheet file.png

Hotkeys (defaults, all configurable)
------------------------------------
  Ctrl+Shift+Alt+R   region: frozen screen, drag a frame, edit, Copy / Save
  Ctrl+Shift+Alt+F   whole screen (monitor under the mouse) -> PNG file
  Ctrl+Shift+Alt+C   whole screen -> clipboard
  Ctrl+Shift+Alt+V   region -> clipboard right after selection, no editor (off by default)
  Ctrl+Shift+Alt+G   record a GIF            Ctrl+Shift+Alt+M   record a video (MP4, needs FFmpeg)
  Ctrl+Shift+Alt+S   scrolling screenshot (a tall picture)

Selecting a region
------------------
- Drag a frame, move it, stretch it by the 8 handles. Space moves the frame while dragging.
- ASPECT RATIOS: Tab (before the region and while it is drawn or stretched) walks through your list: free, 16:9, 4:3, 3:2, 1:1,
  21:9, 4:5, A4. The frame is pulled strictly in the ratio; pulling a 16:9 frame downwards turns it into 9:16 by
  itself (4:3 into 3:4), and back. The handles keep the ratio. The list, its order (the first is the start), your own entries
  (2.39:1, 5:7, A5) and the auto-flip switch: Settings - Aspect ratios.
- PROFILES ON A KEY: in the editor (before, while and after the region is drawn) the key left of 1 (Yo on a Russian keyboard, ` on an English one) walks through the profiles: Full, Ascetic, Minimal and your own. A capture starts with your saved setup until you change the order on the settings page Profiles (arrows or drag); after that it starts with the first profile of the list. Only this capture changes.
- BOTTOM PANEL PER PROFILE: Settings - Panels for a big region, the list at the top "Bottom panel layout": "One for all profiles" (the default, as before) or "Own for each profile". In the second mode the profile key switches the bottom panel too: Full keeps everything on two lines, Ascetic is one line (Copy, Save, Close and the drawing options), Minimal is only Copy, Save and Close. Every group can be dragged away, Copy, Save and Close too (Ctrl+C, Ctrl+S and Esc do the same); with nothing left the bottom panel is not shown. Each layout can be dragged and changed on the same page, and the panels pinned beside the settings (the left flag) show the profile you are editing; "Restore original" on the page Profiles also returns the layout.
- ALL KEYS: docs\HOTKEYS.txt lists every key and where it is changed.
- Shift / Ctrl / Alt while dragging (square, from the center, rounded...) - Settings - Shift / Ctrl / Alt; the wheel sets the strength.
- A click on a window selects the whole window; a fixed size can be set (Settings - Capture).

Editor
------
Tools: arrow, line, rectangle, ellipse, brush, marker, text, numbered steps, blur, fill, spotlight, magnifier, eraser, picker.
Switch tools on and off, change their order and keys. Presets 1-9 for every tool, recent colors, any color with transparency.
Text stays editable (font, bold, italic, alignment including justify, plate, outline for neural networks), shapes and text rotate,
several objects can be selected, aligned, moved together. Undo/redo, Ctrl+C copy, Ctrl+S save, Ctrl+Shift+S save as, Ctrl+P pin,
Ctrl+U upload, Ctrl+B beautiful look. Panels (tools and properties) stand outside the region; for a big region they adapt:
fewer lines, then smaller, then less important groups drop out one by one (rare buttons first), and at last move inside the region, as low as possible (Shift+Tab moves them by corners, clockwise).
The panels follow the frame LIVE while the region is drawn, moved or stretched. The bottom panel is two lines at most on every tool (the font / arrow / brush extras sit in the first line); the groups and their lines are set in Settings - Panels for a big region.

Beautiful screenshot
--------------------
The finished picture on a background with space, rounded corners and a shadow, in one of ELEVEN looks: Classic, Window (macOS-like), Windows, Browser,
Polaroid, Glass, Neon, Sticker, Stack, Device bezel, Cinema. Every look is a set of parameters: the pencil on its tile opens its settings (rotation, frame,
bar, colors, your own TEXT in the address bar / title / caption, own background...); built-in looks can be changed and reset, "+ Random look" adds a
new generated one, "Generate again" replaces a look by a fresh random one. "Random look" is also the first item of the list in the editor (a surprise
every time); what may fall out (dark looks on/off, bases, how unexpected) is set on the same page. Settings - Beautiful look: tiles with live examples, colors,
space, radius, shadow; at the right edge the picture "with the look" and "without it".
In the editor the button "Beautiful" is a switch: a click turns the look on or off for this capture (like Ctrl+B). HOLD the button (or use the right button)
for a short scrolling LIST of the looks ("No look" at the end); a choice turns the look on and shows ONE picture of it, made from your screenshot at 100% size,
and keeps it on the screen beside the region while the look is on (it follows your drawings; hovering over the list shows only a SAMPLE of each look). Ctrl+C and Save then use it, even when the look is off in the settings.
The look chosen last is remembered and used by Ctrl+B and by the next captures.

Settings window
---------------
Own title bar with two flags (pin the panel preview / outline the parts of the panels the page changes) and the current profile.
Search over all settings. The "Apply" button gets an orange outline while there are unsaved changes. Esc closes the window.
Preview pictures stand right of the settings they belong to. Export names the file SNAPter_Settings_<date>_<time>.json.
Base settings ("Save as base" / "Return to base"), factory settings, profiles (Full, Ascetic, Minimal and your own; a built-in
profile can be rewritten with your tools and restored). Fonts: a short list of your own fonts with a filter over all installed.
Option: open the settings on the page where they were closed.
Undo / redo arrows at the bottom right step through the changes that are not saved yet (one stable change = one step; Apply or closing starts a new history).
The panel preview (left flag) follows every change at once, in the same window; changing the language or the theme rebuilds the window without any flash.

Clipboard and files
-------------------
- Clipboard gets PNG and bitmap (pastes into Telegram, Word, Paint, browsers).
- Files go to %USERPROFILE%\Pictures\SNAPter, name SNAPter_<date>_<time>.png (folder, format and name are configurable).
- Optional upload to a folder, or to Google Drive with a link (the Google Drive part is NOT finished and was not tested well; the folder target works).

Structure
---------
Start SNAPter.bat / Build SNAPter.bat   launcher and manual build
tools\AutoBuild.ps1, BuildStamp.ps1, BootstrapSwap.ps1   self-rebuild helpers (NeedsBuild.ps1 is the old check)
app\                    C# project (.NET 8), see docs\STACK-AND-ARCHITECTURE.txt
app\publish\            built program (created by the build) and build.stamp
config\settings.default.json   defaults and switches (never changed by the program)
config\settings.json    your changes
config\settings.baseline.json  your base settings
config\themes\          slate, slate-soft, tide, sage, rose (+ your own, light and dark roles)
config\i18n\            ru.json, en.json (see docs\THEMES-AND-LANGUAGES.txt)
docs\                   notes
logs\                   app.log, selftest.txt, autobuild.log
assets\                 icons

Author and rights
-----------------
SNAPter is written and owned by its author, komario2023.
You may use, copy and change the program and its source code freely.
You may NOT present yourself as the author of the original program: keep the author's name and this notice
in every copy and in every program made from this code. The license is MIT, see the LICENSE file.
