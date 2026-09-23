; ScreenSnipAlpha installer script — build with Inno Setup (https://jrsoftware.org/isinfo.php)
;
; This expects you've already run the publish command from the ScreenSnipAlpha
; folder (see BUILD_INSTALLER.md), which produces a single self-contained exe at:
;   ScreenSnipAlpha\bin\Release\net9.0-windows10.0.19041.0\win-x64\publish\ScreenSnipAlpha.exe
;
; Compile this file (right-click > Compile, or ISCC.exe installer.iss) to get
; installer_output\ScreenSnipAlphaSetup.exe — a normal Setup Wizard.

#define MyAppName "ScreenSnipAlpha"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ScreenSnipAlpha"
#define MyAppExeName "ScreenSnipAlpha.exe"
#define PublishDir "ScreenSnipAlpha\bin\Release\net9.0-windows10.0.19041.0\win-x64\publish"

[Setup]
; Fixed GUID so future versions upgrade in place instead of installing side-by-side.
AppId={{9C6C6E6E-6B7B-4B7A-9B1E-6D7C6F2E7B3A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Per-user install location above means no admin / UAC prompt needed to install.
PrivilegesRequired=lowest
OutputDir=installer_output
OutputBaseFilename=ScreenSnipAlphaSetup
SetupIconFile=ScreenSnipAlpha\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
DisableWelcomePage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startup"; Description: "&Start ScreenSnipAlpha automatically when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

; Settings.json under %AppData%\ScreenSnipAlpha is intentionally left alone on
; uninstall, same convention as most apps — delete it by hand if you want a
; totally clean slate.
