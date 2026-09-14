; Steam MOD 上传工具 - Inno Setup 安装脚本
; 使用：安装 Inno Setup 6 后，用 ISCC.exe 编译本脚本，生成 setup.exe
; 下载：https://jrsoftware.org/isdl.php

#define MyAppName "Steam MOD 上传工具"
#define MyAppExeName "SteamModUploader.exe"

; 版本号默认值；打包脚本会通过 ISCC /DMyAppVersion=x.y.z 传入 csproj 中的版本，
; 这样版本号只需在 SteamModUploader.csproj 里维护一处
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

[Setup]
; 安装程序的唯一标识（GUID）
AppId={{D8C6B1E4-5A2F-4C7B-9E3A-1F6B8D2E4C5A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=SteamModUploader
DefaultDirName={autopf}\SteamModUploader
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
; 输出到 publish 目录
OutputDir=..\publish
OutputBaseFilename=SteamModUploader-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; 安装程序自身的图标
SetupIconFile=..\SteamModUploader\app.ico
; 允许非管理员安装到用户目录（可选）
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "..\publish\win-x64\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent
