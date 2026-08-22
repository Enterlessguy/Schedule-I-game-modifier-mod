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

if not exist "%ROOT%dist" mkdir "%ROOT%dist"

"%CSC%" /nologo /target:winexe /platform:x64 /langversion:latest /optimize+ /deterministic+ ^
  /win32manifest:"%ROOT%src\app.manifest" ^
  /win32icon:"%ROOT%src\assets\app.ico" ^
  /resource:"%ROOT%src\assets\IntelDatabasepagelogo.png",ScheduleIControlCenter.IntelDatabaseLogo.png ^
  /resource:"%ROOT%src\assets\font.ttf",ScheduleIControlCenter.WelcomeFont.ttf ^
  /out:"%ROOT%dist\ScheduleIControlCenter.exe" ^
  /reference:"%FX%\System.dll" ^
  /reference:"%FX%\System.Core.dll" ^
  /reference:"%FX%\System.Drawing.dll" ^
  /reference:"%FX%\System.Windows.Forms.dll" ^
  /reference:"%FX%\System.Web.Extensions.dll" ^
  "%ROOT%src\AssemblyInfo.cs" ^
  "%ROOT%src\Models.cs" ^
  "%ROOT%src\Diagnostics.cs" ^
  "%ROOT%src\JsonUtil.cs" ^
  "%ROOT%src\GameEnvironment.cs" ^
  "%ROOT%src\SaveService.cs" ^
  "%ROOT%src\ReleaseInfo.cs" ^
  "%ROOT%src\BridgeClient.cs" ^
  "%ROOT%src\IntroSplashForm.cs" ^
  "%ROOT%src\MainForm.Theme.cs" ^
  "%ROOT%src\MainForm.cs" ^
  "%ROOT%src\MainForm.NewTabs.cs" ^
  "%ROOT%src\Program.cs"

if errorlevel 1 exit /b %errorlevel%

"%CSC%" /nologo /target:exe /platform:x64 /langversion:latest /optimize+ /deterministic+ ^
  /win32icon:"%ROOT%src\assets\app.ico" ^
  /out:"%ROOT%dist\ScheduleIControlCenter.Cli.exe" ^
  /reference:"%FX%\System.dll" ^
  /reference:"%FX%\System.Core.dll" ^
  /reference:"%FX%\System.Web.Extensions.dll" ^
  "%ROOT%src\AssemblyInfo.cs" ^
  "%ROOT%src\Models.cs" ^
  "%ROOT%src\Diagnostics.cs" ^
  "%ROOT%src\ReleaseInfo.cs" ^
  "%ROOT%src\JsonUtil.cs" ^
  "%ROOT%src\GameEnvironment.cs" ^
  "%ROOT%src\SaveService.cs" ^
  "%ROOT%src\CliProgram.cs"

if errorlevel 1 exit /b %errorlevel%
echo Built: %ROOT%dist\ScheduleIControlCenter.exe
echo Built: %ROOT%dist\ScheduleIControlCenter.Cli.exe
endlocal
