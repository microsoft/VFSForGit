; This script requires Inno Setup Compiler 5.5.9 or later to compile
; The Inno Setup Compiler (and IDE) can be found at http://www.jrsoftware.org/isinfo.php

; General documentation on how to use InnoSetup scripts: http://www.jrsoftware.org/ishelp/index.php

#define MyAppName "RGFS"
#define MyAppInstallerVersion GetFileVersion("RGFS.exe")
#define MyAppPublisher "Microsoft Corporation"
#define MyAppPublisherURL "http://www.microsoft.com"
#define MyAppURL "https://github.com/Microsoft/rgfs"
#define MyAppExeName "RGFS.exe"
#define EnvironmentKey "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"

#define GVFltRelative "..\..\..\..\..\packages\" + GvFltPackage + "\filter" 
#define RGFSCommonRelative "..\..\..\..\RGFS.Common\bin"
#define HooksRelative "..\..\..\..\RGFS.Hooks\bin"
#define HooksLoaderRelative "..\..\..\..\GitHooksLoader\bin"
#define ServiceRelative "..\..\..\..\RGFS.Service\bin"
#define ServiceUIRelative "..\..\..\..\RGFS.Service.UI\bin"
#define RGFSMountRelative "..\..\..\..\RGFS.Mount\bin"
#define ReadObjectRelative "..\..\..\..\RGFS.ReadObjectHook\bin"

[Setup]
AppId={{489CA581-F131-4C28-BE04-4FB178933E6D}
AppName={#MyAppName}
AppVersion={#MyAppInstallerVersion}
VersionInfoVersion={#MyAppInstallerVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppPublisherURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppCopyright=Copyright © Microsoft 2017
BackColor=clWhite
BackSolid=yes
DefaultDirName={pf}\{#MyAppName}
OutputBaseFilename=SetupRGFS
OutputDir=Setup
Compression=lzma2
InternalCompressLevel=ultra64
SolidCompression=yes
MinVersion=10.0.15063
DisableDirPage=yes
DisableReadyPage=yes
SetupIconFile=GitVirtualFileSystem.ico
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
;WizardImageFile=Assets\gcmicon128.bmp
;WizardSmallImageFile=Assets\gcmicon64.bmp
WizardImageStretch=no
WindowResizable=no
CloseApplications=yes
ChangesEnvironment=yes
RestartIfNeededByRun=yes   

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl";

[Types]
Name: "full"; Description: "Full installation"; Flags: iscustom;

[Components]

[Files]
; GVFlt Files
DestDir: "{app}\Filter"; Flags: ignoreversion; Source:"{#GVFltRelative}\GvFlt.sys"
; gvflt.inf is declared explicitly last within the filter files, so we run the GVFlt install only once after required filter files are present
DestDir: "{app}\Filter"; Flags: ignoreversion; Source: "{#GVFltRelative}\gvflt.inf"; AfterInstall: InstallGVFlt

; GitHooks Files
DestDir: "{app}"; Flags: ignoreversion; Source:"{#HooksRelative}\{#PlatformAndConfiguration}\RGFS.Hooks.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#HooksRelative}\{#PlatformAndConfiguration}\RGFS.Hooks.exe"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#HooksRelative}\{#PlatformAndConfiguration}\RGFS.Hooks.exe.config"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#HooksLoaderRelative}\{#PlatformAndConfiguration}\GitHooksLoader.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#HooksLoaderRelative}\{#PlatformAndConfiguration}\GitHooksLoader.exe"

; RGFS.Common Files
DestDir: "{app}"; Flags: ignoreversion; Source:"{#RGFSCommonRelative}\{#PlatformAndConfiguration}\git2.dll"

; RGFS.Mount Files
DestDir: "{app}"; Flags: ignoreversion; Source:"{#RGFSMountRelative}\{#PlatformAndConfiguration}\RGFS.Mount.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#RGFSMountRelative}\{#PlatformAndConfiguration}\RGFS.Mount.exe"

; RGFS.ReadObjectHook files
DestDir: "{app}"; Flags: ignoreversion; Source:"{#ReadObjectRelative}\{#PlatformAndConfiguration}\RGFS.ReadObjectHook.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#ReadObjectRelative}\{#PlatformAndConfiguration}\RGFS.ReadObjectHook.exe"

; RGFS and FastFetch PDB's
DestDir: "{app}"; Flags: ignoreversion; Source:"Esent.Collections.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"Esent.Interop.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"Esent.Isam.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"FastFetch.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"RGFS.Common.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"RGFS.GVFlt.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"RGFS.pdb"

; RGFS.Service.UI Files
DestDir: "{app}"; Flags: ignoreversion; Source:"{#ServiceUIRelative}\{#PlatformAndConfiguration}\RGFS.Service.UI.exe" 
DestDir: "{app}"; Flags: ignoreversion; Source:"{#ServiceUIRelative}\{#PlatformAndConfiguration}\RGFS.Service.UI.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#ServiceUIRelative}\{#PlatformAndConfiguration}\GitVirtualFileSystem.ico"

; FastFetch Files
DestDir: "{app}"; Flags: ignoreversion; Source:"FastFetch.exe"

; RGFS Files
DestDir: "{app}"; Flags: ignoreversion; Source:"CommandLine.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"Esent.Collections.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"Esent.Interop.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"Esent.Isam.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"RGFS.Common.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"RGFS.GVFlt.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"GvLib.Managed.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"GvLib.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"Microsoft.Diagnostics.Tracing.EventSource.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"Newtonsoft.Json.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"RGFS.exe.config"
DestDir: "{app}"; Flags: ignoreversion; Source:"GitVirtualFileSystem.ico"  
DestDir: "{app}"; Flags: ignoreversion; Source:"RGFS.exe" 

; RGFS.Service Files and PDB's
DestDir: "{app}"; Flags: ignoreversion; Source:"{#ServiceRelative}\{#PlatformAndConfiguration}\RGFS.Service.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#ServiceRelative}\{#PlatformAndConfiguration}\RGFS.Service.exe"; AfterInstall: InstallRGFSService

[UninstallDelete]
; Deletes the entire installation directory, including files and subdirectories
Type: filesandordirs; Name: "{app}";

[Registry]
Root: HKLM; Subkey: "{#EnvironmentKey}"; \
    ValueType: expandsz; ValueName: "PATH"; ValueData: "{olddata};{app}"; \
    Check: NeedsAddPath(ExpandConstant('{app}'))

[Code]
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

procedure StopRGFSService();
var
  ResultCode: integer;
begin
  // ErrorCode 1060 means service not installed, 1062 means service not started
  if not Exec(ExpandConstant('SC.EXE'), 'stop RGFS.Service', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode <> 1060) and (ResultCode <> 1062) then
    begin
      RaiseException('Fatal: Could not stop existing RGFS.Service.');
    end;
end;

procedure UninstallRGFSService(ShowProgress: boolean);
var
  ResultCode: integer;
begin
  if Exec(ExpandConstant('SC.EXE'), 'query RGFS.Service', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode <> 1060) then
    begin

      if (ShowProgress) then
        begin
          WizardForm.StatusLabel.Caption := 'Uninstalling existing RGFS.Service.';
          WizardForm.ProgressGauge.Style := npbstMarquee;
        end;

      try
        StopRGFSService();

        if not Exec(ExpandConstant('SC.EXE'), 'delete RGFS.Service', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
          begin
            RaiseException('Fatal: Could not uninstall existing RGFS.Service.');
          end;

          if (ShowProgress) then
            begin
              WizardForm.StatusLabel.Caption := 'Waiting for pending RGFS.Service deletion to complete. This may take a while.';
            end;

      finally
        if (ShowProgress) then
          begin
            WizardForm.ProgressGauge.Style := npbstNormal;
          end;
      end;

    end;
end;

procedure InstallRGFSService();
var
  ResultCode: integer;
  StatusText: string;
  InstallSuccessful: Boolean;
begin
  InstallSuccessful := False;
  
  StatusText := WizardForm.StatusLabel.Caption;
  WizardForm.StatusLabel.Caption := 'Installing RGFS.Service.';
  WizardForm.ProgressGauge.Style := npbstMarquee;
  
  try
    if Exec(ExpandConstant('SC.EXE'), ExpandConstant('create RGFS.Service binPath="{app}\RGFS.Service.exe" start=auto'), '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
      begin
        if Exec(ExpandConstant('SC.EXE'), 'failure RGFS.Service reset= 30 actions= restart/10/restart/5000//1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
          begin
            if Exec(ExpandConstant('SC.EXE'), 'start RGFS.Service', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
              begin
                InstallSuccessful := True;
              end;
          end;
      end;

  finally
    WizardForm.StatusLabel.Caption := StatusText;
    WizardForm.ProgressGauge.Style := npbstNormal;
  end;

  if InstallSuccessful = False then
    begin
      RaiseException('Fatal: An error occured while installing RGFS.Service.');
    end;
end;

procedure InstallGVFlt();
var
  ResultCode: integer;
  StatusText: string;
  InstallSuccessful: Boolean;
begin
  InstallSuccessful := False;

  StatusText := WizardForm.StatusLabel.Caption;
  WizardForm.StatusLabel.Caption := 'Installing GVFlt Driver.';
  WizardForm.ProgressGauge.Style := npbstMarquee;

  try
    Exec(ExpandConstant('SC.EXE'), 'stop gvflt', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // Note: Programatic install of INF notifies user if the driver being upgraded to is older than the existing, otherwise it works silently... doesn't seem like there is a way to block
    if Exec(ExpandConstant('RUNDLL32.EXE'), ExpandConstant('SETUPAPI.DLL,InstallHinfSection DefaultInstall 128 {app}\Filter\gvflt.inf'), '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      begin
        InstallSuccessful := True;
      end;
  finally
    WizardForm.StatusLabel.Caption := StatusText;
    WizardForm.ProgressGauge.Style := npbstNormal;    
    Exec(ExpandConstant('SC.EXE'), 'start gvflt', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;

  if InstallSuccessful = False then
    begin
      RaiseException('Fatal: An error occured while installing GVFlt drivers.');
    end;
end;

function IsRGFSRunning(): Boolean;
var
  ResultCode: integer;
begin
  if Exec('powershell.exe', '-NoProfile "Get-Process rgfs,rgfs.mount | foreach {exit 10}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
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

function EnsureRgfsNotRunning(): Boolean;
var
  MsgBoxResult: Integer;
begin
  MsgBoxResult := IDRETRY;
  while (IsRGFSRunning()) Do
    begin    
      if(MsgBoxResult = IDRETRY) then
        begin
          MsgBoxResult := SuppressibleMsgBox('RGFS is currently running. Please close all instances of RGFS before continuing the installation.', mbError, MB_RETRYCANCEL, IDCANCEL);
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
  Result := EnsureRgfsNotRunning();
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
        UninstallRGFSService(True);
      end;
    end;
end;

procedure CurUninstallStepChanged(CurStep: TUninstallStep);
begin
  case CurStep of
    usUninstall:
      begin
	    UninstallRGFSService(False);

        RemovePath(ExpandConstant('{app}'));
      end;
    end;
end;

procedure InitializeWizard;
begin
  if not EnsureRgfsNotRunning() then
    begin
      Abort();
    end;

  StopRGFSService();
end;