# 弹珠候选项进化路线预览：源码调研

调研日期：2026-10-09。范围：公开源码静态检查，不包含游戏运行或插件编译验证。

需求已扩展为覆盖角色、弹珠、被动以及升级、进化、融合的局内决策助手。本文保留初始源码调研证据；后续开发范围、评分和验收以 [开发计划](development-plan.md) 为准。

## 结论

开发可行性较高。已有源码明确引用选择界面的 `LevelUpUI`、`LevelUpBtn` 和候选 `TgtInfo`，并有读取局内装备、绘制屏幕文字的示例。现在可以规划插件，但不能宣称已验证自动读取全部候选项或目标游戏版本兼容。

建议首个目标为 Windows Steam PC、C#、BepInEx 6 IL2CPP、Harmony。这是社区接入方式，不是已确认的官方 SDK。没有足够证据判定官方接口是否全面存在或不存在。

## 源码证据

### 候选项入口：证据较强，完整列表待验证

仓库：OffTheClockMods/ballxpit-mods。

检查版本：`058e53b2eecd5e97f2c4087580e7f71ffe1c5db7`。

[Plugin.cs 源码](https://github.com/OffTheClockMods/ballxpit-mods/blob/058e53b2eecd5e97f2c4087580e7f71ffe1c5db7/Src/EquipUnlimitedBallsAndItems/Plugin.cs)

- 169–188 行：Harmony 拦截 `LevelUpUI.GetCurUpgradeItem(UpgradeChoice)`；区分 `UpgradeType.kHero` 与 `kPassive`。
- 250–277 行：拦截 `LevelUpUI.HoverBtn`，签名包含 `LevelUpBtn bItem`，读取 `IsNew`、`ChoiceType`、`TgtInfo` 和 `GetUpgradeType()`。
- 367 行：拦截 `InGameUI.OnUpgradesChanged`，可作为已有装备改变后的更新入口候选。
- 源码注释提到 `LevelUpUI.Activate` 和融合界面，但没有提供其完整参数或候选列表字段。

因此，游戏中确实存在候选按钮与类型信息；本次没有找到已核实的“打开界面就枚举全部候选项”的调用。HoverBtn 仅能证明悬停候选信息可读取，不能拿悬停 Hook 代替自动识别全部候选项。下一步检查目标 interop 中 LevelUpUI 的列表字段、按钮初始化方法和 Activate 重载。不要凭空指定尚未查到的方法名。

### 已有弹珠和稳定标识：有参考实现

仓库：jurrm/EncyclopediaHighlight。

检查版本：`6078b296dd1ef604ac5dae3693244176dcfb488d`。

[Plugin.cs 源码](https://github.com/jurrm/EncyclopediaHighlight/blob/6078b296dd1ef604ac5dae3693244176dcfb488d/Plugin.cs)

- BepInEx `BasePlugin` 加载，Harmony 拦截 `GameMgr.SetState`。
- 从 `BattleSaveData.I.Heroes` 读取弹珠，使用 `GetBaseInfo().Slug`；示例跳过 `IsCombo()` 为真的融合球。
- 从 `BattleSaveData.I.Passives` 读取被动，使用 `UpgradeInst<PassiveInfo>.GetInfo().Slug`。
- 修改 `EncyclopediaItem.Init`、`SetSelected`，设置图鉴选中状态与排列。

已有装备读取有源码支持，但融合球如何展开、等级如何读取仍需进一步检查。不能照搬跳过融合球的逻辑，否则路线材料判断可能遗漏。

项目文件以 `net6.0` 为目标，引用 `BepInEx.Unity.IL2CPP` 的 `6.0.0-be.*` 和游戏目录 `BepInEx/interop/*.dll`。后续我们应锁定实际可用版本，不能将该浮动配置视为可复现构建。仓库根目录未见许可证，先研究接口，不复制其代码。

### 游戏内面板：有叠加绘制示例，原生面板未验证

[OtcNotice.cs 源码](https://github.com/OffTheClockMods/ballxpit-mods/blob/058e53b2eecd5e97f2c4087580e7f71ffe1c5db7/Src/otc_bepinex/src/OtcNotice.cs)

通过宿主 OnGUI 调用 `GUI.Box`、`GUI.Label`，按 `Screen.width/height` 定位屏幕提示。可用于最小文字原型，不能等同于已验证 Unity Canvas/TextMeshPro 自定义面板。源码也处理 IMGUI 模块不可用时禁用显示的情况。

最小原型可以先采用只绘制文字和背景的 IMGUI。正式面板优先研究当前游戏 Canvas 与字体资源；中文显示、缩放和遮挡须实测。只读面板不设置全屏输入拦截，展开操作限定在面板范围内。

### 配方数据：有外部数据结构，游戏内部入口未知

仓库：sakiooi1/ballbylon-helper。

检查版本：`aa721a35709e069f5f5cbfa3c4da6d6841aa7691`。

[数据源码](https://github.com/sakiooi1/ballbylon-helper/blob/aa721a35709e069f5f5cbfa3c4da6d6841aa7691/game-data.js) · [算法源码](https://github.com/sakiooi1/ballbylon-helper/blob/aa721a35709e069f5f5cbfa3c4da6d6841aa7691/engine.js) · [说明与许可](https://github.com/sakiooi1/ballbylon-helper/blob/aa721a35709e069f5f5cbfa3c4da6d6841aa7691/README.md)

- 配方对象具有 `id` 与 `options`；options 外层表示替代配方，内层表示同一配方的全部材料。
- 算法遍历材料，区分缺少物品与尚未达到等级要求；这是可借鉴的数据表达。
- README 声称数据对应 2026-08-06 更新，本次没有对照游戏核实完整性和规则。
- 工具自定义 ID 与游戏 Slug 的对应关系没有验证，不能直接当作相同标识。
- 代码标注 MIT；`game-data.js` 明确不在代码 MIT 授权范围内，要求非商业使用并保留来源和声明。当前没有导入或复制其配方。

优先检查目标游戏类型是否公开可枚举的进化配方。无法稳定读取时才采用本地配方数据，并记录游戏版本、来源、授权和 ID 映射。当前没有找到已核实的内部 Recipe API，不应编造接口。

## 最小设计

保持一个插件项目，先不搭建多层框架。逻辑职责为：读取选择界面快照、计算配方路线、绘制面板。

1. 界面候选初始化完成后读取所有候选的顺序、类别和标识，过滤非弹珠候选；不假定永远有三个候选。
2. 从本局状态读取已有弹珠与等级，未知或融合物品明确标记，避免错误宣称材料齐全。
3. 将进化配方表示成“材料集合 → 结果”的超边；允许一个结果有多套配方，不简化为普通单边图。
4. 对每个候选检索参与的配方，再递归检索结果的后续配方；保留每一步的其他材料，以路径访问集合防止循环。
5. 将“未来可达”与“当前能合成”分开；不能把所有分支的材料同时视为拥有，也不能忽略材料消耗、等级及融合球规则。
6. 默认展开一步进化，后续路线可折叠；避免完整分支同时填满右上角。等级和已有材料提示只有规则验证后才启用。
7. 在候选重选、放逐、装备变化时重新计算，在选择界面关闭或本局结束时隐藏并清空；具体生命周期 Hook 待目标类型验证。
8. 数据缺失时显示“未识别候选项”或“配方版本未验证”，不伪造路线。路线计算放在数据变化时执行，不在每次 OnGUI 绘制时遍历。

## 后续验证顺序与通过条件

- 接口原型：日志输出全部候选，顺序和内容与画面一致；覆盖首次打开、重选、放逐、连续升级、新球和已有球升级。
- 状态读取：覆盖基础球、进化球、融合球、被动混选与读档；同一局内数据不串到下一局。
- 配方核验：覆盖单步、多步、替代材料、多个材料、重复材料及循环防护；已持有不等于等级已满足。
- 显示原型：打开显示、刷新同步、关闭隐藏，中文可读，窄屏/高 DPI/窗口缩放下不遮挡候选与确认区域。
- 运行核验：异常不得阻止原生选择，插件卸载后不留下额外游戏数据修改；不使用改数值 Mod 作为正式插件依赖。

没有本地游戏时，可以完成独立配方算法及样例数据验证。要编译针对真实游戏类型的插件，需要目标版本生成的 interop 引用；要确认可运行，需要真实游戏测试环境，未必必须在用户本机。

## 对前序说法的修正

- `devopsdinosaur/ballxpit-mods/autopickup` 实现拾取经验和金币，不是自动选择升级，不能作为候选入口证据。
- 本次 Nexus 页面访问被拒绝，未确认 Auto Pick Upgrades 的实现或版本。
- 未发现完整同类插件只是此前检索结论，不能据此证明不存在。本文仅评价开发路径，不作市场上绝无同类功能的断言。
- 可行性从“推测存在候选接口”提升为“源码有候选按钮信息入口”，但全部候选自动读取、配方和生命周期仍须实际验证。
