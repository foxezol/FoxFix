; FoxFix kurulum paketi (Inno Setup 6). Derlemek icin: tools\paketle.ps1
; Elle: ISCC.exe /DAppVersion=0.7.0 /DSourceDir=..\..\publish-v7 FoxFix.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\publish"
#endif

[Setup]
AppId={{0944F21B-1054-49F1-BC6B-E745424CE09D}
AppName=FoxFix
AppVersion={#AppVersion}
AppPublisher=foxezol
DefaultDirName={autopf}\FoxFix
DefaultGroupName=FoxFix
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\dist
OutputBaseFilename=FoxFixSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\FoxFix.exe
; Kurulum klasorunu kullanici secer
DisableDirPage=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstüne kısayol koy"; Flags: unchecked
Name: "portable"; Description: "Verileri kurulum klasöründe tut (günlük, geri alma kaydı, GoodbyeDPI kopyası). Kaldırınca hepsi birlikte gider."; Flags: unchecked

[Files]
Source: "{#SourceDir}\FoxFix.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\FoxDPI.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "FoxFix.portable"; DestDir: "{app}"; Tasks: portable
Source: "..\..\README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "..\..\src\FoxFix.App\Assets\GoodbyeDpi\LICENSE-goodbyedpi.txt"; DestDir: "{app}\Licenses"; Flags: ignoreversion
Source: "..\..\src\FoxFix.App\Assets\GoodbyeDpi\LICENSE-windivert.txt"; DestDir: "{app}\Licenses"; Flags: ignoreversion
Source: "..\..\src\FoxFix.App\Assets\Zapret\LICENSE-splitwire-turkey.txt"; DestDir: "{app}\Licenses"; Flags: ignoreversion

[Icons]
Name: "{group}\FoxFix"; Filename: "{app}\FoxFix.exe"
Name: "{group}\FoxDPI"; Filename: "{app}\FoxDPI.exe"
Name: "{autodesktop}\FoxFix"; Filename: "{app}\FoxFix.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\FoxFix.exe"; Description: "FoxFix'i aç (FoxDPI.exe için ilk çalıştırma)"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Veri"
