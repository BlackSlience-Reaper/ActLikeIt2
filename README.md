# Act Like It 2

[中文](#中文) | [English](#english)

## 中文

Act Like It 2 是面向《Slay the Spire 2》的自定义 Act 选择框架。它会在进入各幕时收集当前原版 Act 与已注册的自定义 Act，并通过共享 Fork 事件完成单人选择或多人投票；第 4 幕及后续幕还可以在原版胜利结算后继续扩展本局。

当前版本：`v0.1.7+`<br>
最低游戏版本：`0.111.0`

## 功能

- 通过 `ActRegistry.Register` 把普通 Mod 的 `ActModel` 注册到指定幕的 Fork 界面。
- 自动导入 BaseLib 已登记的 `CustomActModel`，运行时无需硬依赖 BaseLib 或 Act Toggler。
- 支持按存档条件控制候选项、候选分组、说明文本、自定义地图和固定 Boss 顺序。
- 支持第 4 幕及后续幕扩展，并保留直接进入 Architect 的胜利选项。
- 已注册的自定义 Act 会接入游戏怪物图鉴的原生发现与显示规则。


## Mod 作者依赖

注册示例均在 [GitHub Wiki](https://github.com/PnLament/ActLikeIt2/wiki/Registering-a-Mod-to-a-Specific-Fork) 中维护。

## English

Act Like It 2 is a custom Act selection framework for Slay the Spire 2. When the game enters an Act, the framework collects the current vanilla Act and every registered custom Act for that slot, then resolves the choice through a shared Fork event for single-player and multiplayer runs. Registered slots 4 and later can extend a run beyond the vanilla ending.

Current version: `v0.1.7+`<br>
Minimum game version: `0.111.0`

### Features

- Registers a normal mod's `ActModel` on a specific Fork screen through `ActRegistry.Register`.
- Imports BaseLib `CustomActModel` registrations without a hard runtime dependency on BaseLib or Act Toggler.
- Supports run-based availability, grouped choices, descriptions, custom maps, and forced boss order.
- Supports Act 4 and later slots while preserving the direct Architect victory choice.
- Adds registered custom Acts to the game's native Bestiary discovery and visibility flow.

### Integration guide

The [English GitHub Wiki guide](https://github.com/PnLament/ActLikeIt2/wiki/Registering-a-Mod-to-a-Specific-Fork-English) covers the project reference, manifest dependency, registration timing, `ActNumber` mapping, and complete examples. 

The [Chinese guide](https://github.com/PnLament/ActLikeIt2/wiki/Registering-a-Mod-to-a-Specific-Fork) contains the same contract.
