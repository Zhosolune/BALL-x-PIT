# 操作记录

## 2026-10-09 15:54 P1 离线决策核心完成

- 操作类型：新增、修改
- 影响文件：
  - `E:\myWorkspace\BALL x PIT\.gitignore`
  - `E:\myWorkspace\BALL x PIT\global.json`
  - `E:\myWorkspace\BALL x PIT\NuGet.Config`
  - `E:\myWorkspace\BALL x PIT\src\DecisionCore\DecisionCore.csproj`
  - `E:\myWorkspace\BALL x PIT\src\DecisionCore\Models.cs`
  - `E:\myWorkspace\BALL x PIT\src\DecisionCore\SampleReader.cs`
  - `E:\myWorkspace\BALL x PIT\src\DecisionCore\DecisionEngine.cs`
  - `E:\myWorkspace\BALL x PIT\tests\DecisionCore.Checks\DecisionCore.Checks.csproj`
  - `E:\myWorkspace\BALL x PIT\tests\DecisionCore.Checks\Program.cs`
  - `E:\myWorkspace\BALL x PIT\scripts\run-p1.ps1`
  - `E:\myWorkspace\BALL x PIT\docs\p1-core.md`
  - `E:\myWorkspace\BALL x PIT\docs\p1-demo.json`
  - `E:\myWorkspace\BALL x PIT\docs\p0-rules.md`
  - `E:\myWorkspace\BALL x PIT\docs\development-plan.md`
  - `E:\myWorkspace\BALL x PIT\docs\operateLog.md`
  - `E:\myWorkspace\BALL x PIT\.tools\dotnet`（工作区 SDK 安装目录，不纳入版本控制）
- 变更摘要：完成纯数据操作模拟、条件路线规划、人工校准评分、可解释推荐和局内结果隔离。
- 原因：把 P0 数据与验收目录转为可执行核心，为 P2 游戏适配准备稳定边界。
- 测试状态：已测试（离线）；真实游戏规则、UI、加载器兼容和目标机器性能待测试。
- 已完成：
  - 安装官方 .NET SDK 8.0.425 到工作区，版本锁定；没有修改系统 PATH。
  - 独立核心库与场景程序，无 Unity/第三方包依赖；中文函数说明与可空类型保留未知语义。
  - 获取/升级/进化合法性及状态转换；融合未核验时返回未评估，不模拟虚假结果。
  - 数量、等级、消耗、独立槽位、替代配方、多步缺失条件、有限搜索与状态循环检查。
  - 人工校准分项、当前与未来收益比较、稳定并列、未知候选范围与快照版本匹配。
  - SDK 运行脚本、P1 说明、人工 JSON 演示；计划标记 P0/P1 完成，其余未开始。
- 验证证据：
  - 工作区 dotnet --version：8.0.425。
  - `./scripts/run-p1.ps1`：最终 Release 构建零警告/零错误；S01–S20 与 B01–B07，27 通过、0 失败。
  - `node scripts/validate-p0.mjs`：继续通过。
  - `dotnet run --project tests/DecisionCore.Checks/DecisionCore.Checks.csproj -c Release --no-build -- --demo`：成功，输出 JSON 可解析，三个人工候选均有分数，首选 direct，接近提示为 true。
- 验证过程中修正：S11 初始误取独立分支，已限定所需路径；循环判断改为按装备状态而非禁止同配方重做，B07 验证重复制作独立材料合法，闭环断言对应调整。最终检查以 27/0 为准。
- 重要边界：社区收益缺失时不输出精确分数；人工规则不进入生产；S20 仅验证数据版本，不证明事件 Hook；本阶段没有游戏安装、运行或插件加载证据。
- 待完成：P2 目标版本类型、角色与等级读取、全部候选/刷新事件、真实配方条件和融合组成验证；P3/P4 UI 与多界面接入；P5 正式策略/性能校准；P6 发布验证。
- 下一步：需要实际游戏测试环境和目标版本 interop 来推进 P2；无需继续安装其他 SDK 或重复请求 P1 权限。

## 2026-10-09 15:39 P1 离线决策核心开始

- 操作类型：新增、修改
- 影响文件：工作区 .tools、src、tests、docs 及构建配置（完成时补充完整清单）。
- 变更摘要：开始准备工作区内 .NET 8 SDK，建立纯数据 C# 决策核心和可执行场景测试。
- 原因：用户授权进入 P1，需将 P0 数据和场景转为实际操作模拟、路线规划、评分与推荐。
- 测试状态：待测试。
- 已完成：读取 P0 约定；dotnet --info 确认只有 .NET 8 运行时，没有 SDK。
- 计划：SDK 仅安装到本工作区 .tools/dotnet，不修改系统 PATH；实现不可变快照、操作合法性、材料消耗、有限规划、分项评分、推荐和结果版本检查；执行 P0 二十个场景及必要边界测试。
- 待完成：SDK 安装验证、代码实现与编译、测试、离线输出示例、阶段记录。
- 范围：尚不接入游戏；社区未知数值仍不计算真实分数；人工数值只用于离线测试。

## 2026-10-09 15:21 P0 规则准备完成

- 操作类型：新增、修改
- 影响文件：
  - `E:\myWorkspace\BALL x PIT\data\p0-sample.json`
  - `E:\myWorkspace\BALL x PIT\data\p0-scenarios.json`
  - `E:\myWorkspace\BALL x PIT\scripts\validate-p0.mjs`
  - `E:\myWorkspace\BALL x PIT\docs\p0-rules.md`
  - `E:\myWorkspace\BALL x PIT\docs\development-plan.md`
  - `E:\myWorkspace\BALL x PIT\docs\operateLog.md`
- 变更摘要：完成可追溯的规则样本、数据类型约定、二十个验收场景和无第三方依赖的校验器。
- 原因：为 P1 离线决策核心提供最小输入，区分社区事实线索、人工算法场景与未知机制。
- 测试状态：已测试（结构、引用、校验器拒绝行为）；推荐算法和真实游戏机制待测试。
- 已完成：
  - 两个角色；十八个物品（十三个社区物品、五个人工物品）；六个配方（三个社区、三个人工）；两条协同关系。
  - 五份基础快照、二十个验收场景；覆盖角色、被动、混合候选、升级、数量、替代配方、多步消耗、循环、融合未知、满槽、并列与过期结果。
  - 记录固定来源提交、使用声明、类型/空值语义、评价状态、评分口径和未知项；社区等级收益、游戏 Slug 和融合机制未填猜测值。
  - 开发计划标记 P0 完成，其余阶段未开始。
- 验证证据：
  - `node scripts/validate-p0.mjs`：通过，报告 2/18/6/5/20 的数据数量。
  - `node --check scripts/validate-p0.mjs`：通过。
  - 通过内存克隆样本做十二项拒绝检查：重复物品、材料引用缺失、数量零、人工/社区混用、未验证条件擅填、真实收益擅填、来源缺失、融合默认值、版本不一致、候选输入重复、候选物品缺失、场景引用缺失。
  - 交付文件存在性检查通过。
- 边界：二十个场景是 P1 验收目录，不是二十项算法测试通过；没有插件编译或游戏运行证据。
- 待完成：P1 的 C# 核心、操作模拟、路线规划、评分和场景测试；P2 的真实接口与规则核验。
- 环境：`dotnet --list-sdks` 两次未返回已安装 SDK，P1 编译前需准备 SDK；P0 Node 校验不受影响。
- 下一步：进入 P1 时先确认 .NET SDK，再实现操作合法性和状态转换；不能直接把 P0 人工规则加载到正式游戏插件。

## 2026-10-09 15:13 P0 规则准备开始

- 操作类型：新增、修改
- 影响文件：
  - `E:\myWorkspace\BALL x PIT\data\p0-sample.json`
  - `E:\myWorkspace\BALL x PIT\data\p0-scenarios.json`
  - `E:\myWorkspace\BALL x PIT\docs\p0-rules.md`
  - `E:\myWorkspace\BALL x PIT\scripts\validate-p0.mjs`
  - `E:\myWorkspace\BALL x PIT\docs\development-plan.md`
  - `E:\myWorkspace\BALL x PIT\docs\operateLog.md`
- 变更摘要：开始 P0，准备可追溯的规则样本、类型约定和离线验收场景。
- 原因：为 P1 决策核心提供输入，避免将未知游戏机制写成已验证规则。
- 测试状态：待测试（数据结构和引用检查）；游戏运行尚未验证。
- 已完成：恢复开发计划；重新读取固定提交的社区样本数据；确认 Node 可用，dotnet --list-sdks 未返回 SDK。
- 待完成：编写样本与数据约定、场景清单、校验脚本；执行有效/损坏样本检查；更新阶段状态。
- 范围：仅 P0，不提前编写评分算法、加载器或 UI；社区规则和人工测试规则分别标记，游戏 ID 映射保持待验证。

## 2026-10-09 15:11 开发计划完成

- 操作类型：新增、修改
- 影响文件：
  - `E:\myWorkspace\BALL x PIT\docs\development-plan.md`
  - `E:\myWorkspace\BALL x PIT\docs\mod-feasibility.md`
  - `E:\myWorkspace\BALL x PIT\docs\operateLog.md`
- 变更摘要：完成 P0–P6 开发计划，覆盖自动局内读取、角色/弹珠/被动协同、候选与路线评分、推荐标记和升级/进化/融合界面。
- 原因：将扩展需求转化为可分阶段实现和验收的工作范围。
- 测试状态：无需测试（文档）；已检查需求覆盖关键词、文档链接目标和阶段状态，未编译或运行插件。
- 已完成：数据职责、操作后状态模拟、有界路线规划、分数语义、过期结果处理、阶段交付、验收矩阵、风险与依赖均已记录；原调研报告增加新计划入口。
- 当前状态：仅完成调研与计划，P0–P6 均未开始；没有安装游戏或新增插件代码。
- 待完成：P0 规则样本与场景准备；P1 离线核心与算法验证；P2 目标游戏接口验证；后续各界面、策略校准和发布验证。
- 下一步：从 P0 开始，不需要先安装本地游戏；P2 及后续实际运行验证需要目标版本 interop 和游戏测试环境。

## 2026-10-09 15:08 开发计划编写中

- 操作类型：新增
- 影响文件：
  - `E:\myWorkspace\BALL x PIT\docs\development-plan.md`
  - `E:\myWorkspace\BALL x PIT\docs\mod-feasibility.md`
  - `E:\myWorkspace\BALL x PIT\docs\operateLog.md`
- 变更摘要：将需求扩展为自动读取局内状态、规划发展路线并覆盖升级、进化、融合的评分推荐助手。
- 原因：用户要求提供可执行的开发计划，明确阶段、依赖和验收条件。
- 测试状态：无需测试（文档编写）。
- 计划：定义完整功能与评分口径；划分离线算法和游戏接入阶段；编写验收矩阵；检查文档一致性。
- 已完成：读取源码调研报告和历史操作记录。
- 待完成：开发计划落盘、更新原调研范围说明、完成文档检查。

## 2026-10-09 14:49

- 操作类型：新增
- 影响文件：
  - `E:\myWorkspace\BALL x PIT\docs\mod-feasibility.md`
  - `E:\myWorkspace\BALL x PIT\docs\operateLog.md`
- 变更摘要：完成弹珠候选项与进化路线预览 Mod 的公开源码调研，记录接入证据、限制和最小实现方案。
- 原因：先验证技术依据，再决定插件开发范围，避免凭猜测编写游戏接口。
- 测试状态：无需测试（仅文档）；已读取公开源码，未编译插件、未运行游戏。
- 已完成：确认 LevelUpUI/LevelUpBtn 候选信息引用、已有装备读取及 IMGUI 叠加示例；检查外部配方结构与许可。
- 待完成：针对目标游戏版本检查 interop 类型，验证完整候选列表、刷新与关闭时机、配方入口、中文字体及面板布局。
- 下一步：可先独立设计与验证配方查询；游戏适配原型需要目标版本的类型资料和实际游戏测试环境。

## 2026-10-09 调研计划与过程

- 操作类型：新增
- 影响文件：`E:\myWorkspace\BALL x PIT\docs\operateLog.md`
- 变更摘要：记录本次源码调研范围及检索过程。
- 原因：工作区初始为空，为后续继续工作保留上下文。
- 测试状态：无需测试。
- 清单：检查工作区和历史记录；检索公开 Mod 仓库；读取候选界面与装备相关源码；检查配方工具数据和许可；形成技术方案。
- 检索限制：Nexus Mods 页面返回 HTTP 403；网页搜索工具返回错误；一个社区开发文档仓库为空。改用 GitHub API 和 raw 源码完成有效调研，未将这些失败检索作为功能不存在的证据。
