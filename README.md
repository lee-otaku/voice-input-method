# voice-input-method

双端输入法：手机输入，电脑上屏。

- `pc-agent/` — Windows 端（C# WinForms 托盘 + 主窗口）：焦点检测、文本注入、WebSocket 服务
- `android-app/` — Android 端（Kotlin）：大输入框 + 自动/手动发送 + 发送校验
- `installer/PcAgent.iss` — Windows 安装包脚本（Inno Setup 6）
- `tests/wsserver-protocol/` — 协议集成测试（`dotnet run`）

## 使用

1. PC 运行 PcAgent（防火墙放行端口 53818），主窗口展示二维码/配对串。
2. 手机安装 APK，App「设置」→ 粘贴配对串 → 保存并连接。
3. PC 上点开任意输入框，手机输入内容即可发送上屏。

## 构建产物（dist/）

- `KeyboardBridge-0.1.0.apk` — Android 安装包（已签名）
- `PcAgent-0.1.0-win-x64-portable.zip` — Windows 便携版（自包含单文件）
- 正式安装包：Windows 上安装 Inno Setup 6 后执行 `ISCC.exe installer\PcAgent.iss`

## 重新构建

- PC：`dotnet publish pc-agent -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableWindowsTargeting=true -o dist/publish-win64`
- Android：`gradle -p android-app assembleRelease`（输出在 `android-app/app/build/outputs/apk/release/`）
- 签名密钥：`android-app/keystore/release.keystore`（alias=bridge，密码见 `app/build.gradle.kts`，正式发布请更换）
