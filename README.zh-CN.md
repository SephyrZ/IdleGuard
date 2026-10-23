<div align="center">
  <img src="src/IdleGuard/Assets/AppIcon.full.png" width="88" alt="IdleGuard 图标">
  <h1>IdleGuard</h1>
  <p>一个按选定设备的活动来计时、自动启动屏保的 Windows 小工具。</p>
  <p>
    <img src="https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4?style=flat" alt="Windows 10 / 11">
    <img src="https://img.shields.io/badge/.NET-10-512BD4?style=flat" alt=".NET 10">
    <img src="https://img.shields.io/badge/UI-WinUI%203-5C2D91?style=flat" alt="WinUI 3">
  </p>
  <p><a href="README.md">English</a> · <strong>简体中文</strong></p>
</div>

自己选择哪些键盘、鼠标和支持的手柄算作活动。IdleGuard 独立计时，选中的设备空闲达到设定时间后，就启动 Windows 系统屏保。

- 自定等待时间，选择能重置计时的设备。
- 关闭窗口后在托盘运行，可选登录自启。
- 在设置中切换中文和英文。

[使用](#使用) · [构建](#构建) · [架构](docs/architecture.md) · [验证记录](docs/input-validation.md)

## 使用

需要 **Windows 10 2004 或更高版本，x64**。运行 `Release/IdleGuard.exe`，无需另装 .NET 或 WinUI 运行环境。

1. 在屏保页选择等待时间，开启监控。
2. 在“监测与设备”中选择哪些键盘、鼠标和支持的手柄能重置计时。
3. 关闭窗口后继续在托盘运行；从托盘菜单退出程序。

首次启动默认请求管理员权限，可在设置中关闭；已有配置中明确关闭的选择会保留。设置还提供中英文切换和登录自启。

应用或驱动请求保持显示开启时，IdleGuard 会延后自动屏保。这项检查需要管理员权限，失败时暂停自动触发。Windows 自己的屏保计时仍独立工作。

配置：`%LOCALAPPDATA%\IdleGuard\settings.json`。移动程序后，请关闭再打开登录自启，以更新路径。

## 构建

需要 **.NET 10 SDK** 和 NuGet 网络访问。重新发布前先退出运行中的发布版。

```powershell
# 开发构建
dotnet build src/IdleGuard/IdleGuard.csproj -c Debug -p:Platform=x64
# 生成本地 Release/ 发布目录
./scripts/publish.ps1
```

发布成功后自动清理临时构建文件，报告保留在 `artifacts/latest-publish`。

目前为原型版本，实体手柄覆盖和登录提权仍需验证。详见[验证记录](docs/input-validation.md)、[架构](docs/architecture.md)及[发布说明](docs/publish-optimization.zh-CN.md)。
