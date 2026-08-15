@echo off
rem DeepSeek Harness Launcher - build script (ASCII only)
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set SRC=%~dp0src\launcher.cs
set OUT=%~dp0DeepSeekHarnessLauncher.exe

rem Kill running instance first (avoid exe lock)
taskkill /f /im DeepSeekHarnessLauncher.exe >nul 2>nul

if not exist "%CSC%" (
  echo [ERROR] csc.exe not found at %CSC%
  exit /b 1
)

echo Compiling...
"%CSC%" /nologo /target:winexe /codepage:65001 "/out:%OUT%" ^
  /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll ^
  "/r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll" ^
  "/r:C:\Windows\Microsoft.NET\assembly\GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll" ^
  "/r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll" ^
  "/r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll" ^
  "%SRC%"

if exist "%OUT%" (
  echo [OK] Built: %OUT%
) else (
  echo [FAILED] Compile error, see above.
  exit /b 1
)
endlocal
