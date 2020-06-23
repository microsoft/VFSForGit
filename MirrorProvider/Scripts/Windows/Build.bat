IF "%1"=="" (SET "Configuration=Debug") ELSE (SET "Configuration=%1")

SET SRC=%~dp0\..\..\..
SET ROOT=%SRC%\..
SET SLN=%SRC%\MirrorProvider\MirrorProvider.sln

FOR /F "tokens=* USEBACKQ" %%F IN (`where nuget.exe`) DO (
	SET nuget=%%F
	ECHO Found nuget.exe at '%%F'
)

%nuget% restore %SLN%
dotnet restore %SLN% /p:Configuration="%Configuration%.Windows" --packages %ROOT%\packages
dotnet build %SLN% --configuration %Configuration%.Windows