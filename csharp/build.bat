@echo off
REM ============================================================
REM  Build Network Watchdog  (Windows 7 SP1 / 8 / 8.1 / 10 / 11)
REM  Compiles NetworkWatchdog.cs into NetworkWatchdog.exe using
REM  the built-in Windows C# compiler (csc.exe) - NO installation
REM  of Visual Studio or any SDK is required.
REM
REM  Requirement: .NET Framework 4.x  (built into Windows 10/11;
REM  for Windows 7 SP1 install it once from Microsoft if missing)
REM ============================================================

setlocal
cd /d "%~dp0"
chcp 65001 >nul

echo.
echo  ============================================================
echo   Network Watchdog - Build (csc.exe)
echo  ============================================================
echo.

REM ---- Ensure source file is UTF-8 with BOM so csc reads Persian text correctly
powershell -NoProfile -ExecutionPolicy Bypass -Command "$f='NetworkWatchdog.cs'; $b=[IO.File]::ReadAllBytes($f); if(!($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)){[IO.File]::WriteAllBytes($f, [byte[]](0xEF,0xBB,0xBF)+$b); Write-Host 'UTF-8 BOM added.'}" >nul 2>&1

REM ---- Locate the .NET Framework 4.x compiler
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo  [ERROR] .NET Framework 4.x compiler not found!
    echo  Please install .NET Framework 4.x and try again:
    echo  https://dotnet.microsoft.com/download/dotnet-framework/net48
    echo.
    pause
    exit /b 1
)

echo  Using compiler: %CSC%
echo.

"%CSC%" ^
    /nologo ^
    /codepage:65001 ^
    /target:winexe ^
    /out:NetworkWatchdog.exe ^
    /optimize+ ^
    /win32icon:app.ico ^
    /win32manifest:app.manifest ^
    /r:System.dll ^
    /r:System.Core.dll ^
    /r:System.Drawing.dll ^
    /r:System.Windows.Forms.dll ^
    /r:System.Management.dll ^
    /r:System.Xml.dll ^
    NetworkWatchdog.cs

if errorlevel 1 (
    echo.
    echo  [FAILED] Build returned errors. See messages above.
    pause
    exit /b 1
)

echo.
echo  [OK] NetworkWatchdog.exe built successfully!
echo.
echo  How to use:
echo    1. Right-click NetworkWatchdog.exe ^> Run as administrator
echo       (UAC prompt appears once at first run)
echo    2. The app registers itself in Windows startup automatically
echo    3. Look for the green tray icon next to the clock
echo.
pause
