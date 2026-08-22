@echo off
rem Build DeepSeekHarness-Setup.exe (ASCII only)
rem Requires: launcher repo at ..\launcher (build.bat), node.zip & dsh.zip in this dir
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set DIR=%~dp0
set OUT=%DIR%DeepSeekHarness-Setup.exe
set RES_LAUNCHER=%DIR%..\DeepSeekHarnessLauncher.exe
set RES_ICO=%DIR%..\DeepSeek Harness.ico

rem 1. Build launcher first
pushd %DIR%..
call build.bat
if errorlevel 1 (
  echo [ERROR] launcher build failed
  exit /b 1
)
popd

rem 2. Ensure resources exist
if not exist "%RES_LAUNCHER%" ( echo [ERROR] launcher.exe missing & exit /b 1 )
if not exist "%RES_ICO%" ( echo [ERROR] ico missing & exit /b 1 )
if not exist "%DIR%node.zip" ( echo [ERROR] node.zip missing - download: & echo   curl -L -o node.zip https://npmmirror.com/mirrors/node/v22.23.2/node-v22.23.2-win-x64.zip & exit /b 1 )
if not exist "%DIR%dsh.zip" ( echo [ERROR] dsh.zip missing & exit /b 1 )

rem 2b. Pack the dsh vision patches (writeback.ps1 + two patched index.js)
if not exist "%DIR%..\patches\dsh-vision\writeback.ps1" ( echo [ERROR] vision patch missing & exit /b 1 )
powershell -NoProfile -Command "Compress-Archive -Path '%DIR%..\patches\dsh-vision\*' -DestinationPath '%DIR%dsh-vision.zip' -Force"
if not exist "%DIR%dsh-vision.zip" ( echo [ERROR] dsh-vision.zip packaging failed & exit /b 1 )

rem 3. Compile setup with embedded resources
"%CSC%" /nologo /target:winexe /codepage:65001 "/out:%OUT%" ^
  /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:Microsoft.CSharp.dll ^
  "/r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll" ^
  "/r:C:\Windows\Microsoft.NET\assembly\GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll" ^
  "/r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll" ^
  "/r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll" ^
  "/resource:%RES_LAUNCHER%,launcher.exe" ^
  "/resource:%RES_ICO%,icon.ico" ^
  "/resource:%DIR%node.zip,node.zip" ^
  "/resource:%DIR%dsh.zip,dsh.zip" ^
  "/resource:%DIR%dsh-vision.zip,dsh-vision.zip" ^
  "%DIR%Setup.cs"

if exist "%OUT%" (
  echo [OK] Built: %OUT%
) else (
  echo [FAILED] Compile error.
  exit /b 1
)
endlocal
