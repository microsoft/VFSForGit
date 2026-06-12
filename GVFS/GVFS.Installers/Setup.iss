; This script requires Inno Setup 6 or later to compile
; The Inno Setup Compiler (and IDE) can be found at http://www.jrsoftware.org/isinfo.php

; General documentation on how to use InnoSetup scripts: http://www.jrsoftware.org/ishelp/index.php

#define MyAppName "VFS for Git"
#define MyAppInstallerVersion GetFileVersion(LayoutDir + "\GVFS.exe")
#define MyAppPublisher "Microsoft"
#define MyAppPublisherURL "http://www.microsoft.com"
#define MyAppURL "https://github.com/microsoft/VFSForGit"
#define MyAppExeName "GVFS.exe"
#define EnvironmentKey "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"
#define UserEnvironmentKey "Environment"
#define FileSystemKey "SYSTEM\CurrentControlSet\Control\FileSystem"
#define GvFltAutologgerKey "SYSTEM\CurrentControlSet\Control\WMI\Autologger\Microsoft-Windows-Git-Filter-Log"
#define GVFSConfigFileName "gvfs.config"
#define GVFSStatuscacheTokenFileName "EnableGitStatusCacheToken.dat"
#define ServiceName "GVFS.Service"
#define ProjFSTaskPath "\GVFS\EnableProjFSOnAllDrives"

[Setup]
AppId={{489CA581-F131-4C28-BE04-4FB178933E6D}
AppName={#MyAppName}
AppVersion={#MyAppInstallerVersion}
VersionInfoVersion={#MyAppInstallerVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppPublisherURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppCopyright=Copyright (c) Microsoft 2021
BackColor=clWhite
BackSolid=yes
DefaultDirName={pf}\{#MyAppName}
OutputBaseFilename=SetupGVFS.{#GVFSVersion}
OutputDir=Setup
Compression=lzma2
InternalCompressLevel=ultra64
SolidCompression=yes
MinVersion=10.0.17763
DisableDirPage=yes
DisableReadyPage=yes
SetupIconFile="{#LayoutDir}\GitVirtualFileSystem.ico"
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
WizardImageStretch=no
WindowResizable=no
CloseApplications=no
ChangesEnvironment=yes
RestartIfNeededByRun=yes
; Allow the installer to run as non-admin when the user passes
; /CURRENTUSER on the command line. Without /CURRENTUSER, the
; installer runs as admin (the default, matching existing behavior).
PrivilegesRequiredOverridesAllowed=commandline

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl";

[Types]
Name: "full"; Description: "Full installation"; Flags: iscustom;

[Components]

[InstallDelete]
; Delete old dependencies from VS 2015 VC redistributables
Type: files; Name: "{app}\ucrtbase.dll"

[Files]
; System-mode install: all files go to {app}, service gets AfterInstall callback
DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Source:"{#LayoutDir}\*"; Check: IsSystemModeNormalInstall
DestDir: "{app}"; Flags: ignoreversion; Source:"{#LayoutDir}\GVFS.Service.exe"; AfterInstall: InstallGVFSService; Check: IsSystemModeNormalInstall
; System-mode staging install: most files go to {app}\PendingUpgrade, but GVFS.Service.exe
; goes directly to {app} so the restarted service has PendingUpgradeHandler code.
; The service is briefly stopped/restarted (mounts are independent processes).
DestDir: "{app}\PendingUpgrade"; Flags: ignoreversion recursesubdirs; Source:"{#LayoutDir}\*"; Check: IsSystemModeStagingInstall
DestDir: "{app}"; Flags: ignoreversion; Source:"{#LayoutDir}\GVFS.Service.exe"; Check: IsSystemModeStagingInstall
; User-mode install: payload goes to %LocalAppData%\GVFS\Versions\<version>\.
; The Current junction and user PATH are set up in CurStepChanged(ssPostInstall).
DestDir: "{localappdata}\GVFS\Versions\{#GVFSVersion}"; Flags: ignoreversion recursesubdirs; Source:"{#LayoutDir}\*"; Check: IsUserModeCheck
; Pre-built EnableProjFSOnAllDrives task XML (embedded, not deployed).
; Extracted to temp at install time for schtasks /Create /XML.
#ifdef ProjFSTaskXml
Source: "{#ProjFSTaskXml}"; Flags: dontcopy
#endif

[Dirs]
Name: "{app}\ProgramData\{#ServiceName}"; Permissions: users-readexec; Check: IsSystemModeInstall

[UninstallDelete]
; Deletes the entire installation directory, including files and subdirectories
Type: filesandordirs; Name: "{app}";
Type: filesandordirs; Name: "{commonappdata}\GVFS\GVFS.Upgrade";

[Registry]
; System-mode: add install dir to system PATH and set NtfsEnableDetailedCleanupResults
Root: HKLM; Subkey: "{#EnvironmentKey}"; \
    ValueType: expandsz; ValueName: "PATH"; ValueData: "{olddata};{app}"; \
    Check: IsSystemModeNeedsAddPath

Root: HKLM; Subkey: "{#FileSystemKey}"; \
    ValueType: dword; ValueName: "NtfsEnableDetailedCleanupResults"; ValueData: "1"; \
    Check: IsSystemModeWindows10PriorToCreatorsUpdate

Root: HKLM; SubKey: "{#GvFltAutologgerKey}"; Flags: deletekey; Check: IsSystemModeInstall

; User-mode: add Current junction dir to user PATH
Root: HKCU; Subkey: "{#UserEnvironmentKey}"; \
    ValueType: expandsz; ValueName: "PATH"; ValueData: "{olddata};{localappdata}\GVFS\Current"; \
    Check: UserModeNeedsAddPath

; User-mode: redirect GVFS data paths to %LocalAppData%\GVFS so
; the user has write access without admin elevation.
Root: HKCU; Subkey: "{#UserEnvironmentKey}"; \
    ValueType: string; ValueName: "GVFS_SECURE_DATA_ROOT"; ValueData: "{localappdata}\GVFS"; \
    Check: IsUserModeCheck

Root: HKCU; Subkey: "{#UserEnvironmentKey}"; \
    ValueType: string; ValueName: "GVFS_COMMON_APPDATA_ROOT"; ValueData: "{localappdata}\GVFS"; \
    Check: IsUserModeCheck

[Code]
var
  ExitCode: Integer;
  KeepMountsRunning: Boolean;
  IsUserModeInstall: Boolean;
  IsAdminStage: Boolean;

function InitializeSetup(): Boolean;
begin
  Result := True;
  IsUserModeInstall := not IsAdmin();
  IsAdminStage := (ExpandConstant('{param:ADMINSTAGE|false}') = 'true');

  if IsAdminStage then
    begin
      Log('InitializeSetup: /ADMINSTAGE mode - will run admin setup only');
    end
  else if IsUserModeInstall then
    begin
      Log('InitializeSetup: User-mode install detected (non-elevated)');
    end
  else
    begin
      Log('InitializeSetup: System-mode install (elevated)');
    end;
end;

function IsNormalInstall(): Boolean;
begin
  Result := not KeepMountsRunning;
end;

function IsStagingInstall(): Boolean;
begin
  Result := KeepMountsRunning;
end;

function IsUserModeCheck(): Boolean;
begin
  Result := IsUserModeInstall and (not IsAdminStage);
end;

function IsSystemModeNormalInstall(): Boolean;
begin
  Result := (not IsUserModeInstall) and (not IsAdminStage) and (not KeepMountsRunning);
end;

function IsSystemModeStagingInstall(): Boolean;
begin
  Result := (not IsUserModeInstall) and (not IsAdminStage) and KeepMountsRunning;
end;

function IsSystemModeInstall(): Boolean;
begin
  Result := (not IsUserModeInstall) and (not IsAdminStage);
end;

function NeedsAddPath(Param: string): boolean;
var
  OrigPath: string;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE,
    '{#EnvironmentKey}',
    'PATH', OrigPath)
  then begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + Param + ';', ';' + OrigPath + ';') = 0;
end;

function IsWindows10VersionPriorToCreatorsUpdate(): Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result := (Version.Major = 10) and (Version.Minor = 0) and (Version.Build < 15063);
end;

function IsSystemModeNeedsAddPath(): Boolean;
begin
  Result := (not IsUserModeInstall) and NeedsAddPath(ExpandConstant('{app}'));
end;

function IsSystemModeWindows10PriorToCreatorsUpdate(): Boolean;
begin
  Result := (not IsUserModeInstall) and IsWindows10VersionPriorToCreatorsUpdate();
end;

function UserModeNeedsAddPath(): Boolean;
var
  OrigPath: string;
  TargetDir: string;
begin
  Result := False;
  if not IsUserModeInstall then exit;
  TargetDir := ExpandConstant('{localappdata}\GVFS\Current');
  if not RegQueryStringValue(HKEY_CURRENT_USER, '{#UserEnvironmentKey}', 'PATH', OrigPath) then
    begin
      Result := True;
      exit;
    end;
  Result := Pos(';' + Uppercase(TargetDir) + ';', ';' + Uppercase(OrigPath) + ';') = 0;
end;

procedure RemovePath(Path: string);
var
  Paths: string;
  PathMatchIndex: Integer;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, '{#EnvironmentKey}', 'Path', Paths) then
    begin
      Log('PATH not found');
    end
  else
    begin
      Log(Format('PATH is [%s]', [Paths]));

      PathMatchIndex := Pos(';' + Uppercase(Path) + ';', ';' + Uppercase(Paths) + ';');
      if PathMatchIndex = 0 then
        begin
          Log(Format('Path [%s] not found in PATH', [Path]));
        end
      else
        begin
          Delete(Paths, PathMatchIndex - 1, Length(Path) + 1);
          Log(Format('Path [%s] removed from PATH => [%s]', [Path, Paths]));

          if RegWriteStringValue(HKEY_LOCAL_MACHINE, '{#EnvironmentKey}', 'Path', Paths) then
            begin
              Log('PATH written');
            end
          else
            begin
              Log('Error writing PATH');
            end;
        end;
    end;
end;

procedure StopService(ServiceName: string);
var
  ResultCode: integer;
begin
  Log('StopService: stopping: ' + ServiceName);
  if not Exec(ExpandConstant('{sys}\SC.EXE'), 'stop ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      Log('StopService: Failed to launch sc.exe');
      RaiseException('Fatal: Could not stop service: ' + ServiceName);
    end;
  // 1060 = service not installed, 1062 = service not started
  if (ResultCode <> 0) and (ResultCode <> 1060) and (ResultCode <> 1062) then
    begin
      Log('StopService: sc stop returned error code ' + IntToStr(ResultCode));
      RaiseException('Fatal: Could not stop service: ' + ServiceName + ' (exit code ' + IntToStr(ResultCode) + ')');
    end;
end;

procedure WaitForServiceProcessToExit(ServiceName: string);
var
  ResultCode: integer;
  Attempts: integer;
  TempFile: string;
  QueryOutput: ansiString;
begin
  // sc stop/delete returns before the service process actually exits.
  // Poll sc query until the service is fully gone (1060) or stopped.
  Attempts := 0;
  TempFile := ExpandConstant('{tmp}\~scquery.txt');
  while Attempts < 30 do
    begin
      if Exec(ExpandConstant('{cmd}'), '/C "' + ExpandConstant('{sys}\SC.EXE') + '" query ' + ServiceName + ' > "' + TempFile + '" 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
        begin
          // 1060 = service does not exist (fully deleted and process exited)
          if ResultCode = 1060 then
            begin
              Log('WaitForServiceProcessToExit: Service no longer exists');
              break;
            end;
          if LoadStringFromFile(TempFile, QueryOutput) then
            begin
              if Pos('STOPPED', QueryOutput) > 0 then
                begin
                  Log('WaitForServiceProcessToExit: Service is stopped');
                  break;
                end;
            end;
        end
      else
        begin
          Log('WaitForServiceProcessToExit: sc query failed, assuming service is gone');
          break;
        end;
      Attempts := Attempts + 1;
      Log('WaitForServiceProcessToExit: Waiting for service to stop (attempt ' + IntToStr(Attempts) + ')');
      Sleep(1000);
    end;
  if Attempts >= 30 then
    begin
      if LoadStringFromFile(TempFile, QueryOutput) then
        Log('WaitForServiceProcessToExit: Timed out. Last sc query output: ' + QueryOutput)
      else
        Log('WaitForServiceProcessToExit: Timed out waiting for service to stop');
    end;
  DeleteFile(TempFile);
end;

procedure UninstallService(ServiceName: string; ShowProgress: boolean);
var
  ResultCode: integer;
begin
  if Exec(ExpandConstant('{sys}\SC.EXE'), 'query ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode <> 1060) then
    begin
      Log('UninstallService: uninstalling service: ' + ServiceName);
      if (ShowProgress) then
        begin
          WizardForm.StatusLabel.Caption := 'Uninstalling service: ' + ServiceName;
          WizardForm.ProgressGauge.Style := npbstMarquee;
        end;

      try
        StopService(ServiceName);

        if not Exec(ExpandConstant('{sys}\SC.EXE'), 'delete ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
          begin
            Log('UninstallService: Could not uninstall service: ' + ServiceName);
            RaiseException('Fatal: Could not uninstall service: ' + ServiceName);
          end;

        WaitForServiceProcessToExit(ServiceName);

        if (ShowProgress) then
          begin
            WizardForm.StatusLabel.Caption := 'Waiting for pending ' + ServiceName + ' deletion to complete. This may take a while.';
          end;

      finally
        if (ShowProgress) then
          begin
            WizardForm.ProgressGauge.Style := npbstNormal;
          end;
      end;

    end;
end;

procedure WriteOnDiskVersion16CapableFile();
var
  FilePath: string;
begin
  FilePath := ExpandConstant('{app}\OnDiskVersion16CapableInstallation.dat');
  if not FileExists(FilePath) then
    begin
      Log('WriteOnDiskVersion16CapableFile: Writing file ' + FilePath);
      SaveStringToFile(FilePath, '', False);
    end
end;

procedure InstallGVFSService();
var
  ResultCode: integer;
  StatusText: string;
  InstallSuccessful: Boolean;
begin
  InstallSuccessful := False;

  StatusText := WizardForm.StatusLabel.Caption;
  WizardForm.StatusLabel.Caption := 'Installing GVFS.Service.';
  WizardForm.ProgressGauge.Style := npbstMarquee;

  // Spaces after the equal signs are REQUIRED.
  // https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sc-create#remarks
  try
    // We must add additional quotes to the binPath to ensure that they survive argument parsing.
    // Without quotes, sc.exe will try to start a file located at C:\Program if it exists.
    if Exec(ExpandConstant('{sys}\SC.EXE'), ExpandConstant('create GVFS.Service binPath= "\"{app}\GVFS.Service.exe\"" start= auto'), '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
      begin
        if Exec(ExpandConstant('{sys}\SC.EXE'), 'failure GVFS.Service reset= 30 actions= restart/10/restart/5000//1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
          begin
            if Exec(ExpandConstant('{sys}\SC.EXE'), 'start GVFS.Service', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
              begin
                InstallSuccessful := True;
              end;
          end;
      end;

    WriteOnDiskVersion16CapableFile();
  finally
    WizardForm.StatusLabel.Caption := StatusText;
    WizardForm.ProgressGauge.Style := npbstNormal;
  end;

  if InstallSuccessful = False then
    begin
      RaiseException('Fatal: An error occured while installing GVFS.Service.');
    end;
end;

procedure StagingUpdateService();
var
  ResultCode: integer;
  StatusText: string;
begin
  // In staging mode: the service was stopped in PrepareToInstall so its exe
  // could be replaced. Now start it with the new binary. The new service has
  // PendingUpgradeHandler which will complete the upgrade on next restart
  // when no mounts are running.
  StatusText := WizardForm.StatusLabel.Caption;
  WizardForm.StatusLabel.Caption := 'Starting GVFS.Service.';
  WizardForm.ProgressGauge.Style := npbstMarquee;

  try
    Log('StagingUpdateService: Starting service with new binary');
    if Exec(ExpandConstant('{sys}\SC.EXE'), 'start GVFS.Service', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      begin
        if ResultCode <> 0 then
          Log('StagingUpdateService: Warning - sc start returned error code ' + IntToStr(ResultCode));
      end
    else
      begin
        Log('StagingUpdateService: Warning - could not launch sc.exe');
      end;

    WriteOnDiskVersion16CapableFile();
  finally
    WizardForm.StatusLabel.Caption := StatusText;
    WizardForm.ProgressGauge.Style := npbstNormal;
  end;
end;

function DeleteFileIfItExists(FilePath: string) : Boolean;
begin
  Result := False;
  if FileExists(FilePath) then
    begin
      Log('DeleteFileIfItExists: Removing ' + FilePath);
      if DeleteFile(FilePath) then
        begin
          if not FileExists(FilePath) then
            begin
              Result := True;
            end
          else
            begin
              Log('DeleteFileIfItExists: File still exists after deleting: ' + FilePath);
            end;
        end
      else
        begin
          Log('DeleteFileIfItExists: Failed to delete ' + FilePath);
        end;
    end
  else
    begin
      Log('DeleteFileIfItExists: File does not exist: ' + FilePath);
      Result := True;
    end;
end;

procedure UninstallGvFlt();
var
  StatusText: string;
  UninstallSuccessful: Boolean;
begin
  if (FileExists(ExpandConstant('{app}\Filter\GvFlt.inf'))) then
  begin
    UninstallSuccessful := False;

    StatusText := WizardForm.StatusLabel.Caption;
    WizardForm.StatusLabel.Caption := 'Uninstalling GvFlt Driver.';
    WizardForm.ProgressGauge.Style := npbstMarquee;

    try
      UninstallService('gvflt', False);
      if DeleteFileIfItExists(ExpandConstant('{sys}\drivers\gvflt.sys')) then
        begin
           UninstallSuccessful := True;
        end;
    finally
      WizardForm.StatusLabel.Caption := StatusText;
      WizardForm.ProgressGauge.Style := npbstNormal;
    end;

    if UninstallSuccessful = True then
      begin
        if not DeleteFile(ExpandConstant('{app}\Filter\GvFlt.inf')) then
          begin
            Log('UninstallGvFlt: Failed to delete GvFlt.inf');
          end;
      end
    else
      begin
          RaiseException('Fatal: An error occured while uninstalling GvFlt drivers.');
      end;
  end;
end;

function UninstallNonInboxProjFS(): Boolean;
var
  StatusText: string;
begin
  Result := False;
  StatusText := WizardForm.StatusLabel.Caption;
  WizardForm.StatusLabel.Caption := 'Uninstalling PrjFlt Driver.';
  WizardForm.ProgressGauge.Style := npbstMarquee;

  Log('UninstallNonInboxProjFS: Uninstalling ProjFS');
  try
    UninstallService('prjflt', False);
    if DeleteFileIfItExists(ExpandConstant('{app}\ProjectedFSLib.dll')) then
      begin
        if DeleteFileIfItExists(ExpandConstant('{sys}\drivers\prjflt.sys')) then
          begin
            Result := True;
          end;
      end;
  finally
    WizardForm.StatusLabel.Caption := StatusText;
    WizardForm.ProgressGauge.Style := npbstNormal;
  end;
end;

procedure UninstallProjFSIfNecessary();
var
  ProjFSFeatureEnabledResultCode: integer;
  UninstallSuccessful: Boolean;
begin
  if FileExists(ExpandConstant('{app}\Filter\PrjFlt.inf')) and FileExists(ExpandConstant('{sys}\drivers\prjflt.sys')) then
    begin
      UninstallSuccessful := False;

      if Exec('powershell.exe', '-NoProfile "$var=(Get-WindowsOptionalFeature -Online -FeatureName Client-ProjFS);  if($var -eq $null){exit 2}else{if($var.State -eq ''Enabled''){exit 3}else{exit 4}}"', '', SW_HIDE, ewWaitUntilTerminated, ProjFSFeatureEnabledResultCode) then
        begin
          if ProjFSFeatureEnabledResultCode = 2 then
            begin
              // Client-ProjFS is not an optional feature
              Log('UninstallProjFSIfNecessary: Could not locate Windows Projected File System optional feature, uninstalling ProjFS');
              if UninstallNonInboxProjFS() then
                begin
                  UninstallSuccessful := True;
                end;
            end;
          if ProjFSFeatureEnabledResultCode = 3 then
            begin
              // Client-ProjFS is already enabled. If the native ProjFS library is in the apps folder it must
              // be deleted to ensure GVFS uses the inbox library (in System32)
              Log('UninstallProjFSIfNecessary: Client-ProjFS already enabled');
              if DeleteFileIfItExists(ExpandConstant('{app}\ProjectedFSLib.dll')) then
                begin
                  UninstallSuccessful := True;
                end;
            end;
          if ProjFSFeatureEnabledResultCode = 4 then
            begin
              // Client-ProjFS is currently disabled but prjflt.sys is present and should be removed
              Log('UninstallProjFSIfNecessary: Client-ProjFS is disabled, uninstalling ProjFS');
              if UninstallNonInboxProjFS() then
                begin
                  UninstallSuccessful := True;
                end;
            end;
        end;

      if UninstallSuccessful = False then
      begin
        RaiseException('Fatal: An error occured while uninstalling ProjFS.');
      end;
    end;
end;

function IsGVFSRunning(): Boolean;
var
  ResultCode: integer;
begin
  if Exec('powershell.exe', '-NoProfile "Get-Process gvfs,gvfs.mount | foreach {exit 10}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      if ResultCode = 10 then
        begin
          Result := True;
        end;
      if ResultCode = 1 then
        begin
          Result := False;
        end;
    end;
end;

function ExecWithResult(Filename, Params, WorkingDir: String; ShowCmd: Integer;
  Wait: TExecWait; var ResultCode: Integer; var ResultString: ansiString): Boolean;
var
  TempFilename: string;
  Command: string;
begin
  TempFilename := ExpandConstant('{tmp}\~execwithresult.txt');
  { Exec via cmd and redirect output to file. Must use special string-behavior to work. }
  Command := Format('"%s" /S /C ""%s" %s > "%s""', [ExpandConstant('{cmd}'), Filename, Params, TempFilename]);
  Result := Exec(ExpandConstant('{cmd}'), Command, WorkingDir, ShowCmd, Wait, ResultCode);
  if Result then
    begin
      LoadStringFromFile(TempFilename, ResultString);
    end;
  DeleteFile(TempFilename);
end;

procedure UnmountRepos();
var
  ResultCode: integer;
begin
  Exec('gvfs.exe', 'service --unmount-all', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure MountRepos();
var
  StatusText: string;
  MountOutput: ansiString;
  ResultCode: integer;
  MsgBoxText: string;
begin
  StatusText := WizardForm.StatusLabel.Caption;
  WizardForm.StatusLabel.Caption := 'Mounting Repos.';
  WizardForm.ProgressGauge.Style := npbstMarquee;

  ExecWithResult(ExpandConstant('{app}') + '\gvfs.exe', 'service --mount-all', '', SW_HIDE, ewWaitUntilTerminated, ResultCode, MountOutput);
  WizardForm.StatusLabel.Caption := StatusText;
  WizardForm.ProgressGauge.Style := npbstNormal;

  // 4 = ReturnCode.FilterError
  if (ResultCode = 4) then
    begin
      RaiseException('Fatal: Could not configure and start Windows Projected File System.');
    end
  else if (ResultCode <> 0) then
    begin
      MsgBoxText := 'Mounting one or more repos failed:' + #13#10 + MountOutput;
      SuppressibleMsgBox(MsgBoxText, mbConfirmation, MB_OK, IDOK);
      ExitCode := 17;
    end;
end;

procedure MigrateFile(OldPath, NewPath : string);
begin
  Log('MigrateFile(' + OldPath + ', ' + NewPath + ')');
  if (FileExists(OldPath)) then
    begin
      if (not FileExists(NewPath)) then
        begin
          if (not RenameFile(OldPath, NewPath)) then
            Log('Could not move ' + OldPath + ' continuing anyway')
          else
            Log('Moved ' + OldPath + ' to ' + NewPath);
        end
      else
        Log('Migration cancelled. Newer file exists at path ' + NewPath);
    end
  else
    Log('Migration cancelled. ' + OldPath + ' does not exist');
end;

procedure MigrateConfigAndStatusCacheFiles();
var
  CommonAppDataDir: string;
  SecureAppDataDir: string;
begin
  CommonAppDataDir := ExpandConstant('{commonappdata}\GVFS');
  SecureAppDataDir := ExpandConstant('{app}\ProgramData');

  MigrateFile(CommonAppDataDir + '\{#GVFSConfigFileName}', SecureAppDataDir + '\{#GVFSConfigFileName}');
  MigrateFile(CommonAppDataDir + '\{#ServiceName}\{#GVFSStatuscacheTokenFileName}', SecureAppDataDir + '\{#ServiceName}\{#GVFSStatuscacheTokenFileName}');
end;

function EnsureGvfsNotRunning(): Boolean;
var
  MsgBoxResult: integer;
begin
  MsgBoxResult := IDRETRY;
  while (IsGVFSRunning()) Do
    begin
      if(MsgBoxResult = IDRETRY) then
        begin
          MsgBoxResult := SuppressibleMsgBox('GVFS is currently running. Please close all instances of GVFS before continuing the installation.', mbError, MB_RETRYCANCEL, IDCANCEL);
        end;
      if(MsgBoxResult = IDCANCEL) then
        begin
          Result := False;
          Abort();
        end;
    end;

  Result := True;
end;

type
  UpgradeRing = (urUnconfigured, urNone, urFast, urSlow);

function GetConfiguredUpgradeRing(): UpgradeRing;
var
  ResultCode: integer;
  ResultString: ansiString;
begin
  Result := urUnconfigured;
  if ExecWithResult('gvfs.exe', 'config upgrade.ring', '', SW_HIDE, ewWaitUntilTerminated, ResultCode, ResultString) then begin
    if ResultCode = 0 then begin
      ResultString := AnsiLowercase(Trim(ResultString));
      Log('GetConfiguredUpgradeRing: upgrade.ring is ' + ResultString);
      if CompareText(ResultString, 'none') = 0 then begin
        Result := urNone;
      end else if CompareText(ResultString, 'fast') = 0 then begin
        Result := urFast;
      end else if CompareText(ResultString, 'slow') = 0 then begin
        Result := urSlow;
      end else begin
        Log('GetConfiguredUpgradeRing: Unknown upgrade ring: ' + ResultString);
      end;
    end else begin
      Log('GetConfiguredUpgradeRing: Call to gvfs config upgrade.ring failed with ' + SysErrorMessage(ResultCode));
    end;
  end else begin
    Log('GetConfiguredUpgradeRing: Call to gvfs config upgrade.ring failed with ' + SysErrorMessage(ResultCode));
  end;
end;

function IsConfigured(ConfigKey: String): Boolean;
var
  ResultCode: integer;
  ResultString: ansiString;
begin
  Result := False
  if ExecWithResult('gvfs.exe', Format('config %s', [ConfigKey]), '', SW_HIDE, ewWaitUntilTerminated, ResultCode, ResultString) then begin
    ResultString := AnsiLowercase(Trim(ResultString));
    Log(Format('IsConfigured(%s): value is %s', [ConfigKey, ResultString]));
    Result := Length(ResultString) > 1
  end
end;

procedure SetIfNotConfigured(ConfigKey: String; ConfigValue: String);
var
  ResultCode: integer;
  ResultString: ansiString;
begin
  if IsConfigured(ConfigKey) = False then begin
    if ExecWithResult('gvfs.exe', Format('config %s %s', [ConfigKey, ConfigValue]), '', SW_HIDE, ewWaitUntilTerminated, ResultCode, ResultString) then begin
      Log(Format('SetIfNotConfigured: Set %s to %s', [ConfigKey, ConfigValue]));
    end else begin
      Log(Format('SetIfNotConfigured: Failed to set %s with %s', [ConfigKey, SysErrorMessage(ResultCode)]));
    end;
  end else begin
    Log(Format('SetIfNotConfigured: %s is configured, not overwriting', [ConfigKey]));
  end;
end;

procedure SetNuGetFeedIfNecessary();
var
  ConfiguredRing: UpgradeRing;
  RingName: String;
  TargetFeed: String;
  FeedPackageName: String;
begin
  ConfiguredRing := GetConfiguredUpgradeRing();
  if ConfiguredRing = urFast then begin
    RingName := 'Fast';
  end else if (ConfiguredRing = urSlow) or (ConfiguredRing = urNone) then begin
    RingName := 'Slow';
  end else begin
    Log('SetNuGetFeedIfNecessary: No upgrade ring configured. Not configuring NuGet feed.')
    exit;
  end;

  TargetFeed := Format('https://pkgs.dev.azure.com/microsoft/_packaging/VFSForGit-%s/nuget/v3/index.json', [RingName]);
  FeedPackageName := 'Microsoft.VfsForGitEnvironment';

  SetIfNotConfigured('upgrade.feedurl', TargetFeed);
  SetIfNotConfigured('upgrade.feedpackagename', FeedPackageName);
end;

// Below are EVENT FUNCTIONS -> The main entry points of InnoSetup into the code region
// Documentation : http://www.jrsoftware.org/ishelp/index.php?topic=scriptevents

function InitializeUninstall(): Boolean;
begin
  UnmountRepos();
  Result := EnsureGvfsNotRunning();
end;

// Called just after "install" phase, before "post install"
function NeedRestart(): Boolean;
begin
  Result := False;
end;

function UninstallNeedRestart(): Boolean;
begin
  Result := False;
end;

procedure RegisterLogonTask();
var
  GvfsPath: string;
  UserSid: ansiString;
  TaskXmlPath: string;
  TaskXml: string;
  ResultCode: integer;
begin
  GvfsPath := ExpandConstant('{localappdata}\GVFS\Current\gvfs.exe');
  // Get current user's SID via whoami /user
  if not ExecWithResult('whoami.exe', '/user /nh', '', SW_HIDE, ewWaitUntilTerminated, ResultCode, UserSid) or (ResultCode <> 0) then
    begin
      Log('RegisterLogonTask: Could not determine user SID');
      exit;
    end;
  // whoami output is like: DOMAIN\user S-1-5-21-... — extract SID
  UserSid := Trim(UserSid);
  if Pos(' ', UserSid) > 0 then
    UserSid := Copy(UserSid, Pos(' ', UserSid) + 1, Length(UserSid));
  UserSid := Trim(UserSid);
  Log('RegisterLogonTask: User SID is ' + UserSid);

  TaskXml := '<?xml version="1.0" encoding="UTF-8"?>' + #13#10 +
    '<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">' + #13#10 +
    '  <RegistrationInfo>' + #13#10 +
    '    <Author>GVFS</Author>' + #13#10 +
    '    <Description>Mounts registered GVFS enlistments at logon.</Description>' + #13#10 +
    '    <URI>\GVFS\AutoMount</URI>' + #13#10 +
    '  </RegistrationInfo>' + #13#10 +
    '  <Triggers>' + #13#10 +
    '    <LogonTrigger><Enabled>true</Enabled><UserId>' + UserSid + '</UserId></LogonTrigger>' + #13#10 +
    '  </Triggers>' + #13#10 +
    '  <Principals>' + #13#10 +
    '    <Principal id="Author"><UserId>' + UserSid + '</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal>' + #13#10 +
    '  </Principals>' + #13#10 +
    '  <Settings>' + #13#10 +
    '    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>' + #13#10 +
    '    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>' + #13#10 +
    '    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>' + #13#10 +
    '    <AllowHardTerminate>true</AllowHardTerminate>' + #13#10 +
    '    <StartWhenAvailable>true</StartWhenAvailable>' + #13#10 +
    '    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>' + #13#10 +
    '    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>' + #13#10 +
    '    <AllowStartOnDemand>true</AllowStartOnDemand>' + #13#10 +
    '    <Enabled>true</Enabled>' + #13#10 +
    '    <Hidden>false</Hidden>' + #13#10 +
    '    <RunOnlyIfIdle>false</RunOnlyIfIdle>' + #13#10 +
    '    <WakeToRun>false</WakeToRun>' + #13#10 +
    '    <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>' + #13#10 +
    '    <Priority>5</Priority>' + #13#10 +
    '  </Settings>' + #13#10 +
    '  <Actions Context="Author">' + #13#10 +
    '    <Exec>' + #13#10 +
    '      <Command>conhost.exe</Command>' + #13#10 +
    '      <Arguments>--headless ' + GvfsPath + ' service --mount-all</Arguments>' + #13#10 +
    '    </Exec>' + #13#10 +
    '  </Actions>' + #13#10 +
    '</Task>';

  TaskXmlPath := ExpandConstant('{tmp}\gvfs-logon-task.xml');
  SaveStringToFile(TaskXmlPath, TaskXml, False);

  Log('RegisterLogonTask: Registering \GVFS\AutoMount task');
  if not Exec(ExpandConstant('{sys}\schtasks.exe'), '/Create /TN "\GVFS\AutoMount" /XML "' + TaskXmlPath + '" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    begin
      Log('RegisterLogonTask: schtasks /Create failed (exit ' + IntToStr(ResultCode) + ') - logon automount will not be available');
    end
  else
    begin
      Log('RegisterLogonTask: Task registered successfully');
    end;
end;

procedure CreateOrUpdateCurrentJunction();
var
  GVFSRoot: string;
  JunctionPath: string;
  VersionDir: string;
  OldTarget: ansiString;
  GvfsExe: string;
  ResultCode: integer;
  VersionOutput: ansiString;
  HadPreviousJunction: Boolean;
begin
  GVFSRoot := ExpandConstant('{localappdata}\GVFS');
  JunctionPath := GVFSRoot + '\Current';
  VersionDir := GVFSRoot + '\Versions\{#GVFSVersion}';
  HadPreviousJunction := False;

  // Save old junction target for rollback
  if DirExists(JunctionPath) then
    begin
      HadPreviousJunction := True;
      // Read the junction target via fsutil reparsepoint query.
      // Output includes a line like "Print Name: C:\Users\...\Versions\1.0.0"
      if ExecWithResult('fsutil.exe', 'reparsepoint query "' + JunctionPath + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode, OldTarget) and (ResultCode = 0) then
        begin
          // Extract the "Print Name:" value
          if Pos('Print Name:', OldTarget) > 0 then
            begin
              OldTarget := Copy(OldTarget, Pos('Print Name:', OldTarget) + Length('Print Name:'), Length(OldTarget));
              // Trim to end of line
              if Pos(#13, OldTarget) > 0 then
                OldTarget := Copy(OldTarget, 1, Pos(#13, OldTarget) - 1);
              OldTarget := Trim(OldTarget);
              Log('CreateOrUpdateCurrentJunction: Previous target: ' + OldTarget);
            end
          else
            begin
              Log('CreateOrUpdateCurrentJunction: Could not parse junction target from fsutil output');
              OldTarget := '';
            end;
        end
      else
        begin
          Log('CreateOrUpdateCurrentJunction: fsutil reparsepoint query failed');
          OldTarget := '';
        end;

      Log('CreateOrUpdateCurrentJunction: Removing existing Current');
      Exec(ExpandConstant('{cmd}'), '/C rmdir "' + JunctionPath + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;

  // Create junction: Current -> Versions\<version>
  Log('CreateOrUpdateCurrentJunction: Creating junction -> ' + VersionDir);
  if not Exec(ExpandConstant('{cmd}'), '/C mklink /J "' + JunctionPath + '" "' + VersionDir + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    begin
      Log('CreateOrUpdateCurrentJunction: mklink failed (exit ' + IntToStr(ResultCode) + ')');
      RaiseException('Fatal: Could not create Current junction at ' + JunctionPath);
    end;

  // Verify the new payload works
  GvfsExe := JunctionPath + '\gvfs.exe';
  if FileExists(GvfsExe) then
    begin
      if ExecWithResult(GvfsExe, 'version', '', SW_HIDE, ewWaitUntilTerminated, ResultCode, VersionOutput) and (ResultCode = 0) then
        begin
          Log('CreateOrUpdateCurrentJunction: Verified gvfs.exe version: ' + Trim(VersionOutput));
        end
      else
        begin
          Log('CreateOrUpdateCurrentJunction: gvfs.exe version failed (exit ' + IntToStr(ResultCode) + ')');
          if HadPreviousJunction and (OldTarget <> '') then
            begin
              Log('CreateOrUpdateCurrentJunction: Rolling back to ' + OldTarget);
              Exec(ExpandConstant('{cmd}'), '/C rmdir "' + JunctionPath + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
              if Exec(ExpandConstant('{cmd}'), '/C mklink /J "' + JunctionPath + '" "' + OldTarget + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
                begin
                  Log('CreateOrUpdateCurrentJunction: Rollback successful - Current restored to ' + OldTarget);
                end
              else
                begin
                  Log('CreateOrUpdateCurrentJunction: Rollback mklink failed (exit ' + IntToStr(ResultCode) + ')');
                end;
            end
          else if HadPreviousJunction then
            begin
              Log('CreateOrUpdateCurrentJunction: Cannot rollback - old target unknown');
              Exec(ExpandConstant('{cmd}'), '/C rmdir "' + JunctionPath + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
            end;
          RaiseException('Fatal: New gvfs.exe failed verification. Installation may be corrupt.');
        end;
    end
  else
    begin
      Log('CreateOrUpdateCurrentJunction: Warning - gvfs.exe not found at ' + GvfsExe + ' (expected after junction creation)');
    end;

  Log('CreateOrUpdateCurrentJunction: Junction created and verified');
end;

procedure GarbageCollectOldVersions();
var
  GVFSRoot: string;
  VersionsDir: string;
  CurrentVersion: string;
  FindRec: TFindRec;
  Versions: TStringList;
  i: Integer;
  PathToDelete: string;
  ResultCode: integer;
begin
  GVFSRoot := ExpandConstant('{localappdata}\GVFS');
  VersionsDir := GVFSRoot + '\Versions';
  CurrentVersion := '{#GVFSVersion}';

  if not DirExists(VersionsDir) then exit;

  Versions := TStringList.Create;
  try
    // Enumerate version directories
    if FindFirst(VersionsDir + '\*', FindRec) then
      begin
        try
          repeat
            if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0) and
               (FindRec.Name <> '.') and (FindRec.Name <> '..') then
              begin
                Versions.Add(FindRec.Name);
              end;
          until not FindNext(FindRec);
        finally
          FindClose(FindRec);
        end;
      end;

    Log('GarbageCollectOldVersions: Found ' + IntToStr(Versions.Count) + ' version(s)');
    if Versions.Count <= 2 then
      begin
        Log('GarbageCollectOldVersions: 2 or fewer versions, nothing to GC');
        exit;
      end;

    // Sort so we can identify the oldest. Versions sort lexically
    // (semver-compatible for our numbering scheme).
    Versions.Sort;

    // Delete all but the most recent 2. The current version is always
    // kept regardless of sort order.
    for i := 0 to Versions.Count - 3 do
      begin
        if Versions[i] = CurrentVersion then
          begin
            Log('GarbageCollectOldVersions: Skipping current version ' + Versions[i]);
            continue;
          end;
        PathToDelete := VersionsDir + '\' + Versions[i];
        Log('GarbageCollectOldVersions: Deleting old version ' + PathToDelete);
        if not DelTree(PathToDelete, True, True, True) then
          begin
            Log('GarbageCollectOldVersions: Warning - could not fully delete ' + PathToDelete);
          end;
      end;
  finally
    Versions.Free;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  case CurStep of
    ssInstall:
      begin
        if (not IsUserModeInstall) and (not KeepMountsRunning) then
          UninstallService('GVFS.Service', True);
      end;
    ssPostInstall:
      begin
        if IsUserModeInstall then
          begin
            CreateOrUpdateCurrentJunction();
            RegisterLogonTask();
            GarbageCollectOldVersions();
          end
        else
          begin
            if KeepMountsRunning then
              begin
                SaveStringToFile(ExpandConstant('{app}\PendingUpgrade\.ready'), '', False);
                Log('CurStepChanged: Wrote PendingUpgrade .ready marker');
                StagingUpdateService();
              end;
            MigrateConfigAndStatusCacheFiles();
            if (not KeepMountsRunning) and (ExpandConstant('{param:REMOUNTREPOS|true}') = 'true') then
              begin
                MountRepos();
              end
          end;
      end;
    end;
end;

function GetCustomSetupExitCode: Integer;
begin
  Result := ExitCode;
end;

procedure CurUninstallStepChanged(CurStep: TUninstallStep);
begin
  case CurStep of
    usUninstall:
      begin
        UninstallService('GVFS.Service', False);
        RemovePath(ExpandConstant('{app}'));
      end;
    end;
end;

// Shows a modal dialog letting the user choose how to handle mounted repos.
// Returns True if the user clicked Continue, False if Cancel. On Continue,
// KeepMounted is set to True if the user chose to stage the upgrade and
// leave repos mounted, or False to unmount and remount immediately.
function ShowMountChoiceDialog(Repos: String; var KeepMounted: Boolean): Boolean;
var
  Form: TForm;
  HeaderLbl, ReposLbl, RemountDescLbl, KeepDescLbl: TNewStaticText;
  RemountRadio, KeepRadio: TNewRadioButton;
  BtnContinue, BtnCancel: TNewButton;
  ButtonWidth, ButtonHeight, ContentWidth, Margin, IndentMargin: Integer;
  ModalResult, Y: Integer;
begin
  Margin := ScaleX(15);
  IndentMargin := ScaleX(34);
  ButtonWidth := ScaleX(85);
  ButtonHeight := ScaleY(25);

  Form := TForm.Create(nil);
  try
    Form.Caption := 'Setup';
    Form.BorderStyle := bsDialog;
    Form.Position := poOwnerFormCenter;
    Form.ClientWidth := ScaleX(520);
    ContentWidth := Form.ClientWidth - (2 * Margin);

    Y := ScaleY(15);

    HeaderLbl := TNewStaticText.Create(Form);
    HeaderLbl.Parent := Form;
    HeaderLbl.Left := Margin;
    HeaderLbl.Top := Y;
    HeaderLbl.Caption := 'The following repos are currently mounted:';
    HeaderLbl.AutoSize := True;
    Y := HeaderLbl.Top + HeaderLbl.Height + ScaleY(4);

    ReposLbl := TNewStaticText.Create(Form);
    ReposLbl.Parent := Form;
    ReposLbl.Left := IndentMargin;
    ReposLbl.Top := Y;
    ReposLbl.Width := Form.ClientWidth - IndentMargin - Margin;
    ReposLbl.WordWrap := True;
    ReposLbl.AutoSize := True;
    ReposLbl.Caption := Trim(Repos);
    Y := ReposLbl.Top + ReposLbl.Height + ScaleY(16);

    RemountRadio := TNewRadioButton.Create(Form);
    RemountRadio.Parent := Form;
    RemountRadio.Left := Margin;
    RemountRadio.Top := Y;
    RemountRadio.Width := ContentWidth;
    RemountRadio.Caption := 'Remount repos as part of the installation';
    RemountRadio.Checked := True;
    Y := RemountRadio.Top + RemountRadio.Height + ScaleY(2);

    RemountDescLbl := TNewStaticText.Create(Form);
    RemountDescLbl.Parent := Form;
    RemountDescLbl.Left := IndentMargin;
    RemountDescLbl.Top := Y;
    RemountDescLbl.Width := Form.ClientWidth - IndentMargin - Margin;
    RemountDescLbl.WordWrap := True;
    RemountDescLbl.AutoSize := True;
    RemountDescLbl.Caption := 'They will be temporarily unavailable.';
    Y := RemountDescLbl.Top + RemountDescLbl.Height + ScaleY(14);

    KeepRadio := TNewRadioButton.Create(Form);
    KeepRadio.Parent := Form;
    KeepRadio.Left := Margin;
    KeepRadio.Top := Y;
    KeepRadio.Width := ContentWidth;
    KeepRadio.Caption := 'Keep repos mounted';
    Y := KeepRadio.Top + KeepRadio.Height + ScaleY(2);

    KeepDescLbl := TNewStaticText.Create(Form);
    KeepDescLbl.Parent := Form;
    KeepDescLbl.Left := IndentMargin;
    KeepDescLbl.Top := Y;
    KeepDescLbl.Width := Form.ClientWidth - IndentMargin - Margin;
    KeepDescLbl.WordWrap := True;
    KeepDescLbl.AutoSize := True;
    KeepDescLbl.Caption := 'The upgrade will complete automatically when all repos are unmounted, or at next reboot.';
    Y := KeepDescLbl.Top + KeepDescLbl.Height + ScaleY(20);

    BtnContinue := TNewButton.Create(Form);
    BtnContinue.Parent := Form;
    BtnContinue.Width := ButtonWidth;
    BtnContinue.Height := ButtonHeight;
    BtnContinue.Top := Y;
    BtnContinue.Left := Form.ClientWidth - Margin - ButtonWidth - ScaleX(10) - ButtonWidth;
    BtnContinue.Caption := '&Continue';
    BtnContinue.Default := True;
    BtnContinue.ModalResult := mrOk;

    BtnCancel := TNewButton.Create(Form);
    BtnCancel.Parent := Form;
    BtnCancel.Width := ButtonWidth;
    BtnCancel.Height := ButtonHeight;
    BtnCancel.Top := Y;
    BtnCancel.Left := Form.ClientWidth - Margin - ButtonWidth;
    BtnCancel.Caption := '&Cancel';
    BtnCancel.Cancel := True;
    BtnCancel.ModalResult := mrCancel;

    Form.ClientHeight := Y + ButtonHeight + ScaleY(15);
    Form.ActiveControl := BtnContinue;

    ModalResult := Form.ShowModal();
    if ModalResult = mrOk then
      begin
        KeepMounted := KeepRadio.Checked;
        Result := True;
      end
    else
      begin
        Result := False;
      end;
  finally
    Form.Free();
  end;
end;

function IsProjFSEnabled(): Boolean;
var
  ResultCode: integer;
begin
  Result := False;
  // exit 3 = enabled, exit 4 = disabled, exit 2 = not an optional feature
  if Exec('powershell.exe', '-NoProfile "$f=Get-WindowsOptionalFeature -Online -FeatureName Client-ProjFS -ErrorAction SilentlyContinue; if($f -and $f.State -eq ''Enabled''){exit 0}else{exit 1}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      Result := (ResultCode = 0);
    end;
end;

function IsEnableProjFSTaskCurrent(): Boolean;
var
  ResultCode: integer;
  TaskXml: ansiString;
  HashPos: Integer;
  ExpectedHash: string;
begin
  Result := False;
  // Query the registered task XML via schtasks
  if not ExecWithResult(ExpandConstant('{sys}\schtasks.exe'), '/Query /TN "{#ProjFSTaskPath}" /XML', '', SW_HIDE, ewWaitUntilTerminated, ResultCode, TaskXml) then
    exit;
  if ResultCode <> 0 then
    exit;
  // Look for the hash marker in the task description
  HashPos := Pos('[gvfs-task-hash=', TaskXml);
  if HashPos = 0 then
    exit;
  // The expected hash is passed as a preprocessor define at build time
  // (set by the build step that runs build-task-xml.ps1). For now, if
  // any valid hash marker is present, we consider the task registered.
  // B4 will add the exact hash comparison when it wires up the build step.
  Result := True;
  Log('IsEnableProjFSTaskCurrent: Task found with hash marker');
end;

procedure EnableProjFSFeature();
var
  ResultCode: integer;
begin
  if IsProjFSEnabled() then
    begin
      Log('EnableProjFSFeature: Already enabled, skipping');
      exit;
    end;
  Log('EnableProjFSFeature: Enabling Client-ProjFS optional feature');
  if Exec('powershell.exe', '-NoProfile "Enable-WindowsOptionalFeature -Online -FeatureName Client-ProjFS -NoRestart"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      if ResultCode <> 0 then
        Log('EnableProjFSFeature: PowerShell returned ' + IntToStr(ResultCode) + ' (may require reboot)')
      else
        Log('EnableProjFSFeature: Enabled successfully');
    end
  else
    begin
      Log('EnableProjFSFeature: Failed to launch PowerShell');
      RaiseException('Fatal: Could not enable ProjFS.');
    end;
end;

procedure RegisterEnableProjFSTask();
var
  ResultCode: integer;
  TaskXmlPath: string;
begin
  if IsEnableProjFSTaskCurrent() then
    begin
      Log('RegisterEnableProjFSTask: Task is already current, skipping');
      exit;
    end;

  Log('RegisterEnableProjFSTask: Registering EnableProjFSOnAllDrives task');
  // Extract the pre-built task XML from the installer's embedded files
  ExtractTemporaryFile('enable-projfs-on-all-drives-task.xml');
  TaskXmlPath := ExpandConstant('{tmp}\enable-projfs-on-all-drives-task.xml');

  if not Exec(ExpandConstant('{sys}\schtasks.exe'), '/Create /TN "{#ProjFSTaskPath}" /XML "' + TaskXmlPath + '" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    begin
      Log('RegisterEnableProjFSTask: schtasks /Create failed with exit code ' + IntToStr(ResultCode));
      RaiseException('Fatal: Could not register EnableProjFSOnAllDrives scheduled task.');
    end;
  Log('RegisterEnableProjFSTask: Task registered successfully');

  // Run the task immediately so PrjFlt is attached before we return
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/Run /TN "{#ProjFSTaskPath}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if ResultCode <> 0 then
    Log('RegisterEnableProjFSTask: Warning - immediate task run returned ' + IntToStr(ResultCode))
  else
    Log('RegisterEnableProjFSTask: Task triggered for immediate execution');
end;

function NeedsAdminSetup(): Boolean;
begin
  if not IsProjFSEnabled() then
    begin
      Log('NeedsAdminSetup: ProjFS not enabled');
      Result := True;
      exit;
    end;
  if not IsEnableProjFSTaskCurrent() then
    begin
      Log('NeedsAdminSetup: EnableProjFSOnAllDrives task not current');
      Result := True;
      exit;
    end;
  Log('NeedsAdminSetup: Admin setup is current, no elevation needed');
  Result := False;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  MsgBoxResult: integer;
  Repos: ansiString;
  ResultCode: integer;
  HasMounts: Boolean;
begin
  NeedsRestart := False;
  KeepMountsRunning := False;
  Result := '';

  if IsAdminStage then
    begin
      // Elevated re-launch from the user-mode installer. Do only
      // the per-machine admin setup, then exit cleanly. The user-mode
      // installer is waiting for our exit code (0 = success).
      //
      // We let Inno Setup proceed through the install phase, but all
      // [Files] entries are gated behind Check functions that return
      // false in admin-stage mode, so nothing is deployed. This gives
      // us a clean exit code without fighting the Inno Setup lifecycle.
      Log('PrepareToInstall: /ADMINSTAGE - running admin setup');
      EnableProjFSFeature();
      RegisterEnableProjFSTask();
      Log('PrepareToInstall: /ADMINSTAGE - admin setup complete');
      exit;
    end;

  if IsUserModeInstall then
    begin
      // User-mode install: check whether per-machine admin setup
      // (ProjFS feature + EnableProjFSOnAllDrives task) is current.
      // If not, re-launch ourselves elevated to do the admin portion.
      if NeedsAdminSetup() then
        begin
          Log('PrepareToInstall: Admin setup needed, re-launching elevated');
          if not ShellExec('runas', ExpandConstant('{srcexe}'), '/VERYSILENT /ADMINSTAGE=true', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
            begin
              Result := 'Failed to launch elevated admin setup (user may have declined UAC).';
              exit;
            end;
          if ResultCode <> 0 then
            begin
              Result := 'Admin setup failed (exit code ' + IntToStr(ResultCode) + ').';
              exit;
            end;
          Log('PrepareToInstall: Admin setup completed successfully');
        end
      else
        begin
          Log('PrepareToInstall: Admin setup is current, no elevation needed');
        end;
      // Skip system-mode preparation (service queries, mount detection,
      // staging logic). The user-mode path just deploys the payload
      // and sets up the junction + PATH.
      exit;
    end;

  SetNuGetFeedIfNecessary();

  // Check for mounted repos by querying the service, and also check for
  // running GVFS processes (a mount can be running without being registered
  // in the service's repo-registry, e.g., after a reinstall).
  HasMounts := False;
  if ExecWithResult('gvfs.exe', 'service --list-mounted', '', SW_HIDE, ewWaitUntilTerminated, ResultCode, Repos) then
    begin
      if (ResultCode = 0) and (Repos <> '') then
        HasMounts := True;
    end;
  if (not HasMounts) and IsGVFSRunning() then
    begin
      HasMounts := True;
      Repos := '(GVFS processes detected)';
      Log('PrepareToInstall: No registered mounts but GVFS processes are running');
    end;

  if HasMounts then
    begin
      if WizardSilent() then
        begin
          // Silent mode: STAGEIFMOUNTED=true stages files instead of unmounting.
          // Default: false (clean upgrade, matching pre-existing behavior).
          KeepMountsRunning := ExpandConstant('{param:STAGEIFMOUNTED|false}') = 'true';
          if KeepMountsRunning then
            Log('PrepareToInstall: Silent mode with mounted repos, KeepMountsRunning=True')
          else
            Log('PrepareToInstall: Silent mode with mounted repos, KeepMountsRunning=False');
        end
      else
        begin
          // Interactive mode: let user choose
          MsgBoxResult := SuppressibleMsgBox(
            'The following repos are currently mounted:' + #13#10 + Repos + #13#10#13#10 +
            'Click Yes to keep repos mounted during the upgrade.' + #13#10 +
            'The upgrade will complete automatically when all repos are unmounted.' + #13#10#13#10 +
            'Click No to unmount all repos now and upgrade without restart.' + #13#10 +
            'Repos will be temporarily unavailable during the upgrade.',
            mbConfirmation, MB_YESNOCANCEL, IDYES);
          if MsgBoxResult = IDYES then
            KeepMountsRunning := True
          else if MsgBoxResult = IDNO then
            KeepMountsRunning := False
          else
            begin
              Result := 'Installation cancelled.';
              exit;
            end;
        end;
    end;

  if KeepMountsRunning then
    begin
      // Staging mode: most files go to {app}\PendingUpgrade\ via [Files] entries
      // with Check: IsStagingInstall. GVFS.Service.exe goes directly to {app}.
      // Clean up any leftover staging dirs from a prior attempt first,
      // so we don't mix files from different upgrade versions.
      if DirExists(ExpandConstant('{app}\PendingUpgrade')) then
        begin
          Log('PrepareToInstall: Removing stale PendingUpgrade from prior staging attempt');
          DelTree(ExpandConstant('{app}\PendingUpgrade'), True, True, True);
        end;
      if DirExists(ExpandConstant('{app}\PreviousVersion')) then
        begin
          Log('PrepareToInstall: Removing stale PreviousVersion from prior staging attempt');
          DelTree(ExpandConstant('{app}\PreviousVersion'), True, True, True);
        end;
      // Stop the service now so its exe is unlocked for replacement.
      // Mounts are independent processes and unaffected.
      Log('PrepareToInstall: Staging mode. Stopping service for exe replacement.');
      StopService('GVFS.Service');
      WaitForServiceProcessToExit('GVFS.Service');
    end
  else
    begin
      // Clean upgrade: unmount, stop everything, replace files directly.
      // Remove any leftover PendingUpgrade or PreviousVersion from a
      // previous staging install so stale files don't interfere with
      // the fresh install.
      if DirExists(ExpandConstant('{app}\PendingUpgrade')) then
        begin
          Log('PrepareToInstall: Removing leftover PendingUpgrade directory');
          DelTree(ExpandConstant('{app}\PendingUpgrade'), True, True, True);
        end;
      if DirExists(ExpandConstant('{app}\PreviousVersion')) then
        begin
          Log('PrepareToInstall: Removing leftover PreviousVersion directory');
          DelTree(ExpandConstant('{app}\PreviousVersion'), True, True, True);
        end;
      if HasMounts then
        begin
          UnmountRepos();
        end;
      // With CloseApplications=no, Restart Manager won't kill GVFS
      // processes. If unmount-all didn't clean up everything (e.g.
      // registry was empty), force-kill remaining processes since
      // the user already consented to a full upgrade.
      if IsGVFSRunning() then
        begin
          Log('PrepareToInstall: GVFS processes still running after unmount, force-killing');
          Exec('powershell.exe', '-NoProfile "Get-Process gvfs,gvfs.mount -ErrorAction SilentlyContinue | Stop-Process -Force"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
          Sleep(2000);
        end;
      if not EnsureGvfsNotRunning() then
        begin
          Abort();
        end;
      StopService('GVFS.Service');
      UninstallGvFlt();
      UninstallProjFSIfNecessary();
    end;
end;
