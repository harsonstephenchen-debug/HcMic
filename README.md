# HcMic

把 Android 手机变成电脑的虚拟麦克风。

## 组成
- **pc/** — Windows 客户端（C# WinForms, .NET 8）
  - WiFi(TCP) / USB(adb forward) / 蓝牙(RFCOMM) 三种传输
  - WASAPI 直写虚拟声卡（VB-Cable），未装则回退命名管道
  - 断线自动重连、开机自启、自动连接、记住上次设置
  - installer/ — Inno Setup 安装包脚本（含 VB-Cable 驱动静默安装/卸载、端点隐藏）
- **android/** — 手机端 APK（Kotlin）
  - AudioRecord 采集，48k/44.1k/16k，单/立体声，三档延迟
  - WiFi/USB/蓝牙连接，断线自动重连（5 次退避）
  - 前台服务保活

## 协议
自研帧格式：`[0x44][type][len_lo][len_hi][payload]`，type：HELLO/AUDIO/PING/PONG/BYE/START/STOP。见 `pc/HcMic.Core/Protocol.cs` 与 `android/app/src/main/java/com/hcmic/app/Transport.kt`。

## 构建
PC：`dotnet publish pc/HcMicClient/HcMicClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`
安装包：用 [Inno Setup 6](https://jrsoftware.org/isinfo.php) 编译 `pc/installer/HcMic.iss`
APK：`gradle assembleDebug`（需 Android SDK 34）

## 使用
1. 安装 HcMicSetup.exe（自动装 VB-Cable 驱动并隐藏播放端点）
2. 电脑端点"启动"，手机端选同方式点"开始推流"
3. 系统里出现麦克风 "CABLE Output"，任意软件可选
