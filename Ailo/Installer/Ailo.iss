#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\bin\Release\net10.0\win-x64\publish"
#endif

#ifndef OutputDir
  #define OutputDir "..\..\artifacts"
#endif

#ifndef OutputBaseName
  #define OutputBaseName "ailo-setup"
#endif

#ifndef SetupIcon
  #define SetupIcon "..\Assets\ailo.ico"
#endif

#define MyAppName "Ailo"
#define MyAppPublisher "Ailo"
#define MyAppExeName "Ailo.exe"

[Setup]
AppId={{799FB6B9-ABF5-4394-8301-9522BB0030F0}}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Ailo
DefaultGroupName=Ailo
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseName}
SetupIconFile={#SetupIcon}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Ailo
CloseApplications=yes

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Icons]
Name: "{autoprograms}\Ailo"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Ailo"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Ailo"; Flags: nowait postinstall skipifsilent
