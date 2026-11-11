type
  TTelemetryGuid = record
    Data1: LongWord;
    Data2, Data3: Word;
    Data4: array[0..7] of Byte;
  end;
  TTelemetrySystemTime = record
    Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds: Word;
  end;
  TTelemetrySecurityAttributes = record
    Length, Descriptor, InheritHandle: LongWord;
  end;
  TTelemetryAclSize = record
    Count, Used, Free: LongWord;
  end;
  TTelemetryAce = record
    Kind, Flags: Byte;
    Size: Word;
    Mask: LongWord;
  end;

function TelemetryCoCreateGuid(var Value: TTelemetryGuid): Integer;
  external 'CoCreateGuid@ole32.dll stdcall';
function TelemetryGuidToString(var Value: TTelemetryGuid; Buffer: String; Count: Integer): Integer;
  external 'StringFromGUID2@ole32.dll stdcall';
procedure TelemetryGetSystemTime(var Value: TTelemetrySystemTime);
  external 'GetSystemTime@kernel32.dll stdcall';
function TelemetrySystemTimeToFileTime(var Value: TTelemetrySystemTime; var FileTime: TFileTime): Boolean;
  external 'SystemTimeToFileTime@kernel32.dll stdcall';
function TelemetryConvertDescriptor(Value: String; Revision: LongWord; var Descriptor, Size: LongWord): Boolean;
  external 'ConvertStringSecurityDescriptorToSecurityDescriptorW@advapi32.dll stdcall';
function TelemetryCreateDirectory(Path: String; var Attributes: TTelemetrySecurityAttributes): Boolean;
  external 'CreateDirectoryW@kernel32.dll stdcall';
function TelemetryCreateFile(Path: String; Access, Sharing: LongWord; var Attributes: TTelemetrySecurityAttributes;
  Disposition, Flags, Template: LongWord): LongWord;
  external 'CreateFileW@kernel32.dll stdcall';
function TelemetryWriteFile(Handle: LongWord; Buffer: AnsiString; Count: LongWord; var Written: LongWord; Overlapped: LongWord): Boolean;
  external 'WriteFile@kernel32.dll stdcall';
function TelemetryCloseHandle(Handle: LongWord): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function TelemetryDeleteFile(Path: String): Boolean;
  external 'DeleteFileW@kernel32.dll stdcall';
function TelemetryGetSecurity(Path: String; Kind, Information: LongWord;
  var Owner, Group, Dacl, Sacl, Descriptor: LongWord): LongWord;
  external 'GetNamedSecurityInfoW@advapi32.dll stdcall';
function TelemetryGetAclInformation(Acl: LongWord; var Information: TTelemetryAclSize; Size, Kind: LongWord): Boolean;
  external 'GetAclInformation@advapi32.dll stdcall';
function TelemetryGetAce(Acl, Index: LongWord; var Ace: LongWord): Boolean;
  external 'GetAce@advapi32.dll stdcall';
procedure TelemetryCopyAce(var Target: TTelemetryAce; Source, Size: LongWord);
  external 'RtlMoveMemory@kernel32.dll stdcall';
function TelemetrySidToString(Sid: LongWord; var Text: LongWord): Boolean;
  external 'ConvertSidToStringSidW@advapi32.dll stdcall';
function TelemetryCopyString(Target: String; Source: LongWord; Count: Integer): LongWord;
  external 'lstrcpynW@kernel32.dll stdcall';
function TelemetryLocalFree(Value: LongWord): LongWord;
  external 'LocalFree@kernel32.dll stdcall';
function TelemetryGetAttributes(Path: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';

var
  TelemetryDirectory, TelemetryAttemptId, TelemetryStartId, TelemetryFinishId: String;
  TelemetryPreviousVersion, TelemetryMode, TelemetryOriginalMode, TelemetryOutcome, TelemetryPhase, TelemetryRemount: String;
  TelemetryStarted, TelemetryFinished: Boolean;
  TelemetryScriptResult: Integer;

function TelemetryGuid(): String;
var
  Value: TTelemetryGuid;
begin
  if TelemetryCoCreateGuid(Value) <> 0 then
    RaiseException('Cannot create an installation telemetry identifier.');
  SetLength(Result, 39);
  if TelemetryGuidToString(Value, Result, 39) = 0 then
    RaiseException('Cannot format an installation telemetry identifier.');
  Result := Lowercase(Copy(Result, 2, 36));
end;

function TelemetryTrustedSid(Sid: LongWord): Boolean;
var
  Buffer: String;
  Text: LongWord;
begin
  Result := False;
  if not TelemetrySidToString(Sid, Text) then exit;
  try
    SetLength(Buffer, 256);
    TelemetryCopyString(Buffer, Text, 256);
    SetLength(Buffer, Pos(#0, Buffer) - 1);
    Result := (Buffer = 'S-1-5-18') or (Buffer = 'S-1-5-32-544') or
      (Buffer = 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464');
#ifdef TelemetryTestSid
    Result := Result or (Buffer = '{#TelemetryTestSid}');
#endif
  finally
    TelemetryLocalFree(Text);
  end;
end;

function TelemetrySafePath(Path: String; Leaf: Boolean): Boolean;
var
  Owner, Group, Dacl, Sacl, Descriptor, AcePointer, Index, UnsafeRights, Attributes: LongWord;
  AclSize: TTelemetryAclSize;
  Ace: TTelemetryAce;
begin
  Result := False;
  Attributes := TelemetryGetAttributes(Path);
  if (Attributes = $FFFFFFFF) or ((Attributes and $400) <> 0) or ((Attributes and $10) = 0) then exit;
  if TelemetryGetSecurity(Path, 1, 5, Owner, Group, Dacl, Sacl, Descriptor) <> 0 then exit;
  try
    if not TelemetryTrustedSid(Owner) or (Dacl = 0) then exit;
    if not TelemetryGetAclInformation(Dacl, AclSize, 12, 2) then exit;
    if AclSize.Count = 0 then exit;
    UnsafeRights := $100D0040;
    if Leaf then UnsafeRights := $500D0156;
    if AclSize.Count > 0 then
      for Index := 0 to AclSize.Count - 1 do
        begin
          if not TelemetryGetAce(Dacl, Index, AcePointer) then exit;
          TelemetryCopyAce(Ace, AcePointer, 8);
          if (Ace.Flags and $08) = 0 then
            begin
              if (Ace.Kind <> 0) and (Ace.Kind <> 1) then exit;
              if (Ace.Kind = 0) and ((Ace.Mask and UnsafeRights) <> 0) and
                not TelemetryTrustedSid(AcePointer + 8) then exit;
            end;
        end;
    Result := True;
  finally
    TelemetryLocalFree(Descriptor);
  end;
end;

function TelemetrySafeParents(Path: String): Boolean;
var
  Parent: String;
begin
  Result := False;
  while Path <> '' do
    begin
      if not TelemetrySafePath(Path, False) then exit;
      Parent := ExtractFileDir(Path);
      if Parent = Path then break;
      Path := Parent;
    end;
  Result := True;
end;

function TelemetrySecurityDescriptor(var Descriptor: LongWord): Boolean;
var
  Sddl: String;
  Size: LongWord;
begin
  Sddl := 'O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRGX;;;BU)';
#ifdef TelemetryTestSid
  Sddl := 'O:{#TelemetryTestSid}D:P(A;OICI;FA;;;{#TelemetryTestSid})(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRGX;;;BU)';
#endif
  Result := TelemetryConvertDescriptor(Sddl, 1, Descriptor, Size);
end;

function TelemetryCreateProtectedDirectory(Path: String): Boolean;
var
  Descriptor: LongWord;
  Attributes: TTelemetrySecurityAttributes;
begin
  Result := False;
  if not TelemetrySecurityDescriptor(Descriptor) then exit;
  try
    Attributes.Length := 12;
    Attributes.Descriptor := Descriptor;
    Attributes.InheritHandle := 0;
    Result := TelemetryCreateDirectory(Path, Attributes);
  finally
    TelemetryLocalFree(Descriptor);
  end;
end;

function TelemetryEnsureParent(Path: String): Boolean;
var
  Parent: String;
begin
  Result := False;
  if DirExists(Path) then
    begin
      Result := TelemetrySafeParents(Path);
      exit;
    end;
  Parent := ExtractFileDir(Path);
  if (Parent = '') or (Parent = Path) then exit;
  if not TelemetryEnsureParent(Parent) then exit;
  // Create one component, then verify it before any child operation.
  if not TelemetryCreateProtectedDirectory(Path) and not DirExists(Path) then exit;
  Result := TelemetrySafeParents(Path);
end;

function TelemetryPrepareDirectory(): Boolean;
var
  Parent: String;
begin
  Result := False;
  Parent := ExtractFileDir(TelemetryDirectory);
  if not TelemetryEnsureParent(Parent) then exit;
  if not DirExists(TelemetryDirectory) then
    begin
      if not TelemetryCreateProtectedDirectory(TelemetryDirectory) and not DirExists(TelemetryDirectory) then exit;
    end;
  Result := TelemetrySafePath(TelemetryDirectory, True);
end;

function TelemetryFileTicks(Value: TFileTime): Int64;
begin
  Result := Int64(Value.dwHighDateTime) * 4294967296 + Value.dwLowDateTime;
end;

function TelemetryRecordName(Name: String): Boolean;
var
  Index: Integer;
begin
  Result := False;
  if (Length(Name) <> 41) and (Length(Name) <> 40) then exit;
  if (ExtractFileExt(Name) <> '.json') and (ExtractFileExt(Name) <> '.tmp') then exit;
  for Index := 1 to 36 do
    if (Index = 9) or (Index = 14) or (Index = 19) or (Index = 24) then
      begin
        if Name[Index] <> '-' then exit;
      end
    else if Pos(Lowercase(Name[Index]), '0123456789abcdef') = 0 then exit;
  Result := True;
end;

function TelemetryDeleteRecord(Path: String): Boolean;
var
  Error: LongWord;
begin
  Result := TelemetryDeleteFile(Path);
  if not Result then
    begin
      Error := DLLGetLastError;
      Result := (Error = 2) or (Error = 3);
    end;
end;

function TelemetryPrune(): Boolean;
var
  FindRec: TFindRec;
  Now: TTelemetrySystemTime;
  NowFile: TFileTime;
  Count: Integer;
  Total, Oldest, Ticks, Cutoff: Int64;
  OldestName, Path: String;
begin
  Result := False;
  TelemetryGetSystemTime(Now);
  if not TelemetrySystemTimeToFileTime(Now, NowFile) then exit;
  Cutoff := TelemetryFileTicks(NowFile) - 6048000000000;
  repeat
    Count := 0;
    Total := 0;
    Oldest := 9223372036854775807;
    OldestName := '';
    if FindFirst(AddBackslash(TelemetryDirectory) + '*', FindRec) then
      try
        repeat
          if ((FindRec.Attributes and ($10 or $400)) = 0) and TelemetryRecordName(FindRec.Name) then
            begin
              Path := AddBackslash(TelemetryDirectory) + FindRec.Name;
              Ticks := TelemetryFileTicks(FindRec.LastWriteTime);
              if (Ticks < Cutoff) or (FindRec.SizeHigh <> 0) or (FindRec.SizeLow > 4096) then
                begin
                  if not TelemetryDeleteRecord(Path) then
                    begin
                      Log('Installation telemetry: Cannot delete an expired or excessive record.');
                      exit;
                    end;
                end
              else
                begin
                  Count := Count + 1;
                  Total := Total + FindRec.SizeLow;
                  if Ticks < Oldest then
                    begin
                      Oldest := Ticks;
                      OldestName := Path;
                    end;
                end;
            end;
        until not FindNext(FindRec);
      finally
        FindClose(FindRec);
      end;
    if (Count < 256) and (Total <= 1048576 - 4096) then
      begin
        Result := True;
        exit;
      end;
    Log('Installation telemetry: Capacity limit reached. Delete the oldest record.');
    if (OldestName = '') or not TelemetryDeleteRecord(OldestName) then exit;
  until False;
end;

function TelemetryJson(Value: String): String;
var
  Index, Code: Integer;
begin
  Result := '"';
  for Index := 1 to Length(Value) do
    begin
      Code := Ord(Value[Index]);
      if (Value[Index] = '"') or (Value[Index] = '\') then
        Result := Result + '\' + Value[Index]
      else if (Code < 32) or (Code > 126) then
        Result := Result + Format('\u%.4x', [Code])
      else
        Result := Result + Value[Index];
    end;
  Result := Result + '"';
end;

function TelemetrySaveRecord(Path, Json: String): Boolean;
var
  Descriptor, Handle, Written: LongWord;
  Attributes: TTelemetrySecurityAttributes;
  Bytes: AnsiString;
begin
  Result := False;
  if not TelemetrySecurityDescriptor(Descriptor) then exit;
  try
    Attributes.Length := 12;
    Attributes.Descriptor := Descriptor;
    Attributes.InheritHandle := 0;
    Handle := TelemetryCreateFile(Path, $40000000, 5, Attributes, 1, $80, 0);
    if Handle = $FFFFFFFF then exit;
    try
      // Escaped ASCII is also valid UTF-8, independent of the system code page.
      Bytes := AnsiString(Json);
      Result := TelemetryWriteFile(Handle, Bytes, Length(Bytes), Written, 0);
      Result := Result and (Written = Length(Bytes));
    finally
      TelemetryCloseHandle(Handle);
    end;
  finally
    TelemetryLocalFree(Descriptor);
  end;
end;

function TelemetryWrite(EventType, EventId, Outcome: String): Boolean;
var
  Now: TTelemetrySystemTime;
  Json, Stamp, TempPath, Path, OSArch: String;
begin
  Result := False;
  try
    if not TelemetryPrepareDirectory() or not TelemetryPrune() then
      begin
        Log('Installation telemetry: Cannot use the protected record directory.');
        exit;
      end;
    TelemetryGetSystemTime(Now);
    Stamp := Format('%.4d-%.2d-%.2dT%.2d:%.2d:%.2d.%.3dZ', [Now.Year, Now.Month, Now.Day, Now.Hour, Now.Minute, Now.Second, Now.Milliseconds]);
    OSArch := 'x64';
    if IsArm64 then OSArch := 'arm64';
    Json := '{"SchemaVersion":1,"AttemptId":' + TelemetryJson(TelemetryAttemptId) +
      ',"EventId":' + TelemetryJson(EventId) + ',"EventType":' + TelemetryJson(EventType) +
      ',"OccurredUtc":' + TelemetryJson(Stamp) +
      ',"InstallerVersion":' + TelemetryJson('{#GVFSVersion}') +
      ',"PreviousVersion":' + TelemetryJson(TelemetryPreviousVersion) +
      ',"PackageArchitecture":' + TelemetryJson('{#TelemetryPackageArchitecture}') +
      ',"OSArchitecture":' + TelemetryJson(OSArch) +
      ',"Mode":' + TelemetryJson(TelemetryMode) + ',"Outcome":' + TelemetryJson(Outcome) +
      ',"LastPhase":' + TelemetryJson(TelemetryPhase) +
      ',"RemountOutcome":' + TelemetryJson(TelemetryRemount) +
      ',"ScriptResult":' + IntToStr(TelemetryScriptResult) + '}';
    if Length(Json) * 4 > 4096 then
      begin
        Log('Installation telemetry: Record exceeds the size limit.');
        exit;
      end;
    TempPath := AddBackslash(TelemetryDirectory) + EventId + '.tmp';
    Path := AddBackslash(TelemetryDirectory) + EventId + '.json';
    if FileExists(TempPath) or FileExists(Path) then
      begin
        Log('Installation telemetry: Record identifier already exists.');
        exit;
      end;
    if not TelemetrySaveRecord(TempPath, Json) or not RenameFile(TempPath, Path) then
      begin
        Log('Installation telemetry: Cannot publish the record.');
        exit;
      end;
    Result := True;
  except
    Log('Installation telemetry: ' + GetExceptionMessage);
  end;
end;

procedure TelemetryBegin(Directory, InstalledFile: String);
begin
  if TelemetryStarted then exit;
  try
    TelemetryDirectory := Directory;
    TelemetryAttemptId := TelemetryGuid();
    TelemetryStartId := TelemetryGuid();
    TelemetryFinishId := TelemetryGuid();
    TelemetryPreviousVersion := 'Unknown';
    TelemetryMode := 'Fresh';
    if FileExists(InstalledFile) then
      begin
        TelemetryMode := 'Immediate';
        GetVersionNumbersString(InstalledFile, TelemetryPreviousVersion);
        if TelemetryPreviousVersion = '' then TelemetryPreviousVersion := 'Unknown';
      end;
    TelemetryPhase := 'Prepare';
    TelemetryOriginalMode := TelemetryMode;
    TelemetryOutcome := 'Incomplete';
    TelemetryRemount := 'NotRequested';
    TelemetryScriptResult := 0;
    TelemetryStarted := True;
    TelemetryWrite('InstallationStarted', TelemetryStartId, 'Started');
  except
    Log('Installation telemetry: ' + GetExceptionMessage);
  end;
end;

procedure TelemetryFinish(Outcome: String);
begin
  if TelemetryStarted and not TelemetryFinished then
    TelemetryFinished := TelemetryWrite('InstallationFinished', TelemetryFinishId, Outcome);
end;

procedure TelemetryDone();
begin
  TelemetryPhase := 'Done';
  TelemetryOutcome := 'Completed';
  if TelemetryMode = 'Staged' then TelemetryOutcome := 'Staged';
  TelemetryFinish(TelemetryOutcome);
end;
