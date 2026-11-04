#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef FfmpegDir
  #define FfmpegDir "..\artifacts\approved-ffmpeg"
#endif
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef FfmpegSha256
  #define FfmpegSha256 "UNAPPROVED"
#endif

#if FfmpegSha256 == "UNAPPROVED" || !FileExists(FfmpegDir + "\ffmpeg.exe")
  #error "A reviewed FFmpeg build and its pinned SHA-256 are required. See THIRD-PARTY-NOTICES.md before packaging."
#endif
#if LowerCase(GetSHA256OfFile(FfmpegDir + "\ffmpeg.exe")) != LowerCase(FfmpegSha256)
  #error "The FFmpeg executable does not match the SHA-256 reviewed for this release."
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
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\Vidora.exe
LicenseFile=..\LICENSE
InfoAfterFile=..\THIRD-PARTY-NOTICES.md
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "removedata"; Description: "Also remove Vidora local settings and history"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#FfmpegDir}\ffmpeg.exe"; DestDir: "{app}\tools\ffmpeg"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Vidora"; Filename: "{app}\Vidora.exe"
Name: "{autodesktop}\Vidora"; Filename: "{app}\Vidora.exe"; Tasks: desktopicon

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\Vidora"; Tasks: removedata
