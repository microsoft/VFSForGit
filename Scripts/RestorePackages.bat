@ECHO OFF
CALL %~dp0\InitializeEnvironment.bat || EXIT /b 10

SETLOCAL

SET nuget="%VFS_TOOLSDIR%\nuget.exe"
IF NOT EXIST %nuget% (
  mkdir %nuget%\..
  powershell -ExecutionPolicy Bypass -Command "Invoke-WebRequest 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile %nuget%"
)

%nuget% restore %VFS_SRCDIR%\GVFS.sln || exit /b 1

SET VCTargetsPath=C:\Program Files (x86)\MSBuild\Microsoft.Cpp\v4.0\V140
dotnet restore %VFS_SRCDIR%\GVFS.sln /p:Configuration=%SolutionConfiguration% --packages %VFS_PACKAGESDIR% || exit /b 1


ENDLOCAL