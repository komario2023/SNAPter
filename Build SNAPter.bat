@echo off
rem Builds SNAPter into app\publish (needs the .NET 8 SDK or newer: dotnet --list-sdks).
setlocal
set "ROOT=%~dp0"
echo Building SNAPter...
dotnet publish "%ROOT%app\Snapter.csproj" -c Release -r win-x64 --self-contained false -nologo -v q -o "%ROOT%app\publish"
if errorlevel 1 (
    echo.
    echo Build failed. Check that the .NET SDK is installed: dotnet --list-sdks
    pause
    exit /b 1
)
rem the fingerprint of the sources: the program does not rebuild itself again at the next start
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%tools\BuildStamp.ps1" -Root "%ROOT%." -Write >nul
echo Done: %ROOT%app\publish\SNAPter.exe
