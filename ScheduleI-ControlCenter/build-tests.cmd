@echo off
setlocal
set "ROOT=%~dp0"
if not defined CSC (
  for /f "usebackq delims=" %%I in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.Roslyn.Compiler -find MSBuild\Current\Bin\Roslyn\csc.exe`) do if not defined CSC set "CSC=%%I"
)
set "FX=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%ROOT%tests\bin" mkdir "%ROOT%tests\bin"

"%CSC%" /nologo /target:exe /platform:x64 /langversion:latest /optimize+ /deterministic+ ^
  /out:"%ROOT%tests\bin\SmokeTests.exe" ^
  /reference:"%FX%\System.dll" ^
  /reference:"%FX%\System.Core.dll" ^
  /reference:"%FX%\System.Web.Extensions.dll" ^
  /reference:"%FX%\System.IO.Compression.dll" ^
  /reference:"%FX%\System.IO.Compression.FileSystem.dll" ^
  "%ROOT%src\Models.cs" ^
  "%ROOT%src\Diagnostics.cs" ^
  "%ROOT%src\ReleaseInfo.cs" ^
  "%ROOT%src\JsonUtil.cs" ^
  "%ROOT%src\GameEnvironment.cs" ^
  "%ROOT%src\SaveService.cs" ^
  "%ROOT%src\UpdateService.cs" ^
  "%ROOT%mod\ScheduleIControlBridge\InventoryPagingModel.cs" ^
  "%ROOT%tests\InventoryPagingTests.cs" ^
  "%ROOT%tests\DiagnosticsTests.cs" ^
  "%ROOT%tests\UpdateTests.cs" ^
  "%ROOT%tests\SmokeTests.cs"

if errorlevel 1 exit /b %errorlevel%
echo Built: %ROOT%tests\bin\SmokeTests.exe
endlocal
