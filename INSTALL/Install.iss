#define MyAppName "SolidWorks To URDF"
#define MyAppPublisher "Stephen Brawner / AnDong-cn"
#define MyAppURL "https://github.com/AnDong-cn/solidworks_urdf_exporter"
#define BinaryDir SourcePath + "..\SW2URDF\bin\x64\Release"
#define BuildVersion GetFileVersion(BinaryDir + "\SW2URDF.dll")
#define CommitVersion GetFileProductVersion(BinaryDir + "\SW2URDF.dll")

[Setup]
AppId={{E43E85A9-071D-430A-91B2-84B7AB923170}
AppName={#MyAppName}
AppVersion={#CommitVersion}
VersionInfoVersion={#BuildVersion}
VersionInfoProductName={#MyAppName} with STEP export
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
LicenseFile=..\LICENSE
DefaultDirName={autopf}\SolidWorks Corp\SolidWorks\URDFExporter
OutputDir=..\artifacts\installer
OutputBaseFilename=sw2urdfSetup-step
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\SW2URDF.png

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#BinaryDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.xml,*.lastcodeanalysissucceeded"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
Root: HKLM64; Subkey: "SOFTWARE\SolidWorks\Addins\{{65c9fc17-6a74-45a3-8f84-55185900275d}"; ValueType: dword; ValueName: ""; ValueData: "0"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "SOFTWARE\SolidWorks\Addins\{{65c9fc17-6a74-45a3-8f84-55185900275d}"; ValueType: string; ValueName: "Title"; ValueData: "SolidWorks to URDF Exporter"
Root: HKLM64; Subkey: "SOFTWARE\SolidWorks\Addins\{{65c9fc17-6a74-45a3-8f84-55185900275d}"; ValueType: string; ValueName: "Description"; ValueData: "URDF exporter with per-link STEP export"

[UninstallRun]
Filename: "{win}\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"; Parameters: """{app}\SW2URDF.dll"" /unregister"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterSW2URDF"

[Code]
function SolidWorksIsClosed(): Boolean;
var
  Locator, Service, Processes: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name=''SLDWORKS.exe''');
    Result := Processes.Count = 0;
    if not Result then
      MsgBox('Close SolidWorks before installing or removing the exporter.', mbError, MB_OK);
  except
    MsgBox('Could not check whether SolidWorks is closed. Setup cannot continue.', mbError, MB_OK);
  end;
end;

function InitializeSetup(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
    and (Release >= 528040);
  if not Result then
    MsgBox('.NET Framework 4.8 or later is required.', mbError, MB_OK)
  else
    Result := SolidWorksIsClosed();
end;

function InitializeUninstall(): Boolean;
begin
  Result := SolidWorksIsClosed();
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  CodeBase: String;
begin
  if CurStep = ssPostInstall then
  begin
    if not Exec(ExpandConstant('{win}\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'),
      '"' + ExpandConstant('{app}\SW2URDF.dll') + '" /codebase', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      RaiseException('Could not start the 64-bit .NET COM registration tool.');
    if ResultCode <> 0 then
      RaiseException('COM registration failed with exit code ' + IntToStr(ResultCode) + '.');
    if not RegQueryStringValue(HKLM64,
      'SOFTWARE\Classes\CLSID\{65c9fc17-6a74-45a3-8f84-55185900275d}\InprocServer32', 'CodeBase', CodeBase) then
      RaiseException('The exporter COM registration was not created.');
    Log('Registered exporter: ' + CodeBase);
  end;
end;
