IF "%1"=="" (SET "Configuration=Debug") ELSE (SET "Configuration=%1")

SET SRC=%~dp0\..\..\..
SET ROOT=%SRC%\..
SET SLN=%SRC%\MirrorProvider\MirrorProvider.sln

SET nuget="%ROOT%\.tools\nuget.exe"
IF NOT EXIST %nuget% (
  mkdir %nuget%\..
  powershell -ExecutionPolicy Bypass -Command "Invoke-WebRequest 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile %nuget%"
)

%nuget% restore %SLN%
dotnet restore %SLN% /p:Configuration="%Configuration%.Windows" --packages %ROOT%\packages
dotnet build %SLN% --configuration %Configuration%.Windows