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

; Architecture directives: x64 builds allow installation on x64 and ARM64
; (under Prism emulation); ARM64 builds target native ARM64 only.
; ArchSuffix and TargetArch are set via /D on the ISCC command line from
; GVFS.Installers.csproj; defaults handle the case where they're not set
; (e.g. old invocations without the arch parameters).
#ifndef TargetArch
#define TargetArch "x64compatible"
#endif
#ifndef ArchSuffix
#define ArchSuffix ""
#endif

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
OutputBaseFilename=SetupGVFS.{#GVFSVersion}{#ArchSuffix}
OutputDir=Setup
Compression=lzma2
InternalCompressLevel=ultra64
SolidCompression=yes
MinVersion=10.0.17763
DisableDirPage=yes
DisableReadyPage=yes
SetupIconFile="{#LayoutDir}\GitVirtualFileSystem.ico"
ArchitecturesInstallIn64BitMode={#TargetArch}
ArchitecturesAllowed={#TargetArch}
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
; Normal install: all files go to {app}, service gets AfterInstall callback
DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Source:"{#LayoutDir}\*"; Check: IsNormalInstall
DestDir: "{app}"; Flags: ignoreversion; Source:"{#LayoutDir}\GVFS.Service.exe"; AfterInstall: InstallGVFSService; Check: IsNormalInstall
; Staging install: most files go to {app}\PendingUpgrade, but GVFS.Service.exe
; goes directly to {app} so the restarted service has PendingUpgradeHandler code.
; The service is briefly stopped/restarted (mounts are independent processes).
DestDir: "{app}\PendingUpgrade"; Flags: ignoreversion recursesubdirs; Source:"{#LayoutDir}\*"; Check: IsStagingInstall
DestDir: "{app}"; Flags: ignoreversion; Source:"{#LayoutDir}\GVFS.Service.exe"; Check: IsStagingInstall

[Dirs]
Name: "{app}\ProgramData\{#ServiceName}"; Permissions: users-readexec

[UninstallDelete]
; Deletes the entire installation directory, including files and subdirectories
Type: filesandordirs; Name: "{app}";
Type: filesandordirs; Name: "{commonappdata}\GVFS\GVFS.Upgrade";

[Registry]
Root: HKLM; Subkey: "{#EnvironmentKey}"; \
    ValueType: expandsz; ValueName: "PATH"; ValueData: "{olddata};{app}"; \
    Check: NeedsAddPath(ExpandConstant('{app}'))

Root: HKLM; Subkey: "{#FileSystemKey}"; \
    ValueType: dword; ValueName: "NtfsEnableDetailedCleanupResults"; ValueData: "1"; \
    Check: IsWindows10VersionPriorToCreatorsUpdate

Root: HKLM; SubKey: "{#GvFltAutologgerKey}"; Flags: deletekey

[Code]
var
  ExitCode: Integer;
  KeepMountsRunning: Boolean;

function IsNormalInstall(): Boolean;
begin
  Result := not KeepMountsRunning;
end;

function IsStagingInstall(): Boolean;
begin
  Result := KeepMountsRunning;
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

procedure CurStepChanged(CurStep: TSetupStep);
begin
  case CurStep of
    ssInstall:
      begin
        if not KeepMountsRunning then
          UninstallService('GVFS.Service', True);
      end;
    ssPostInstall:
      begin
        if KeepMountsRunning then
          begin
            // All staged files have been written to PendingUpgrade.
            // Write .ready marker so the service knows the staging is
            // complete and safe to apply.
            SaveStringToFile(ExpandConstant('{app}\PendingUpgrade\.ready'), '', False);
            Log('CurStepChanged: Wrote PendingUpgrade .ready marker');

            // Start the service AFTER .ready is written. Previously this
            // was an AfterInstall hook on GVFS.Service.exe, but that races:
            // the service's debounce timer could fire before .ready exists.
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

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Repos: ansiString;
  ResultCode: integer;
  HasMounts: Boolean;
begin
  NeedsRestart := False;
  KeepMountsRunning := False;
  Result := '';

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
          // Interactive mode: show a radio-button modal so the user can pick
          // between remounting (immediate but brief unavailability) and
          // staging the upgrade (deferred until repos are unmounted).
          if not ShowMountChoiceDialog(Repos, KeepMountsRunning) then
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
