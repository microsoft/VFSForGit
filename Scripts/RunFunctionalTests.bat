@ECHO OFF
IF "%1"=="" (SET "Configuration=Debug") ELSE (SET "Configuration=%1")

SETLOCAL
SET PATH=C:\Program Files\GVFS;C:\Program Files\Git\cmd;%PATH%

if not "%2"=="--test-gvfs-on-path" goto :startFunctionalTests

REM Force GVFS.FunctionalTests.exe to use the installed version of GVFS
del %~dp0\..\..\BuildOutput\GVFS.FunctionalTests\bin\x64\%Configuration%\netcoreapp2.0\GitHooksLoader.exe
del %~dp0\..\..\BuildOutput\GVFS.FunctionalTests\bin\x64\%Configuration%\netcoreapp2.0\GVFS.exe
del %~dp0\..\..\BuildOutput\GVFS.FunctionalTests\bin\x64\%Configuration%\netcoreapp2.0\GVFS.Hooks.exe
del %~dp0\..\..\BuildOutput\GVFS.FunctionalTests\bin\x64\%Configuration%\netcoreapp2.0\GVFS.ReadObjectHook.exe
del %~dp0\..\..\BuildOutput\GVFS.FunctionalTests\bin\x64\%Configuration%\netcoreapp2.0\GVFS.VirtualFileSystemHook.exe
del %~dp0\..\..\BuildOutput\GVFS.FunctionalTests\bin\x64\%Configuration%\netcoreapp2.0\GVFS.Mount.exe
del %~dp0\..\..\BuildOutput\GVFS.FunctionalTests\bin\x64\%Configuration%\netcoreapp2.0\GVFS.Service.exe
del %~dp0\..\..\BuildOutput\GVFS.FunctionalTests\bin\x64\%Configuration%\netcoreapp2.0\GVFS.Service.UI.exe

REM Same for GVFS.FunctionalTests.Windows.exe
del %~dp0\..\..\BuildOutput\GVFS.FunctionalTests.Windows\bin\x64\%Configuration%\GitHooksLoader.exe
del %~dp0\..\..\BuildOutput\GVFS.FunctionalTests.Windows\bin\x64\%Configuration%\GVFS.exe
del %~dp0\..\..\BuildOutput\GVFS.FunctionalTests.Windows\bin\x64\%Configuration%\GVFS.Hooks.exe
del %~dp0\..\..\BuildOutput\GVFS.FunctionalTests.Windows\bin\x64\%Configuration%\GVFS.ReadObjectHook.exe
del %~dp0\..\..\BuildOutput\GVFS.FunctionalTests.Windows\bin\x64\%Configuration%\GVFS.VirtualFileSystemHook.exe
del %~dp0\..\..\BuildOutput\GVFS.FunctionalTests.Windows\bin\x64\%Configuration%\GVFS.Mount.exe
del %~dp0\..\..\BuildOutput\GVFS.FunctionalTests.Windows\bin\x64\%Configuration%\GVFS.Service.exe
del %~dp0\..\..\BuildOutput\GVFS.FunctionalTests.Windows\bin\x64\%Configuration%\GVFS.Service.UI.exe

echo PATH = %PATH%
echo gvfs location:
where gvfs
echo GVFS.Service location:
where GVFS.Service
echo git location:
where git

:startFunctionalTests
dotnet %~dp0\..\..\BuildOutput\GVFS.FunctionalTests\bin\x64\%Configuration%\netcoreapp2.0\GVFS.FunctionalTests.dll %2 %3 %4 %5 || goto :endFunctionalTests
%~dp0\..\..\BuildOutput\GVFS.FunctionalTests.Windows\bin\x64\%Configuration%\GVFS.FunctionalTests.Windows.exe --windows-only %2 %3 %4 %5 || goto :endFunctionalTests

:endFunctionalTests
set error=%errorlevel%

call %~dp0\StopAllServices.bat

exit /b %error%