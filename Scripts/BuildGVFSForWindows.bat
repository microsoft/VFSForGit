@ECHO OFF
SETLOCAL

IF "%1"=="" (SET "Configuration=Debug") ELSE (SET "Configuration=%1")
IF "%2"=="" (SET "GVFSVersion=0.2.173.2") ELSE (SET "GVFSVersion=%2")

SET SolutionConfiguration=%Configuration%.Windows

SET nuget="%~dp0\..\..\.tools\nuget.exe"
IF NOT EXIST %nuget% (
  mkdir %nuget%\..
  powershell -ExecutionPolicy Bypass -Command "Invoke-WebRequest 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile %nuget%"
)

:: Acquire vswhere to find dev15 installations reliably.
SET vswherever=2.5.2
%nuget% install vswhere -Version %vswherever% || exit /b 1
SET vswhere=%~dp0..\..\packages\vswhere.%vswherever%\tools\vswhere.exe

:: Use vswhere to find the latest VS installation (including prerelease installations) with the msbuild component.
:: See https://github.com/Microsoft/vswhere/wiki/Find-MSBuild
for /f "usebackq tokens=*" %%i in (`%vswhere% -all -prerelease -latest -products * -requires Microsoft.Component.MSBuild Microsoft.VisualStudio.Workload.ManagedDesktop Microsoft.VisualStudio.Workload.NativeDesktop Microsoft.VisualStudio.Workload.NetCoreTools Microsoft.Component.NetFX.Core.Runtime Microsoft.VisualStudio.Component.Windows10SDK.10240 -property installationPath`) do (
  set VsInstallDir=%%i
)

IF NOT DEFINED VsInstallDir (
  echo ERROR: Could not locate a Visual Studio installation with required components.
  echo Refer to Readme.md for a list of the required Visual Studio components.
  exit /b 10
)

SET msbuild="%VsInstallDir%\MSBuild\15.0\Bin\amd64\msbuild.exe"
IF NOT EXIST %msbuild% (
  echo ERROR: Could not find msbuild
  exit /b 1
)

%msbuild% %~dp0\..\GVFS.sln /p:GVFSVersion=%GVFSVersion% /p:Configuration=%SolutionConfiguration% /p:Platform=x64 || exit /b 1

ENDLOCAL