@echo off
rem SNAPter launcher. Double-click: the icon appears in the tray (near the clock).
rem The program rebuilds ITSELF at the start when the source files changed (whoever edited them), so nothing has to be built by hand.
rem Only the very first start (no exe yet) builds here. Exit: right-click the tray icon, Exit.
setlocal
set "ROOT=%~dp0"
set "EXE=%ROOT%app\publish\SNAPter.exe"

rem no exe, or an exe from before the self-rebuilding (no build.stamp yet): build once here, then the exe takes care of itself
if not exist "%EXE%" call "%ROOT%Build SNAPter.bat"
if not exist "%ROOT%app\publish\build.stamp" call "%ROOT%Build SNAPter.bat"
if not exist "%EXE%" exit /b 1
start "" "%EXE%" --restart
