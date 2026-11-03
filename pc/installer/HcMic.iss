; HcMic 安装脚本 (Inno Setup 6)
; 安装 HcMicClient（自带 .NET 运行时）+ VB-Cable 虚拟声卡驱动（仅输入/录音部分需要的整包）
; 运行 VBCABLE_Setup_x64.exe 时使用静默参数（VB 官方支持 /S 静默安装）

#define MyAppName "HcMic"
#define MyAppVersion "1.0"
#define MyAppPublisher "HcMic"
#define MyAppExeName "HcMicClient.exe"

[Setup]
AppName={#MyAppName}
AppId={{8A6D8F2C-1B34-4C5D-9E7F-D5BM1C000001}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\HcMic
DefaultGroupName=HcMic
OutputDir=installer
OutputBaseFilename=HcMicSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
; 驱动安装需要管理员权限
PrivilegesRequired=admin
SetupIconFile=icon.ico
UninstallDisplayIcon={app}\HcMicClient.exe
; 中文语言文件为 UTF-8 无 BOM 时需开启
ShowLanguageDialog=no

[Languages]
Name: "chinese"; MessagesFile: "ChineseSimplified.isl"

[Files]
; 主程序（自包含单文件，含 .NET 运行时；支持 hide/show/state 命令行管理端点可见性）
Source: "..\publish\HcMicClient.exe"; DestDir: "{app}"; Flags: ignoreversion
; 无窗口包装器（隐藏 VB 安装器闪现的黑窗）
Source: "wrap.vbs"; DestDir: "{app}"; Flags: ignoreversion
; VB-Cable 驱动安装器（保留在安装目录，卸载时需要用它 -u 卸载驱动）
Source: "driver\VBCABLE_Setup_x64.exe"; DestDir: "{app}\driver"; Flags: ignoreversion

[Icons]
Name: "{group}\HcMic"; Filename: "{app}\HcMicClient.exe"
Name: "{autodesktop}\HcMic"; Filename: "{app}\HcMicClient.exe"; Tasks: desktopicon
; 默认始终创建桌面快捷方式
Name: "{autodesktop}\HcMic"; Filename: "{app}\HcMicClient.exe"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："

[Run]
; 静默安装 VB-Cable 驱动（经 VBS 包装完全无窗口；-i 安装 -h 隐藏）
Filename: "wscript.exe"; Parameters: """{app}\wrap.vbs"" ""{app}\driver\VBCABLE_Setup_x64.exe"" -i -h"; Flags: runhidden waituntilterminated; StatusMsg: "正在安装虚拟声卡驱动 (VB-Cable)…"
; 安装后隐藏 CABLE Input 播放端点（用户只见麦克风 CABLE Output）
Filename: "{app}\HcMicClient.exe"; Parameters: "hide"; Flags: runhidden waituntilterminated; StatusMsg: "正在配置虚拟麦克风…"

[UninstallRun]
; 卸载时恢复端点显示
Filename: "{app}\HcMicClient.exe"; Parameters: "show"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreEndpoint"
; 静默卸载 VB-Cable 驱动（-u 参数，同样经 VBS 包装无窗口）
Filename: "wscript.exe"; Parameters: """{app}\wrap.vbs"" ""{app}\driver\VBCABLE_Setup_x64.exe"" -u -h"; Flags: runhidden waituntilterminated; RunOnceId: "UninstallVBCable"

[Code]
var
  ResultCode: Integer;
  DriverPreexisting: Boolean;

// 安装前记录驱动是否已存在（服务键 + 设备双重检测）
function InitializeSetup(): Boolean;
begin
  DriverPreexisting :=
    RegKeyExists(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Services\VB-Cable') or
    RegKeyExists(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Services\VBAudioVACMME');
  Result := True;
end;

// 安装结束后：仅当本次新装了驱动才提示重启
procedure CurStepChanged(CurStep: TSetupStep);
var
  RestartNow: Boolean;
begin
  if CurStep = ssPostInstall then
  begin
    if DriverPreexisting then
      Exit; // 驱动本来就装过，无需重启提示
    RestartNow := MsgBox(
      '虚拟声卡驱动安装完成。' #13#10 #13#10 +
      '建议重启电脑让麦克风设备完全生效。' #13#10 #13#10 +
      '是否立即重启？', mbInformation, MB_YESNO) = IDYES;
    if RestartNow then
    begin
      // 重启后自动启动 HcMic 客户端
      RegWriteStringValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\RunOnce',
        'HcMicStart', '"' + ExpandConstant('{app}') + '\HcMicClient.exe"');
      Exec(ExpandConstant('{sys}\shutdown.exe'), '/r /t 5 /d p:4:1 /c "HcMic: 虚拟麦克风驱动已安装，系统即将重启"',
        '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;

// 卸载完成后：驱动已移除，提示重启使其彻底卸载
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RestartNow: Boolean;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RestartNow := MsgBox(
      'HcMic 已卸载，虚拟声卡驱动已一并移除。' #13#10 #13#10 +
      '需要重启电脑才能让驱动彻底从系统中消失。' #13#10 #13#10 +
      '是否立即重启？', mbInformation, MB_YESNO) = IDYES;
    if RestartNow then
      Exec(ExpandConstant('{sys}\shutdown.exe'), '/r /t 5 /d p:4:1 /c "HcMic: 虚拟声卡驱动已卸载，系统即将重启"',
        '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

[UninstallDelete]
; 卸载流程（UninstallRun 先执行完驱动卸载）后再清理安装目录
Type: filesandordirs; Name: "{app}"
