# IdleGuard 架构草案

## 目标与当前状态

C# + WinUI 3 单项目、单进程桌面工具。维护 IdleGuard 自己的空闲计时，用户选择哪些设备活动可以重置计时；默认包含键盘、鼠标和支持的手柄。达到 timeout 后，主动启动 Windows 当前配置的屏保。

timeout 可使用用户自定义值，或读取系统屏保 timeout。同步的是超时数值，不是 Windows 的空闲计时。应用不控制水冷软件、不修改 Windows 的输入计时机制，也不单独实现自动锁屏。

当前已创建可编译的 .NET 10 / WinUI 3 x64 无包应用，采用左侧 NavigationView：屏保主页、监测与设备、左下角关于及设置。支持键鼠枚举、每接口开关、实时计时、手动与超时自动启动屏保。InputMonitor 由 App 持有，通过隐藏窗口接收后台输入和设备变化；锁屏、睡眠期间暂停，恢复时重建基线。SettingsStore 保存语言、timeout 来源/自定义值及监控开关，LocalizationService 使用内嵌中英文 JSON 资源即时更新界面。TrayIcon 使用 Shell_NotifyIcon 和窗口子类化管理通知区域图标；关闭窗口取消销毁并隐藏，控制器和计时器保持运行。StartupService 管理当前用户 Run 项，以 --background 登录后隐藏启动；命名互斥量与注册窗口消息实现单实例唤回。手柄和设备选择持久化尚未实现。

主页使用 IdleController 协调 Disabled / Monitoring / Triggering / Triggered / Error 状态。IdleTracker 的 Cycle 在接受输入、恢复或显式重建基线时递增，已触发周期只在新 Cycle 后重新监控。系统屏保正在运行时消费当前周期，避免重复请求。启动请求期间防重入，检测失败进入 Error，用户显式重试后才恢复。切换页面不停止监控，手动测试入口在全部页面底部保留。首次启动默认暂停自动触发，后续恢复保存的监控开关；语言与时间偏好从本地 JSON 读取。托盘和主页共享控制器，设置页与托盘共享当前用户自启项。固定栏显示当前本地时间和首次检测到屏保运行的名称与日期时间，精确到秒。

“启动屏保”按钮长期保留为用户测试入口，后续接入自动触发时也不移除。

当前输入实现包含 Models/InputDevice、Services/Input 下的 InputMonitor、DeviceCatalog、RawInputSource、MouseActivityFilter、DevicePolicy、IdleTracker，以及 Native 下的 InputMessageWindow、InputNativeMethods。设备路径作为本次运行的规则键，当前句柄仅用于输入路由；相同路径重连保留开关，离线设备保留记录。键盘按下、重复、释放均计为活动；鼠标绝对坐标首报建立基线，未变化的坐标不算活动。界面每 250 ms 刷新摘要，不逐条渲染输入报文。

## 目录与计划文件

```text
prototypes/RawIdleTest.ps1         现有输入检测原型
src/IdleGuard/
  App.xaml / App.xaml.cs          启动、持有服务、退出时清理
  MainWindow.xaml / .xaml.cs      WinUI 窗口外壳
  Views/
    DevicesPage.xaml             设备名单、启用开关、输入活动提示
    SettingsPage.xaml            timeout 来源、自定义值、启动与语言设置
  ViewModels/
    DevicesViewModel.cs          设备列表与界面操作
    SettingsViewModel.cs         设置与界面操作
  Models/
    InputDevice.cs               设备身份、类型、显示名、连接状态
    DeviceRule.cs                每设备的用户覆盖规则
    InputActivity.cs             来源、事件类型、单调时钟时间戳
    AppSettings.cs               设备规则、timeout 模式、自定义值、死区等
  Services/
    Input/
      DeviceCatalog.cs           枚举、热插拔、身份匹配
      RawInputSource.cs          键鼠及后续支持的 HID 输入
      XInputSource.cs            手柄状态轮询、变化与死区判断
      DevicePolicy.cs            根据身份和类型决定是否接受输入
      IdleTracker.cs             只接受有效活动，维护独立空闲计时
    Windows/
      ScreenSaverService.cs      读取系统屏保配置、请求启动屏保
    IdleController.cs            应用 timeout、协调计时与屏保触发状态
    SettingsStore.cs             用户应用数据目录中的 JSON 配置
    TrayIconService.cs           打开窗口、暂停、退出
  Native/
    NativeMethods.cs             Windows API 声明
    InputMessageWindow.cs        独立隐藏窗口，接收输入和设备变化
  Strings/en-US/                 英文资源
  Strings/zh-CN/                 中文资源
  Assets/                        应用图标与图片
```

不提前拆类库或引入数据库。空目录使用 `.gitkeep` 保留，创建实际文件后可以删除占位文件。

## 数据流

输入来源 -> 设备身份匹配 -> 有效活动判断 -> 名单过滤 -> IdleTracker

IdleTracker + 有效 timeout -> IdleController -> ScreenSaverService -> 启动当前系统屏保

设置 -> 自定义 timeout / 读取系统 timeout -> IdleController

界面通过 ViewModel 查看设备名单、IdleGuard 空闲时间、有效 timeout、最近接受的输入来源与触发状态。Windows 系统空闲时间仅作为可选诊断信息，不参与触发判断。后台服务由 App 持有，不能依赖设置页是否打开。后台事件通过 UI Dispatcher 更新界面。

## 设备规则

- 明确的每设备规则优先于设备类别默认值。新键盘、鼠标、支持的手柄默认启用，未知 HID 默认禁用。
- Raw Input 事件的设备句柄用于当前连接路由，不作为持久化身份。保存设备路径及可用的身份信息；重插、蓝牙/有线切换和复合设备需要验证匹配策略。
- XInput 只提供 0–3 的控制器槽位，不能将槽位冒充跨重连稳定的物理身份。第一版可以显示槽位并说明限制；更可靠的身份关联是后续验证项。
- 鼠标仅有实际移动、按键或滚轮变化才算活动；不把所有 WM_INPUT 消息都当成用户操作。
- 手柄需要摇杆/扳机死区，避免漂移和重复上报不断重置计时。有效按键或状态变化才算活动；持续按住的计时语义需要单独确定。
- 防止同一手柄经多个来源重复计数。拔出设备不算活动，保留规则并标记离线。
- 使用单调时钟计时。暂停、系统睡眠及恢复时重新建立监控基线；锁屏和远程会话支持需要验证。

## 计时与触发规则

- 启动或恢复监控时建立单调时钟基线。只有被名单接受的有效输入重置正常监控中的 idle；未选设备输入不改变它。
- timeout 模式为 Custom 或 SystemScreenSaver。Custom 使用用户保存的正数秒值；SystemScreenSaver 读取系统值，不写回系统设置，不覆盖已保存的自定义值。
- 系统同步模式在启用监控、切换模式、收到系统设置变化通知时重新读取，并以低频检查作为补充。界面显示当前实际采用的秒数；读取失败或值无效时显示错误并暂停触发，不静默猜测。
- idle >= timeout 时触发一次。状态从 Monitoring 进入 Triggering，防止重复请求；请求成功后进入 Triggered，等待下次被接受的有效输入重新进入 Monitoring。
- 屏保因未选设备活动退出时，不据此重置独立计时，也不立即循环重启屏保。下一次被接受的输入才开启新的空闲周期。
- 触发失败显示错误并暂停自动触发，用户重试或重新启用后建立新基线。测试按钮显式请求启动屏保，沿用相同的防重入处理。
- 已检测到屏保运行时不重复启动；会话锁定或系统挂起期间暂停触发，解锁或恢复时建立新基线。检测方式在实现时验证。

## 屏保服务与系统边界

ScreenSaverService 负责读取当前系统屏保配置、timeout 和运行状态，并通过 Windows 支持的方式请求启动已配置屏保。具体启动与状态检测方式在小原型中验证；没有配置屏保时显示提示，不把显示器关闭当作启动屏保成功。启动请求返回与屏保实际运行要分别判断。

当前验证实现通过 HKCU `Control Panel\Desktop` 的 `SCRNSAVE.EXE` 读取屏保路径，通过 `SystemParametersInfoW` 读取 timeout、自动屏保开关和运行状态。启动时向窗口投递 `WM_SYSCOMMAND / SC_SCREENSAVE`，后台每 250 ms 检测一次，最多 10 秒；记录检测到运行的结果，退出屏保后仍可查看。发送成功仅表示消息已入队，不保证屏保已启动。运行状态是采样结果，极短的运行可能未被观察到。

设备名单只控制 IdleGuard 在启动屏保前的计时。屏保启动后如何响应输入并退出，以及退出是否要求登录，由屏保和 Windows 设置决定。应用不尝试绕过登录要求，也不单独调用锁屏操作。

Windows 自己的屏保定时继续独立工作，可能先于 IdleGuard 触发；应用不默认禁用或修改它。界面与使用说明需要明确这一点。读取相同的 timeout 不代表两种计时会同步。

不需要通过模拟输入、保持唤醒请求或全局输入拦截来实现独立计时。退出时释放输入注册、轮询、系统通知和托盘资源。

## 实现顺序

1. 用小原型验证读取系统 timeout、主动启动当前屏保与运行状态检测。
2. 创建并运行空白 WinUI 3 项目。
3. 枚举键鼠、显示实时来源并实现设备规则与独立计时。
4. 接入 XInput 手柄，验证死区、持续按键和热插拔。
5. 接入 IdleController 与 ScreenSaverService，实现自定义 / 系统 timeout、一次触发与错误状态。
6. 配置保存、中英资源、托盘和发布验证。

## 首版验收场景

- 启用设备输入重置 IdleGuard 计时，禁用设备输入不重置；正常键鼠使用不被拦截。
- 自定义短 timeout 到达后启动当前屏保，每个空闲周期只发一次请求。
- 系统模式采用读取到的秒数，切回自定义模式恢复用户原先保存的值。
- 屏保未配置、系统 timeout 读取失败或启动失败时有明确状态，不无限重试。
- 屏保被未选设备退出后不立即重启；选中设备输入后重新计时。
- 隐藏设置窗口后继续监控；暂停、锁屏、睡眠与恢复不会意外触发。
- 手柄漂移、热插拔与系统自身屏保提前触发的共存行为经过验证。

## 参考

- [Raw Input](https://learn.microsoft.com/en-us/windows/win32/inputdev/raw-input)
- [GetRawInputDeviceList](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getrawinputdevicelist)
- [XInputGetState](https://learn.microsoft.com/en-us/windows/win32/api/xinput/nf-xinput-xinputgetstate)
- [SystemParametersInfoW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow)
