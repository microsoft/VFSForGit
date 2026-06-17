; This script requires Inno Setup Compiler 5.5.9 or later to compile
; The Inno Setup Compiler (and IDE) can be found at http://www.jrsoftware.org/isinfo.php

; General documentation on how to use InnoSetup scripts: http://www.jrsoftware.org/ishelp/index.php

#define MyAppName "VFS for Git"
#define MyAppInstallerVersion GetFileVersion(LayoutDir + "\GVFS.exe")
#define MyAppPublisher "Microsoft"
#define MyAppPublisherURL "http://www.microsoft.com"
#define MyAppURL "https://github.com/microsoft/VFSForGit"
#define MyAppExeName "GVFS.exe"
#define EnvironmentKey "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"
#define FileSystemKey "SYSTEM\CurrentControlSet\Control\FileSystem"
#define GvFltAutologgerKey "SYSTEM\CurrentControlSet\Control\WMI\Autologger\Microsoft-Windows-Git-Filter-Log"
#define GVFSConfigFileName "gvfs.config"
#define GVFSStatuscacheTokenFileName "EnableGitStatusCacheToken.dat"
#define ServiceName "GVFS.Service"

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

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl";

[Types]
Name: "full"; Description: "Full installation"; Flags: iscustom;

[Components]

[InstallDelete]
; Delete old dependencies from VS 2015 VC redistributables
Type: files; Name: "{app}\ucrtbase.dll"

[Files]
; Versioned install: all files go to {app}\Versions\{version}
; Service binary gets AfterInstall callback to register service
; Check: ShouldDeployFiles skips copy on same-version reinstall (files locked by running processes).
DestDir: "{app}\Versions\{#MyAppInstallerVersion}"; Flags: recursesubdirs; Source:"{#LayoutDir}\*"; Check: ShouldDeployFiles
DestDir: "{app}\Versions\{#MyAppInstallerVersion}"; Source:"{#LayoutDir}\GVFS.Service.exe"; AfterInstall: InstallGVFSService; Check: ShouldDeployFiles

[Dirs]
Name: "{app}\ProgramData\{#ServiceName}"; Permissions: users-readexec

[UninstallDelete]
; Deletes the entire installation directory, including files and subdirectories
Type: filesandordirs; Name: "{app}";
Type: filesandordirs; Name: "{commonappdata}\GVFS\GVFS.Upgrade";

[Registry]
Root: HKLM; Subkey: "{#EnvironmentKey}"; \
    ValueType: expandsz; ValueName: "PATH"; ValueData: "{olddata};{app}\Current"; \
    Check: NeedsAddPath(ExpandConstant('{app}\Current'))

Root: HKLM; Subkey: "{#FileSystemKey}"; \
    ValueType: dword; ValueName: "NtfsEnableDetailedCleanupResults"; ValueData: "1"; \
    Check: IsWindows10VersionPriorToCreatorsUpdate

Root: HKLM; SubKey: "{#GvFltAutologgerKey}"; Flags: deletekey

[Code]
var
  ExitCode: Integer;
  KeepMountsRunning: Boolean;
  SkipFileDeploy: Boolean;

function ShouldDeployFiles(): Boolean;
begin
  Result := not SkipFileDeploy;
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
  // look for the path with leading and trailing semicolon
  // Pos() returns 0 if not found
  Result := Pos(';' + Param + ';', ';' + OrigPath + ';') = 0;
end;

function IsWindows10VersionPriorToCreatorsUpdate(): Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result := (Version.Major = 10) and (Version.Minor = 0) and (Version.Build < 15063);
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
  FilePath := ExpandConstant('{app}\Versions\{#MyAppInstallerVersion}\OnDiskVersion16CapableInstallation.dat');
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
    // Use the direct versioned path for sc create + start since the Current junction
    // isn't created until ssPostInstall (after file extraction). The junction path
    // would be prettier but isn't available yet at AfterInstall time.
    if Exec(ExpandConstant('{sys}\SC.EXE'), ExpandConstant('create GVFS.Service binPath= "\"{app}\Versions\{#MyAppInstallerVersion}\GVFS.Service.exe\"" start= auto'), '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
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

// StagingUpdateService removed - staging upgrade flow replaced by versioned layout with junction swap.
// Service install/start is handled in InstallGVFSService.

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
  SecureAppDataDir := ExpandConstant('{app}\Current\ProgramData');

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


procedure CreateOrUpdateCurrentJunction();
var
  AppDir: string;
  JunctionPath: string;
  JunctionNew: string;
  VersionDir: string;
  ResultCode: integer;
begin
  AppDir := ExpandConstant('{app}');
  JunctionPath := AppDir + '\Current';
  JunctionNew := AppDir + '\Current.new';
  VersionDir := AppDir + '\Versions\{#MyAppInstallerVersion}';

  Log('[GVFS-INSTALL] CreateOrUpdateCurrentJunction: Target version = {#MyAppInstallerVersion}');

  // Fix #4: Atomic junction swap using .new temporary
  // Create new junction at Current.new
  Log('[GVFS-INSTALL] CreateOrUpdateCurrentJunction: Creating junction Current.new -> ' + VersionDir);
  if not Exec(ExpandConstant('{cmd}'), '/C mklink /J "' + JunctionNew + '" "' + VersionDir + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    begin
      Log('[GVFS-INSTALL] CreateOrUpdateCurrentJunction: mklink /J failed with exit code ' + IntToStr(ResultCode));
      RaiseException('Fatal: Could not create Current.new junction at ' + JunctionNew);
    end;

  // Remove existing Current junction if present
  if DirExists(JunctionPath) then
    begin
      Log('[GVFS-INSTALL] CreateOrUpdateCurrentJunction: Removing existing Current junction');
      if not Exec(ExpandConstant('{cmd}'), '/C rmdir "' + JunctionPath + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
        begin
          Log('[GVFS-INSTALL] CreateOrUpdateCurrentJunction: WARNING - rmdir failed with exit code ' + IntToStr(ResultCode));
          // Continue anyway - rename might still work
        end;
    end;

  // Rename Current.new -> Current
  Log('[GVFS-INSTALL] CreateOrUpdateCurrentJunction: Renaming Current.new -> Current');
  if not Exec(ExpandConstant('{cmd}'), '/C ren "' + JunctionNew + '" Current', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    begin
      Log('[GVFS-INSTALL] CreateOrUpdateCurrentJunction: ren failed with exit code ' + IntToStr(ResultCode));
      // Fallback: if Current.new exists, at least installer can reference it
      if DirExists(JunctionNew) then
        begin
          Log('[GVFS-INSTALL] CreateOrUpdateCurrentJunction: WARNING - Using Current.new as fallback');
        end
      else
        begin
          RaiseException('Fatal: Could not rename Current.new to Current');
        end;
    end
  else
    begin
      Log('[GVFS-INSTALL] CreateOrUpdateCurrentJunction: Junction created successfully');
    end;
end;

function GetFileVersion(FilePath: string): string;
var
  VersionMS: Cardinal;
  VersionLS: Cardinal;
begin
  Result := '';
  if GetVersionNumbers(FilePath, VersionMS, VersionLS) then
    begin
      Result := Format('%d.%d.%d.%d', [
        VersionMS shr 16,
        VersionMS and $FFFF,
        VersionLS shr 16,
        VersionLS and $FFFF
      ]);
    end;
end;

function IsProcessRunningFromPath(PathPrefix: string): Boolean;
var
  ResultCode: integer;
  PowerShellCmd: string;
begin
  // PowerShell: check if any gvfs.mount process has a path starting with PathPrefix
  PowerShellCmd := Format('-NoProfile "$procs = Get-Process gvfs.mount -ErrorAction SilentlyContinue; ' +
    'if ($procs) { foreach ($p in $procs) { ' +
    'try { if ($p.Path -like ''%s*'') { exit 10 } } catch {} } }; exit 0"', [PathPrefix]);
  
  if Exec('powershell.exe', PowerShellCmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      Result := (ResultCode = 10);
    end
  else
    begin
      Log('[GVFS-INSTALL] IsProcessRunningFromPath: PowerShell query failed');
      Result := False;
    end;
end;

procedure GarbageCollectOldVersions();
var
  AppDir: string;
  VersionsDir: string;
  CurrentVersion: string;
  FlatGvfsExe: string;
  FlatVersion: string;
  FindRec: TFindRec;
  VersionDirs: array of string;
  Count: integer;
  I, J: integer;
  TempStr: string;
  VersionPath: string;
  CanDelete: Boolean;
begin
  AppDir := ExpandConstant('{app}');
  VersionsDir := AppDir + '\Versions';
  CurrentVersion := '{#MyAppInstallerVersion}';

  Log('[GVFS-INSTALL] GarbageCollectOldVersions: Current version = ' + CurrentVersion);

  // First, check for flat-layout binaries at {app}\GVFS.exe
  FlatGvfsExe := AppDir + '\GVFS.exe';
  if FileExists(FlatGvfsExe) then
    begin
      FlatVersion := GetFileVersion(FlatGvfsExe);
      Log('[GVFS-INSTALL] GarbageCollectOldVersions: Detected flat layout with version ' + FlatVersion);
      
      // Check if any mounts are running from the flat install
      if IsProcessRunningFromPath(AppDir + '\') then
        begin
          Log('[GVFS-INSTALL] GarbageCollectOldVersions: Mounts running from flat layout - leaving in place');
        end
      else
        begin
          Log('[GVFS-INSTALL] GarbageCollectOldVersions: No mounts running from flat layout - would migrate to Versions\' + FlatVersion);
          // For now, just log. Full migration logic can move files to Versions\<FlatVersion>.
          // Defer to avoid complexity in first PR.
        end;
    end;

  // Enumerate version directories
  Count := 0;
  SetArrayLength(VersionDirs, 0);
  
  if not DirExists(VersionsDir) then
    begin
      Log('[GVFS-INSTALL] GarbageCollectOldVersions: Versions directory does not exist');
      exit;
    end;

  if FindFirst(VersionsDir + '\*', FindRec) then
    begin
      try
        repeat
          if (FindRec.Name <> '.') and (FindRec.Name <> '..') and (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0) then
            begin
              // Skip the current version
              if FindRec.Name <> CurrentVersion then
                begin
                  SetArrayLength(VersionDirs, Count + 1);
                  VersionDirs[Count] := FindRec.Name;
                  Count := Count + 1;
                end;
            end;
        until not FindNext(FindRec);
      finally
        FindClose(FindRec);
      end;
    end;

  if Count = 0 then
    begin
      Log('[GVFS-INSTALL] GarbageCollectOldVersions: No old versions to clean up');
      exit;
    end;

  Log('[GVFS-INSTALL] GarbageCollectOldVersions: Found ' + IntToStr(Count) + ' old version(s)');

  // Sort by version name (oldest first — lower version strings sort earlier)
  for I := 0 to Count - 2 do
    begin
      for J := I + 1 to Count - 1 do
        begin
          if CompareText(VersionDirs[I], VersionDirs[J]) > 0 then
            begin
              TempStr := VersionDirs[I];
              VersionDirs[I] := VersionDirs[J];
              VersionDirs[J] := TempStr;
            end;
        end;
    end;

  // Keep the 1 most recent old version (index Count-1), delete the rest
  for I := 0 to Count - 2 do
    begin
      VersionPath := VersionsDir + '\' + VersionDirs[I];
      Log('[GVFS-INSTALL] GarbageCollectOldVersions: Checking version ' + VersionDirs[I]);
      
      // Check if any mounts are running from this version
      CanDelete := not IsProcessRunningFromPath(VersionPath + '\');
      
      if CanDelete then
        begin
          Log('[GVFS-INSTALL] GarbageCollectOldVersions: Deleting old version ' + VersionDirs[I]);
          if DelTree(VersionPath, True, True, True) then
            Log('[GVFS-INSTALL] GarbageCollectOldVersions: Deleted ' + VersionPath)
          else
            Log('[GVFS-INSTALL] GarbageCollectOldVersions: Failed to delete ' + VersionPath);
        end
      else
        begin
          Log('[GVFS-INSTALL] GarbageCollectOldVersions: Version ' + VersionDirs[I] + ' has running mounts - skipping');
        end;
    end;

  // Log the most recent old version that we're keeping
  if Count > 0 then
    Log('[GVFS-INSTALL] GarbageCollectOldVersions: Keeping most recent old version ' + VersionDirs[Count - 1]);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: integer;
begin
  NeedsRestart := False;
  KeepMountsRunning := False;
  SkipFileDeploy := False;
  Result := '';
  SetNuGetFeedIfNecessary();

  // Same-version no-op: if Versions\{ver}\ already exists with gvfs.exe,
  // this is a same-version reinstall. Skip file deployment to avoid conflicts
  // with locked files. Still re-create junction and service registration.
  if FileExists(ExpandConstant('{app}\Versions\{#MyAppInstallerVersion}\GVFS.exe')) then
    begin
      Log('[GVFS-INSTALL] PrepareToInstall: Version {#MyAppInstallerVersion} already deployed - skipping file copy');
      SkipFileDeploy := True;
    end
  else
    begin
      Log('[GVFS-INSTALL] PrepareToInstall: Deploying new version {#MyAppInstallerVersion}');
    end;

  // Versioned layout: new version deploys to a fresh Versions\{ver}\ folder,
  // so running GVFS.Mount processes do NOT hold locks on the files we're writing.
  // No need to force-close mounts or abort. Just stop the service for re-registration.
  Log('PrepareToInstall: Versioned layout - skipping GVFS process check');

  // Clean upgrade: remove leftover staging dirs from old installs.
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

  if not SkipFileDeploy then
    begin
      StopService('GVFS.Service');
    end
  else
    Log('[GVFS-INSTALL] PrepareToInstall: Same-version no-op, leaving service running');
end;

function UninstallNeedRestart(): Boolean;
begin
  Result := False;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  case CurStep of
    ssInstall:
      begin
        if not SkipFileDeploy then
          UninstallService('GVFS.Service', True);
      end;
    ssPostInstall:
      begin
        // Create the Current junction AFTER file extraction (the target
        // Versions\{ver}\ directory must exist for mklink /J to succeed).
        CreateOrUpdateCurrentJunction();

        // Remove legacy flat PATH entry on upgrade from flat layout.
        RemovePath(ExpandConstant('{app}'));
        
        GarbageCollectOldVersions();
        MigrateConfigAndStatusCacheFiles();
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
        RemovePath(ExpandConstant('{app}\Current'));
      end;
    end;
end;
