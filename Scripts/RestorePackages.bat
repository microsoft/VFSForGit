@ECHO OFF
SETLOCAL

SET nuget="%~dp0\..\..\.tools\nuget.exe"
IF NOT EXIST %nuget% (
  mkdir %nuget%\..
  powershell -ExecutionPolicy Bypass -Command "Invoke-WebRequest 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile %nuget%"
)

%nuget% restore %~dp0\..\GVFS.sln || exit /b 1

dotnet restore %~dp0\..\GVFS\GVFS.Common\GVFS.Common.csproj || exit /b 1
dotnet restore %~dp0\..\GVFS\GVFS.Virtualization\GVFS.Virtualization.csproj || exit /b 1

ENDLOCAL