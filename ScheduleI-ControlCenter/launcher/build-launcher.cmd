@echo off
setlocal
set "ROOT=%~dp0"
if not defined CSC (
  for /f "usebackq delims=" %%I in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.Roslyn.Compiler -find MSBuild\Current\Bin\Roslyn\csc.exe`) do if not defined CSC set "CSC=%%I"
)
set "FX=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"

if not exist "%CSC%" (
  echo Roslyn compiler not found: %CSC%
  exit /b 1
)

if not exist "%ROOT%bin" mkdir "%ROOT%bin"

"%CSC%" /nologo /target:winexe /platform:x64 /langversion:latest /optimize+ /deterministic+ ^
  /win32manifest:"%ROOT%app.manifest" ^
  /out:"%ROOT%bin\ScheduleIControlCenter.exe" ^
  /reference:"%FX%\System.dll" ^
  /reference:"%FX%\System.Core.dll" ^
  /reference:"%FX%\System.Drawing.dll" ^
  /reference:"%FX%\System.Windows.Forms.dll" ^
  "%ROOT%AssemblyInfo.cs" ^
  "%ROOT%Launcher.cs"

if errorlevel 1 exit /b %errorlevel%
echo Built: %ROOT%bin\ScheduleIControlCenter.exe
endlocal
