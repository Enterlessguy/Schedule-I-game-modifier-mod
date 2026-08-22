@echo off
setlocal
set "ROOT=%~dp0"
set "SRC=%ROOT%mod\ScheduleIControlBridge"
set "OUT=%SRC%\bin"
if not defined CSC (
  for /f "usebackq delims=" %%I in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.Roslyn.Compiler -find MSBuild\Current\Bin\Roslyn\csc.exe`) do if not defined CSC set "CSC=%%I"
)
set "RUNTIME=C:\Program Files\dotnet\shared\Microsoft.NETCore.App\6.0.36"
set "ML=%ROOT%..\MelonLoader\net6"
set "IL2CPP=%ROOT%..\MelonLoader\Il2CppAssemblies"

if not exist "%CSC%" (
  echo Roslyn compiler not found: %CSC%
  exit /b 1
)
if not exist "%RUNTIME%\System.Private.CoreLib.dll" (
  echo .NET 6 runtime not found: %RUNTIME%
  exit /b 1
)
if not exist "%OUT%" mkdir "%OUT%"
set "BUILD=%OUT%\ScheduleIControlBridge.build.dll"
set "FINAL=%OUT%\ScheduleIControlBridge.dll"
if exist "%BUILD%" del /q "%BUILD%"
if exist "%FINAL%" del /q "%FINAL%"

"%CSC%" /noconfig /nostdlib+ /nologo /target:library /platform:x64 /langversion:latest /optimize+ /deterministic+ ^
  /out:"%BUILD%" ^
  /reference:"%RUNTIME%\System.Private.CoreLib.dll" ^
  /reference:"%RUNTIME%\System.Runtime.dll" ^
  /reference:"%RUNTIME%\netstandard.dll" ^
  /reference:"%RUNTIME%\System.Collections.dll" ^
  /reference:"%RUNTIME%\System.Collections.Concurrent.dll" ^
  /reference:"%RUNTIME%\System.Console.dll" ^
  /reference:"%RUNTIME%\System.IO.dll" ^
  /reference:"%RUNTIME%\System.IO.FileSystem.dll" ^
  /reference:"%RUNTIME%\System.IO.Pipes.dll" ^
  /reference:"%RUNTIME%\System.IO.Pipes.AccessControl.dll" ^
  /reference:"%RUNTIME%\System.Linq.dll" ^
  /reference:"%RUNTIME%\System.Linq.Expressions.dll" ^
  /reference:"%RUNTIME%\System.ObjectModel.dll" ^
  /reference:"%RUNTIME%\System.Runtime.Extensions.dll" ^
  /reference:"%RUNTIME%\System.Security.Cryptography.Algorithms.dll" ^
  /reference:"%RUNTIME%\System.Security.Cryptography.Primitives.dll" ^
  /reference:"%RUNTIME%\System.Security.AccessControl.dll" ^
  /reference:"%RUNTIME%\System.Security.Claims.dll" ^
  /reference:"%RUNTIME%\System.Security.Principal.dll" ^
  /reference:"%RUNTIME%\System.Security.Principal.Windows.dll" ^
  /reference:"%RUNTIME%\System.Text.Encoding.dll" ^
  /reference:"%RUNTIME%\System.Text.Encoding.Extensions.dll" ^
  /reference:"%RUNTIME%\System.Threading.dll" ^
  /reference:"%RUNTIME%\System.Threading.Thread.dll" ^
  /reference:"%RUNTIME%\System.Threading.Tasks.dll" ^
  /reference:"%ML%\MelonLoader.dll" ^
  /reference:"%ML%\Il2CppInterop.Runtime.dll" ^
  /reference:"%ML%\0Harmony.dll" ^
  /reference:"%ML%\Newtonsoft.Json.dll" ^
  /reference:"%IL2CPP%\Assembly-CSharp.dll" ^
  /reference:"%IL2CPP%\Il2CppFishNet.Runtime.dll" ^
  /reference:"%IL2CPP%\Il2Cppmscorlib.dll" ^
  /reference:"%IL2CPP%\Il2CppSystem.dll" ^
  /reference:"%IL2CPP%\Il2CppScheduleOne.Core.dll" ^
  /reference:"%IL2CPP%\UnityEngine.CoreModule.dll" ^
  /reference:"%IL2CPP%\UnityEngine.InputLegacyModule.dll" ^
  /reference:"%IL2CPP%\Unity.InputSystem.dll" ^
  /reference:"%IL2CPP%\UnityEngine.UIModule.dll" ^
  /reference:"%IL2CPP%\UnityEngine.TextRenderingModule.dll" ^
  /reference:"%IL2CPP%\UnityEngine.UI.dll" ^
  "%SRC%\AssemblyInfo.cs" ^
  "%SRC%\CompatibilityDiagnostics.cs" ^
  "%SRC%\BridgeMod.cs" ^
  "%SRC%\PipeProtocol.cs" ^
  "%SRC%\MarketValueScaling.cs" ^
  "%SRC%\CustomerAllowanceScaling.cs" ^
  "%SRC%\SellPriceLimitManager.cs" ^
  "%SRC%\BusinessLaunderScaling.cs" ^
  "%SRC%\EffectsIntensityManager.cs" ^
  "%ROOT%src\ReleaseInfo.cs" ^
  "%SRC%\InventoryPagingModel.cs" ^
  "%SRC%\PlayerRuntimeSettings.cs" ^
  "%SRC%\GameOperations.cs"

if errorlevel 1 exit /b %errorlevel%
move /y "%BUILD%" "%FINAL%" >nul
if errorlevel 1 exit /b %errorlevel%
echo Built: %FINAL%
endlocal
