#define GVFSVersion "0.2.1.0"
#define TelemetryPackageArchitecture "x64"
#ifndef TelemetryTestSid
#error TelemetryTestSid must identify the non-elevated harness user.
#endif

[Setup]
AppId=GVFS.InstallationTelemetry.Tests
AppName=GVFS installation telemetry tests
AppVersion=0.2.1.0
DefaultDirName={tmp}\GVFS.InstallationTelemetry.Tests
PrivilegesRequired=lowest
Uninstallable=no
CreateAppDir=no
DisableWelcomePage=yes
DisableDirPage=yes
DisableReadyPage=yes
OutputBaseFilename=InstallationTelemetryTests
Compression=none

[Code]
#include "..\InstallationTelemetry.iss"

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  TelemetryBegin(ExpandConstant('{param:RECORDDIR}'), '');
  TelemetryBegin(ExpandConstant('{param:RECORDDIR}'), '');
  if ExpandConstant('{param:SCENARIO}') = 'escape' then
    SaveStringToFile(AddBackslash(TelemetryDirectory) + 'escape.txt', TelemetryJson('quotes" slash\' + #10 + #$96EA), False);
  if ExpandConstant('{param:SCENARIO}') = 'cancel' then
    begin
      TelemetryOutcome := 'Cancelled';
      Result := 'Harness cancellation.';
    end
  else Result := '';
end;

procedure CurStepChanged(Step: TSetupStep);
begin
  if Step = ssInstall then
    begin
      TelemetryPhase := 'Install';
      if ExpandConstant('{param:SCENARIO}') = 'fail' then
        begin
          TelemetryOutcome := 'Failed';
          RaiseException('Harness failure.');
        end;
      if ExpandConstant('{param:SCENARIO}') = 'staged' then TelemetryMode := 'Staged';
      if ExpandConstant('{param:SCENARIO}') = 'remount' then
        begin
          TelemetryRemount := 'Failed';
          TelemetryScriptResult := 17;
        end;
    end;
  if Step = ssDone then
    begin
      TelemetryDone();
    end;
end;

procedure DeinitializeSetup();
begin
  TelemetryFinish(TelemetryOutcome);
end;

function GetCustomSetupExitCode(): Integer;
begin
  Result := TelemetryScriptResult;
end;
