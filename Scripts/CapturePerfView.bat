@if not defined _echo echo off
setlocal

set filename=%~n0
goto :parseargs

:showhelp
echo.
echo Captures a system wide PerfView while running a command then compresses it into a zip.
echo.
echo USAGE: %filename% command
echo.
echo EXAMPLES:
echo   %filename% git status
echo   %filename% git fetch
goto :end

:parseargs
if "%1" == "" goto :showhelp
if /i "%1" == "/?" goto :showhelp
if /i "%1" == "-?" goto :showhelp
if /i "%1" == "/h" goto :showhelp
if /i "%1" == "-h" goto :showhelp
if /i "%1" == "/help" goto :showhelp
if /i "%1" == "-help" goto :showhelp

:: Find the given command on the path, then look for a .PDB file next to it
:VerifyPDB
set P2=.;%PATH%
for %%e in (%PATHEXT%) do @for %%i in (%~n1%%e) do @if NOT "%%~$P2:i"=="" if NOT exist "%%~dpn$P2:i.pdb" (
	echo Unable to locate PDB file %%~dpn$P2:i.pdb. Aborting %filename% 1>&2
	exit /B 1
)

:VerifyPerfView
where /q perfview || (
	echo Please see the PerfView GitHub Download Page to download an up-to-date version 1>&2
	echo of PerfView and copy it to a directory in your path. 1>&2
	echo. 1>&2
	echo https://github.com/Microsoft/perfview/blob/master/documentation/Downloading.md 1>&2
	exit /B 2
)

:: Generate output filenames
if NOT "%_NTUSER%" == "" (
	set perfviewfilename=%_NTUSER%-%~n1-%2
) ELSE (
	if NOT "%USERNAME%" == "" (
		set perfviewfilename=%USERNAME%-%~n1-%2
	) ELSE (
		set perfviewfilename=%~n1-%2
	)
)
set perfviewstartlog=%perfviewfilename%.start.log.txt
set perfviewstoplog=%perfviewfilename%.end.log.txt

:: Capture the perfview without requiring any human intervention
:CapturePerfView
echo Capture perf view for '%*'...
perfview start /AcceptEULA /NoGui /NoNGenRundown /Merge /Zip /Providers:*Microsoft.Git.GVFS:@StacksEnabled=true,*Microsoft.Internal.Git.Plugin:@StacksEnabled=true,*Microsoft.OSGENG.Testing.GitMsWrapper:@StacksEnabled=true /kernelEvents=default+FileIOInit /logfile:"%perfviewstartlog%" "%perfviewfilename%" || goto :HandlePerfViewStartError
echo.
set STARTTIME=%TIME%
%*
set ENDTIME=%TIME%
echo.
CALL :PrintElapsedTime

:: Merge perfview into ZIP file
echo Merging and compressing perf view...
perfview stop /AcceptEULA /NoGui /NoNGenRundown /Merge /Zip /Providers:*Microsoft.Git.GVFS:@StacksEnabled=true,*Microsoft.Internal.Git.Plugin:@StacksEnabled=true,*Microsoft.OSGENG.Testing.GitMsWrapper:@StacksEnabled=true /kernelEvents=default+FileIOInit /logfile:"%perfviewstoplog%" || goto :HandlePerfViewStopError
CALL :CheckForFile
echo PerfView trace can be found in "%perfviewfilename%.etl.zip"
goto :end

:HandlePerfViewStartError
echo Could not start perfview, please see %perfviewstartlog% for details.
EXIT /B 3

:HandlePerfViewStopError
echo Could not stop perfview, please see %perfviewstoplog% for details.
EXIT /B 4

:: Now wait for perfview to complete writing out the file
:CheckForFile
IF EXIST "%perfviewfilename%.etl.zip" EXIT /B 0
TIMEOUT /T 1 >nul
goto :CheckForFile

:PrintElapsedTime
:: Change formatting for the start and end times
for /F "tokens=1-4 delims=:.," %%a in ("%STARTTIME%") do (
   set /A "start=(((%%a*60)+1%%b %% 100)*60+1%%c %% 100)*100+1%%d %% 100"
)

for /F "tokens=1-4 delims=:.," %%a in ("%ENDTIME%") do (
   set /A "end=(((%%a*60)+1%%b %% 100)*60+1%%c %% 100)*100+1%%d %% 100"
)

:: Calculate the elapsed time by subtracting values
set /A elapsed=end-start

:: Format the results for output
set /A hh=elapsed/(60*60*100), rest=elapsed%%(60*60*100), mm=rest/(60*100), rest%%=60*100, ss=rest/100, cc=rest%%100
if %hh% lss 10 set hh=0%hh%
if %mm% lss 10 set mm=0%mm%
if %ss% lss 10 set ss=0%ss%
if %cc% lss 10 set cc=0%cc%

set DURATION=%hh%:%mm%:%ss%.%cc%
echo Command duration : %DURATION%
EXIT /B 0

:end
endlocal
