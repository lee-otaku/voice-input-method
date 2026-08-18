; PC 输入桥接 Inno Setup 安装脚本
; 编译方式（Windows 上安装 Inno Setup 6 后）：
;   ISCC.exe installer\PcAgent.iss
; 产物：installer\Output\PcAgent-0.1.0-Setup.exe

#define MyAppName "PC 输入桥接"
#define MyAppVersion "0.1.0"
#define MyAppExeName "PcAgent.exe"

[Setup]
AppId={{7C2A9E44-1B3D-4F58-8A61-9D0B4E7C5A22}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Keyboard Bridge
DefaultDirName={localappdata}\PcAgent
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=Output
OutputBaseFilename=PcAgent-0.1.0-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes

[Files]
Source: "..\dist\publish-win64\PcAgent.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："
Name: "autostart"; Description: "开机自动启动"; GroupDescription: "附加任务："; Flags: unchecked

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "PcAgent"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 卸载时保留 %APPDATA%\PcAgent 下的配对配置，避免重新安装需重新扫码
Type: files; Name: "{app}\PcAgent.exe"
