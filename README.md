# Act Like It 2

[中文](#中文) | [English](#english)

## 中文

Act Like It 2 是面向《Slay the Spire 2》的自定义 Act 选择框架。它会在进入各幕时收集当前原版 Act 与已注册的自定义 Act，并通过共享 Fork 事件完成单人选择或多人投票；第 4 幕及后续幕还可以在原版胜利结算后继续扩展本局。

当前版本：`v0.1.7`<br>
最低游戏版本：`0.111.0`

## 功能

- 通过初始化器中的 `ActRegistry.Register<TAct>` 把普通 Mod 的 `ActModel` 注册到指定幕的 Fork 界面，框架会延迟解析 ModelDb。
- 自动导入 BaseLib 已登记的 `CustomActModel`，运行时无需硬依赖 BaseLib 或 Act Toggler。
- 支持按存档条件控制候选项、候选分组、说明文本、自定义地图和固定 Boss 顺序。
- 支持第 4 幕及后续幕扩展，并保留直接进入 Architect 的胜利选项。
- 已注册的自定义 Act 会接入游戏怪物图鉴的原生发现与显示规则。

## 玩家安装

把发布包中的以下文件放入游戏的 `mods/ActLikeIt2` 目录：

```text
ActLikeIt2.dll
ActLikeIt2.pck
ActLikeIt2.json
```

使用本框架的其他 Mod 需要在各自清单中声明 `ActLikeIt2` 依赖，并与本框架一起启用。

## Mod 作者接入

依赖声明、编译引用、注册时机、`ActNumber` 与特定 Fork 界面的映射，以及完整注册示例均在 [GitHub Wiki](https://github.com/PnLament/ActLikeIt2/wiki/Registering-a-Mod-to-a-Specific-Fork) 中维护。

## 本地构建

项目使用 .NET 9 与 Godot 4.5.1 Mono。默认路径可以通过 MSBuild 属性或根目录下不纳入版本控制的 `local.props` 覆盖：

```xml
<Project>
  <PropertyGroup>
    <Sts2Dir>D:\path\to\Slay the Spire 2</Sts2Dir>
    <GodotPath>D:\path\to\Godot.exe</GodotPath>
  </PropertyGroup>
</Project>
```

构建 DLL：

```powershell
dotnet build ActLikeIt2.csproj -c Release
```

## English

Act Like It 2 is a custom Act selection framework for Slay the Spire 2. When the game enters an Act, the framework collects the current vanilla Act and every registered custom Act for that slot, then resolves the choice through a shared Fork event for single-player and multiplayer runs. Registered slots 4 and later can extend a run beyond the vanilla ending.

Current version: `v0.1.7`<br>
Minimum game version: `0.111.0`

### Features

- Registers a normal mod's `ActModel` on a specific Fork screen through initializer-time `ActRegistry.Register<TAct>`, with ModelDb resolution deferred to the framework.
- Imports BaseLib `CustomActModel` registrations without a hard runtime dependency on BaseLib or Act Toggler.
- Supports run-based availability, grouped choices, descriptions, custom maps, and forced boss order.
- Supports Act 4 and later slots while preserving the direct Architect victory choice.
- Adds registered custom Acts to the game's native Bestiary discovery and visibility flow.

### Player installation

Place the following release files in the game's `mods/ActLikeIt2` directory:

```text
ActLikeIt2.dll
ActLikeIt2.pck
ActLikeIt2.json
```

Mods using this framework must declare `ActLikeIt2` in their manifest dependencies and be enabled together with the framework.

### Integration guide

The [English GitHub Wiki guide](https://github.com/PnLament/ActLikeIt2/wiki/Registering-a-Mod-to-a-Specific-Fork-English) covers the project reference, manifest dependency, registration timing, `ActNumber` mapping, and complete examples. The [Chinese guide](https://github.com/PnLament/ActLikeIt2/wiki/Registering-a-Mod-to-a-Specific-Fork) contains the same contract.

### Local build

The project uses .NET 9 and Godot 4.5.1 Mono. Override local paths through MSBuild properties or an untracked `local.props` file in the project root:

```xml
<Project>
  <PropertyGroup>
    <Sts2Dir>D:\path\to\Slay the Spire 2</Sts2Dir>
    <GodotPath>D:\path\to\Godot.exe</GodotPath>
  </PropertyGroup>
</Project>
```

Build the managed assembly:

```powershell
dotnet build ActLikeIt2.csproj -c Release
```
