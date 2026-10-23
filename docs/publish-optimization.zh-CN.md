# 发布体积优化

[English](publish-optimization.md) | **简体中文**

保留 C#、WinUI 3、现有图片、单文件压缩及自包含运行环境，不启用 Native AOT。

## 修改

- 移除 `Microsoft.WindowsAppSDK.Runtime` 显式引用，保留 WinUI 组件引用。当前 SDK 在存在 Runtime 包时会解包整套原生运行依赖；移除后走 SDK 自带的按组件部署逻辑，同时生成对应激活清单，无需手动删除 DLL，用户仍不需要另装运行环境。
- 开启完整托管代码裁剪，JSON 设置和语言资源改用源生成元数据。保留旧设置的字段名和默认值，为语言索引器和屏保名称绑定生成属性提供器。
- 每次发布使用独立的编译、中间和暂存目录。按实际单文件打包列表输出依赖清单，并保存构建及发布日志。

此前 input-validation.md 中“组件引用已排除 AI/ML”的描述不完整：虽然托管依赖图没有这些组件，Runtime 包仍会把它们的原生文件带入发布包。

## 实测

| 版本 | EXE 大小 |
| --- | ---: |
| 原版 | 87.4 MiB |
| 仅裁剪托管代码 | 54.9 MiB |
| 按组件部署 + 托管裁剪 | 35.0 MiB |

最终候选版比原版减少约 60%，打包清单共 299 项，不包含 ONNX Runtime、DirectML 和 Widgets。

普通检查程序和实际裁剪后的检查程序均通过 116 项检查。候选界面已验证中文显示、英文即时切换、屏保名称和设备列表，并观察到真实鼠标滚轮输入被接受。

仍存在来自 C#/WinRT 泛型反射回退实现的 IL2081 裁剪警告，未隐藏，也尚不能证明所有路径均无影响。本轮没有重新验证实体手柄、提权/登录自启、Windows 10 或实际自动启动屏保。

## 构建与核查

```powershell
.\scripts\publish.ps1
.\scripts\publish.ps1 -OutputDirectory artifacts\untrimmed-comparison -Untrimmed
```

发布成功后，`artifacts/latest-publish` 保存最新构建日志、发布日志、依赖清单及 `release.json`，本次大型中间目录自动删除。使用 `-KeepBuildArtifacts` 可保留中间文件；失败构建不会自动清理。清单中的源路径仅用于记录构建来源，清理后可能不再存在。清单来自实际打包项目，包含原生组件，其字节数是压缩前源文件大小，包含单文件宿主，不能直接当作 EXE 大小或运行环境安装大小。
