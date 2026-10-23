# IdleGuard

[English](README.md) | **简体中文**

使用 C# + WinUI 3 开发的 Windows 工具：只根据选中设备的活动计时，空闲达到设定时间后启动系统屏保。

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
