# Compact publishing

**English** | [简体中文](publish-optimization.zh-CN.md)

The compact profile keeps C# / WinUI 3, both embedded images, a compressed single executable, and self-contained .NET and Windows App SDK deployment. Native AOT is not enabled.

## Dependency selection

Reference `Microsoft.WindowsAppSDK.WinUI` without `Microsoft.WindowsAppSDK.Runtime`. In this SDK version, referencing Runtime selects the entire framework MSIX payload for self-contained deployment, even when the managed package graph excludes AI, ML and Widgets. Without Runtime, the SDK's `AddMicrosoftWindowsAppSDKPayloadFilesFromComponents` target selects the referenced components and creates their activation manifest. No manual deletion of native DLLs is needed. Framework-dependent deployment would require reintroducing Runtime and a different deployment profile.

The earlier statement in input-validation.md that component references alone had excluded AI/ML from the release was incomplete: the Runtime reference still caused these native files to be bundled.

## Trimming

`Compact.pubxml` enables full managed-code trimming and leaves individual linker warnings visible. Settings and language dictionaries use `AppJsonContext` source-generated JSON metadata, preserving existing property names and constructor defaults. Localization indexer binding and screensaver `DisplayMemberPath` binding use generated custom-property providers. `InputDevice` is partial so C#/WinRT can generate its interface exposure code.

The publish step currently reports IL2081 warnings in C#/WinRT generic reflection-fallback implementations. They have not been suppressed or established to be harmless in every execution path. The successful checks below do not prove complete trimming compatibility; monitor these warnings when updating SDK packages.

## Reproduce and audit

```powershell
.\scripts\publish.ps1
.\scripts\publish.ps1 -OutputDirectory artifacts\untrimmed-comparison -Untrimmed
```

Each invocation creates isolated build, intermediate, and staging directories under `artifacts/publish-<id>`. After success, reports (`build.log`, `publish.log`, `dependencies.txt`, `dependencies.csv`, and `release.json`) are saved under `artifacts/latest-publish` and that invocation's large intermediate directory is removed. Use `-KeepBuildArtifacts` to retain it for diagnosis; failed builds remain available. The inventory is captured from the actual `FilesToBundle` items after `PrepareForBundle`, including native payloads; it is not inferred from NuGet packages or an old output directory. Source paths in retained reports describe the build and may no longer exist after cleanup. Source-byte totals are uncompressed and include the bundle host, so they are not final executable size or runtime installation size.

## Measured validation (2026-10-22)

| Executable | Bytes | MiB |
| --- | ---: | ---: |
| Original release | 91,656,869 | 87.4 |
| Full runtime payload, managed trimming | 57,591,624 | 54.9 |
| Component payload, managed trimming | 36,726,655 | 35.0 |

The component candidate inventory contains 299 items and no ONNX Runtime, DirectML or Widgets files. The ordinary and fully trimmed InputChecks executables each passed 116 checks, including existing JSON compatibility, missing optional fields, unknown fields, Unicode device rules, null rejection, native input initialization and read-only screensaver enumeration. The checks disable reflection-based JSON serialization.

The component candidate was opened on the development machine: Chinese home and settings rendered, English switching updated bindings immediately, the screensaver name rendered, and device rows populated with an observed accepted mouse-wheel event. Physical controllers, elevation/sign-in, Windows 10, and actual automatic screensaver triggering were not revalidated in this optimization pass.
