#ifndef PublishDir
  #define PublishDir "..\artifacts\installer-input"
#endif
#ifndef ValidatedSlimInput
  #error "Compile via scripts/build-installer.ps1 using an extracted slim candidate."
#endif
#ifndef AppVersion
  #define AppVersion "0.1.0-beta.1"
#endif

[Setup]
AppId={{8B2B4D7D-4A1A-4A19-A5C5-6F82B5C83A10}
AppName=Vidora
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Vidora
DefaultGroupName=Vidora
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=Vidora-Setup-{#AppVersion}-win-x64
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0.22000
UninstallDisplayIcon={app}\Vidora.exe
LicenseFile={#PublishDir}\LICENSE
InfoAfterFile={#PublishDir}\THIRD-PARTY-NOTICES.md
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "removedata"; Description: "Also remove Vidora local settings and history"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "tools\*,ffmpeg*,ffprobe*,ffplay*,yt-dlp*,yt_dlp*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Vidora"; Filename: "{app}\Vidora.exe"
Name: "{autodesktop}\Vidora"; Filename: "{app}\Vidora.exe"; Tasks: desktopicon

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\Vidora"; Tasks: removedata
