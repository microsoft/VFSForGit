; This script requires Inno Setup Compiler 5.5.9 or later to compile
; The Inno Setup Compiler (and IDE) can be found at http://www.jrsoftware.org/isinfo.php

; General documentation on how to use InnoSetup scripts: http://www.jrsoftware.org/ishelp/index.php

#define PrjFltDir PackagesDir + "\" + ProjFSNativePackage + "\filter" 
#define ProjFSNativeLibDir PackagesDir + "\" + ProjFSNativePackage + "\lib" 
#define VCRuntimeDir PackagesDir + "\GVFS.VCRuntime.0.2.0-build\lib\x64"
#define GVFSDir BuildOutputDir + "\GVFS.Windows\bin\" + PlatformAndConfiguration
#define GVFSCommonDir BuildOutputDir + "\GVFS.Common\bin\" + PlatformAndConfiguration + "\netstandard2.0"
#define HooksDir BuildOutputDir + "\GVFS.Hooks.Windows\bin\" + PlatformAndConfiguration
#define HooksLoaderDir BuildOutputDir + "\GitHooksLoader\bin\" + PlatformAndConfiguration
#define ServiceDir BuildOutputDir + "\GVFS.Service.Windows\bin\" + PlatformAndConfiguration
#define ServiceUIDir BuildOutputDir + "\GVFS.Service.UI\bin\" + PlatformAndConfiguration
#define GVFSMountDir BuildOutputDir + "\GVFS.Mount.Windows\bin\" + PlatformAndConfiguration
#define ReadObjectDir BuildOutputDir + "\GVFS.ReadObjectHook.Windows\bin\" + PlatformAndConfiguration
#define VirtualFileSystemDir BuildOutputDir + "\GVFS.VirtualFileSystemHook.Windows\bin\" + PlatformAndConfiguration
#define PostIndexChangedDir BuildOutputDir + "\GVFS.PostIndexChangedHook.Windows\bin\" + PlatformAndConfiguration
#define GVFSUpgraderDir BuildOutputDir + "\GVFS.Upgrader\bin\" + PlatformAndConfiguration + "\net461"

#define MyAppName "GVFS"
#define MyAppInstallerVersion GetFileVersion(GVFSDir + "\GVFS.exe")
#define MyAppPublisher "Microsoft Corporation"
#define MyAppPublisherURL "http://www.microsoft.com"
#define MyAppURL "https://github.com/microsoft/VFSForGit"
#define MyAppExeName "GVFS.exe"
#define EnvironmentKey "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"
#define FileSystemKey "SYSTEM\CurrentControlSet\Control\FileSystem"
#define GvFltAutologgerKey "SYSTEM\CurrentControlSet\Control\WMI\Autologger\Microsoft-Windows-Git-Filter-Log"
#define GVFSConfigFileName "gvfs.config"
#define GVFSStatuscacheTokenFileName "EnableGitStatusCacheToken.dat"
#define ServiceName "GVFS.Service"
#define ServiceUIName "VFS For Git"

[Setup]
AppId={{489CA581-F131-4C28-BE04-4FB178933E6D}
AppName={#MyAppName}
AppVersion={#MyAppInstallerVersion}
VersionInfoVersion={#MyAppInstallerVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppPublisherURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppCopyright=Copyright � Microsoft 2019
BackColor=clWhite
BackSolid=yes
DefaultDirName={pf}\{#MyAppName}
OutputBaseFilename=SetupGVFS.{#GVFSVersion}
OutputDir=Setup
Compression=lzma2
InternalCompressLevel=ultra64
SolidCompression=yes
MinVersion=10.0.14374
DisableDirPage=yes
DisableReadyPage=yes
SetupIconFile="{#GVFSDir}\GitVirtualFileSystem.ico"
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
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

[InstallDelete]
; Delete old dependencies from VS 2015 VC redistributables
Type: files; Name: "{app}\ucrtbase.dll"

[Files]
; PrjFlt Filter Files
DestDir: "{app}\Filter"; Flags: ignoreversion; Source:"{#PrjFltDir}\PrjFlt.sys"
DestDir : "{app}\Filter"; Flags: ignoreversion; Source: "{#PrjFltDir}\prjflt.inf"

; PrjFlt Native Library Files, the GVFS.Service will copy these files into the {app} directory if needed
DestDir: "{app}\ProjFS"; Flags: ignoreversion; Source:"{#ProjFSNativeLibDir}\ProjectedFSLib.dll"

; PrjFlt Managed Assembly Files
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\ProjectedFSLib.Managed.dll"

; GitHooks Files
DestDir: "{app}"; Flags: ignoreversion; Source:"{#HooksDir}\GVFS.Hooks.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#HooksDir}\GVFS.Hooks.exe"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#HooksDir}\GVFS.Hooks.exe.config"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#HooksLoaderDir}\GitHooksLoader.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#HooksLoaderDir}\GitHooksLoader.exe"

; GVFS.Common Files
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSCommonDir}\git2.dll"

; GVFS.Mount Files
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSMountDir}\GVFS.Mount.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSMountDir}\GVFS.Mount.exe"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSMountDir}\GVFS.Mount.exe.config"

; GVFS.Upgrader Files
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSUpgraderDir}\GVFS.Upgrader.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSUpgraderDir}\GVFS.Upgrader.exe"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSUpgraderDir}\GVFS.Upgrader.exe.config"

; GVFS.ReadObjectHook files
DestDir: "{app}"; Flags: ignoreversion; Source:"{#ReadObjectDir}\GVFS.ReadObjectHook.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#ReadObjectDir}\GVFS.ReadObjectHook.exe"

; GVFS.VirtualFileSystemHook files
DestDir: "{app}"; Flags: ignoreversion; Source:"{#VirtualFileSystemDir}\GVFS.VirtualFileSystemHook.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#VirtualFileSystemDir}\GVFS.VirtualFileSystemHook.exe"

; GVFS.PostIndexChangedHook files
DestDir: "{app}"; Flags: ignoreversion; Source:"{#PostIndexChangedDir}\GVFS.PostIndexChangedHook.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#PostIndexChangedDir}\GVFS.PostIndexChangedHook.exe"

; Cpp Dependencies
DestDir: "{app}"; Flags: ignoreversion; Source:"{#VCRuntimeDir}\msvcp140.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#VCRuntimeDir}\msvcp140_1.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#VCRuntimeDir}\msvcp140_2.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#VCRuntimeDir}\vcruntime140.dll"

; GVFS PDB's
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\Esent.Collections.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\Esent.Interop.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\Esent.Isam.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\GVFS.Common.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\GVFS.GVFlt.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\GVFS.Virtualization.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\GVFS.Platform.Windows.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\GVFS.pdb"

; GVFS.Service.UI Files
DestDir: "{app}"; Flags: ignoreversion; Source:"{#ServiceUIDir}\GVFS.Service.UI.exe"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#ServiceUIDir}\GVFS.Service.UI.exe.config" 
DestDir: "{app}"; Flags: ignoreversion; Source:"{#ServiceUIDir}\GVFS.Service.UI.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#ServiceUIDir}\GitVirtualFileSystem.ico"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#ServiceUIDir}\System.Runtime.dll"

; GVFS Files
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\CommandLine.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\Esent.Collections.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\Esent.Interop.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\Esent.Isam.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\Microsoft.Data.Sqlite.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\SQLitePCLRaw.batteries_green.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\SQLitePCLRaw.batteries_v2.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\SQLitePCLRaw.core.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\SQLitePCLRaw.provider.e_sqlite3.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\x64\e_sqlite3.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\GVFS.Common.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\GVFS.GVFlt.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\GVFS.Virtualization.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\GVFS.Platform.Windows.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\Newtonsoft.Json.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\ICSharpCode.SharpZipLib.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\GVFS.exe.config"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\GitVirtualFileSystem.ico"  
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\GVFS.exe" 

; NuGet support DLLs
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\NuGet.Commands.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\NuGet.Common.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\NuGet.Configuration.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\NuGet.Frameworks.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\NuGet.Packaging.Core.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\NuGet.Packaging.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\NuGet.Protocol.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\NuGet.Versioning.dll"

; .NET Standard Files
; See https://github.com/dotnet/standard/issues/415 for a discussion on why this are copied
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\netstandard.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\System.Net.Http.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\System.ValueTuple.dll"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#GVFSDir}\System.IO.Compression.dll"

; GVFS.Service Files and PDB's
DestDir: "{app}"; Flags: ignoreversion; Source:"{#ServiceDir}\GVFS.Service.pdb"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#ServiceDir}\GVFS.Service.exe.config"
DestDir: "{app}"; Flags: ignoreversion; Source:"{#ServiceDir}\GVFS.Service.exe"; AfterInstall: InstallGVFSService

[Dirs]
Name: "{app}\ProgramData\{#ServiceName}"; Permissions: users-readexec

[Icons]
Name: "{commonstartmenu}\{#ServiceUIName}"; Filename: "{app}\GVFS.Service.UI.exe"; AppUserModelID: "GVFS"

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
  // ErrorCode 1060 means service not installed, 1062 means service not started
  if not Exec(ExpandConstant('{sys}\SC.EXE'), 'stop ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode <> 1060) and (ResultCode <> 1062) then
    begin
      RaiseException('Fatal: Could not stop service: ' + ServiceName);
    end;
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
  
  try
    if Exec(ExpandConstant('{sys}\SC.EXE'), ExpandConstant('create GVFS.Service binPath="{app}\GVFS.Service.exe" start=auto'), '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
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

procedure StartGVFSServiceUI();
var
  ResultCode: integer;
begin
  if GetEnv("GVFS_UNATTENDED") = "1" then
    begin
      Log('StartGVFSServiceUI: Skipping launching GVFS.Service.UI');
    end
  else if ExecAsOriginalUser(ExpandConstant('{app}\GVFS.Service.UI.exe'), '', '', SW_HIDE, ewNoWait, ResultCode) then
    begin
      Log('StartGVFSServiceUI: Successfully launched GVFS.Service.UI');
    end
  else
    begin
      Log('StartGVFSServiceUI: Failed to launch GVFS.Service.UI');
    end;
end;

procedure StopGVFSServiceUI();
var
  ResultCode: integer;
begin
  if Exec('powershell.exe', '-NoProfile "Stop-Process -Name GVFS.Service.UI"', '', SW_HIDE, ewNoWait, ResultCode) then
    begin
      Log('StopGVFSServiceUI: Successfully stopped GVFS.Service.UI');
    end
  else
    begin
      RaiseException('Fatal: Could not stop process: GVFS.Service.UI');
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

function ConfirmUnmountAll(): Boolean;
var
  MsgBoxResult: integer;
  Repos: ansiString;
  ResultCode: integer;
  MsgBoxText: string;
begin
  Result := False;
  if ExecWithResult('gvfs.exe', 'service --list-mounted', '', SW_HIDE, ewWaitUntilTerminated, ResultCode, Repos) then
    begin
      if Repos = '' then
        begin
          Result := False;
        end
      else
        begin
          if ResultCode = 0 then
            begin
              MsgBoxText := 'The following repos are currently mounted:' + #13#10 + Repos + #13#10 + 'Setup needs to unmount all repos before it can proceed, and those repos will be unavailable while setup is running. Do you want to continue?';
              MsgBoxResult := SuppressibleMsgBox(MsgBoxText, mbConfirmation, MB_OKCANCEL, IDOK);
              if (MsgBoxResult = IDOK) then
                begin
                  Result := True;
                end
              else
                begin
                  Abort();
                end;
            end;
        end;
    end;
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

procedure CurStepChanged(CurStep: TSetupStep);
begin
  case CurStep of
    ssInstall:
      begin
        UninstallService('GVFS.Service', True);
      end;
    ssPostInstall:
      begin
        MigrateConfigAndStatusCacheFiles();
        StartGVFSServiceUI();
        if ExpandConstant('{param:REMOUNTREPOS|true}') = 'true' then
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
        StopGVFSServiceUI();
        UninstallService('GVFS.Service', False);
        RemovePath(ExpandConstant('{app}'));
      end;
    end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  NeedsRestart := False;
  Result := '';
  SetNuGetFeedIfNecessary();
  if ConfirmUnmountAll() then
    begin
      if ExpandConstant('{param:REMOUNTREPOS|true}') = 'true' then
        begin
          UnmountRepos();
        end
    end;
  if not EnsureGvfsNotRunning() then
    begin
      Abort();
    end;
  StopService('GVFS.Service');
  StopGVFSServiceUI();
  UninstallGvFlt();
  UninstallProjFSIfNecessary();
end;
