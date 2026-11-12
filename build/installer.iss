#define MyAppName "DayTodo 日清单"
#define MyAppVersion "1.0.2"
#define MyAppExeName "DayTodo.exe"
#define MyAppPublisher "DayTodo"

[Setup]
AppId={{7E4A9C31-2D58-4B6F-9A0E-3C7D5E1F8B24}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={localappdata}\DayTodo
DefaultGroupName=DayTodo 日清单
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist\installer
OutputBaseFilename=DayTodo-Setup-1.0.2
SetupIconFile=..\src\DayTodo\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0
ShowLanguageDialog=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel1=欢迎使用 [name/ver] 安装向导
WelcomeLabel2=这个向导会把 [name/ver] 安装到你的电脑中，仅当前用户使用，不需要管理员权限。%n%n建议先关闭其他程序再继续。
SelectDirLabel3=安装程序会把 [name] 安装到下面的文件夹。点击"下一步"继续；如果要换个位置，点击"浏览"。
SelectDirBrowseLabel=选择完成后点击"确定"继续。
SelectStartMenuFolderLabel3=安装程序会在下面创建开始菜单快捷方式。
ReadyLabel1=安装程序已经准备好，可以开始把 [name] 安装到你的电脑中了。
ReadyLabel2a=点击"安装"继续；如果想修改任何设置，点击"上一步"。
InstallingLabel=正在安装 [name]，请稍候……
FinishedHeadingLabel=[name] 安装完成
FinishedLabel=安装程序已经把 [name] 装好了。%n%n托盘图标常驻，双击可以打开主窗口。
ButtonInstall=立即安装(&I)
ButtonFinish=完成(&F)
ConfirmUninstall=确定要完全卸载 [name] 以及它的组件吗？%n%n注意：你的任务数据保存在 %APPDATA%\DayTodo，卸载不会被删除。
ClickNext=点击"下一步"继续，或点击"取消"退出安装程序。

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项:"; Flags: unchecked
Name: "autostart"; Description: "开机自动启动（启动后最小化到托盘）"; GroupDescription: "附加选项:"

[Files]
Source: "..\dist\portable\DayTodo.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\DayTodo\Assets\app.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; DestName: "使用说明.md"; Flags: ignoreversion

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DayTodo"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; Flags: uninsdeletevalue; Tasks: autostart

[Icons]
Name: "{group}\DayTodo 日清单"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 DayTodo"; Filename: "{uninstallexe}"
Name: "{autodesktop}\DayTodo 日清单"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即启动 DayTodo"; Flags: nowait postinstall skipifsilent

; User data in %APPDATA%\DayTodo is intentionally kept after uninstall.
; CloseApplications=yes lets the Restart Manager close a running DayTodo.exe.
