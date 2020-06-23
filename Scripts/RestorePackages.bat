@ECHO OFF
CALL %~dp0\InitializeEnvironment.bat || EXIT /b 10

SETLOCAL

IF "%1"=="" (SET "Configuration=Debug") ELSE (SET "Configuration=%1")
 
SET SolutionConfiguration=%Configuration%.Windows

FOR /F "tokens=* USEBACKQ" %%F IN (`where nuget.exe`) DO (
	SET nuget=%%F
	ECHO Found nuget.exe at '%%F'
)

dotnet restore %VFS_SRCDIR%\GVFS.sln /p:Configuration=%SolutionConfiguration% /p:VCTargetsPath="C:\Program Files (x86)\MSBuild\Microsoft.Cpp\v4.0\V140" --packages %VFS_PACKAGESDIR% || exit /b 1

%nuget% restore %VFS_SRCDIR%\GVFS.sln || exit /b 1

ENDLOCAL