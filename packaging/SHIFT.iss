#ifndef AppVersion
  #define AppVersion "1.0.0-rc.6"
#endif
#ifndef SourceDir
  #error SourceDir must point to the dotnet publish directory
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif
#ifndef NumericVersion
  #define NumericVersion "1.0.0.0"
#endif
[Setup]
AppId={{E4FAF163-A081-42F6-9974-0729E1397AA3}
AppName=SHIFT
AppVersion={#AppVersion}
VersionInfoVersion={#NumericVersion}
AppPublisher=SHIFT contributors
DefaultDirName={localappdata}\Programs\SHIFT
DefaultGroupName=SHIFT
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=SHIFT-{#AppVersion}-win-x64-setup
SetupIconFile=..\src\Shift.App\Assets\shift.ico
UninstallDisplayIcon={app}\SHIFT.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupMutex=SHIFTSetup
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autoprograms}\SHIFT"; Filename: "{app}\SHIFT.exe"
Name: "{autodesktop}\SHIFT"; Filename: "{app}\SHIFT.exe"; Tasks: desktopicon
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "SHIFT"; Flags: uninsdeletevalue
[Run]
Filename: "{app}\SHIFT.exe"; Description: "{cm:LaunchProgram,SHIFT}"; Flags: nowait postinstall skipifsilent
; User data under LocalAppData\SHIFT is deliberately retained on uninstall.
