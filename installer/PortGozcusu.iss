; Port Gözcüsü — Inno Setup 6 kurulum betiği
; Derleme: build.ps1 tarafından çağrılır.
;   ISCC /DAppVersion=1.0.0 /DPublishDir=..\artifacts\publish installer\PortGozcusu.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

#define AppName "Port Gözcüsü"
#define AppPublisher "Softviser Yazılım"
#define AppURL "https://softviser.com.tr"
#define AppRepo "https://github.com/softviser/port-gozcusu"
#define AppExe "PortGozcusu.exe"

[Setup]
AppId={{6B1E3C2A-9F4D-4E7B-A8C1-5D2F0B7E9A31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppRepo}/issues
AppUpdatesURL={#AppRepo}/releases
AppCopyright=© 2026 {#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Kurulum
DefaultDirName={autopf}\Softviser\Port Gozcusu
DefaultGroupName=Softviser\{#AppName}
DisableProgramGroupPage=yes
; Yönetici yetkisi olmadan kullanıcı bazında kurulur; istenirse tüm kullanıcılar için seçilebilir.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputBaseFilename=PortGozcusu-{#AppVersion}-setup-win-x64
SetupIconFile=..\src\PortGozcusu\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
AppMutex=Softviser.PortGozcusu.SingleInstance
CloseApplications=yes

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Windows açıldığında otomatik başlat (sistem tepsisinde)"; GroupDescription: "Başlangıç:"

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
#ifdef CertFile
Source: "{#CertFile}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\scripts\trust-cert.ps1"; DestDir: "{app}"; Flags: ignoreversion
#endif

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Softviser Yazılım web sitesi"; Filename: "{#AppURL}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "PortGozcusu"; ValueData: """{app}\{#AppExe}"" --minimized"; Tasks: autostart; Flags: uninsdeletevalue
; Görev seçilmese bile kaldırırken değeri temizle (uygulama içinden açılmış olabilir).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "PortGozcusu"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
