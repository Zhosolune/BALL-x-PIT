# P0：规则样本、数据约定与验收场景

更新时间：2026-10-09。交付范围：离线数据准备，不包含评分器、游戏适配或 UI。

## 1. 文件与使用方式

| 文件 | 职责 |
| --- | --- |
| `data/p0-sample.json` | 两个角色、基础物品、配方、协同关系、未知机制和来源 |
| `data/p0-scenarios.json` | 五份基础快照及二十个验收场景，供 P1 转成可执行测试 |
| `scripts/validate-p0.mjs` | 结构、来源与引用检查，阻止人工规则混入社区规则 |

在项目根目录执行：

```powershell
node scripts/validate-p0.mjs
```

脚本无第三方依赖，不要求 .NET SDK 或游戏安装。校验通过不代表二十个推荐场景已经执行，也不代表游戏规则正确。

## 2. 来源、版本与使用声明

社区来源固定为 [球比伦抉择台 game-data.js](https://github.com/sakiooi1/ballbylon-helper/blob/aa721a35709e069f5f5cbfa3c4da6d6841aa7691/game-data.js)，读取日期 2026-10-09，提交 `aa721a35709e069f5f5cbfa3c4da6d6841aa7691`。来源声明数据对应 The Naturalist Update（2026-08-06）；本项目尚未核对目标游戏版本。

本样本对少量名称、角色机制、配方关系和效果标签进行整理，未导入该仓库代码、强度评价或整份数据。中文名称作为参考译名，不保证与官方译名相同；标签是本项目用于表达规则的简化分类，不是游戏内部字段。

按照来源的 [README](https://github.com/sakiooi1/ballbylon-helper/blob/aa721a35709e069f5f5cbfa3c4da6d6841aa7691/README.md) 和 [NOTICE](https://github.com/sakiooi1/ballbylon-helper/blob/aa721a35709e069f5f5cbfa3c4da6d6841aa7691/NOTICE.md)，这些社区游戏数据仅作非商业学习分享，转载须保留来源与声明，不得用于商业用途，不得声称官方授权。社区代码 MIT 不覆盖 game-data.js。任何正式数据导入需另外遵循该版本完整再分发要求，不能以本摘要替代原声明。

BALL x PIT 的名称和相关游戏内容权利归 Kenny Sun and Friends / Devolver Digital 及相应权利人。本项目为非官方玩家工具，与原权利人无隶属、授权或合作关系。当前样本不包含游戏程序、美术、音频或官方效果文案原文。

### 验证状态

| 值 | 意义 | 使用范围 |
| --- | --- | --- |
| `community_reported` | 固定社区来源中有该关系，尚未对照目标游戏 | 离线参考与待核验输入；不足以证明实际数值或完整规则 |
| `synthetic` | 本项目人工构造 | 只用于算法边界测试，永不进入真实游戏规则库 |
| `pending` | 无可靠规则，暂不赋值 | 禁止补默认值或作确定推断 |

当前没有 `game_verified` 数据。后续增加该状态必须附游戏版本和现场验证证据，升级 schema 与校验器。sample 的 `purpose` 固定为 `offline_preparation_only`，`supportedGameVersion=null`，不具备生产数据资格。

## 3. 样本覆盖

- 角色：战士作为普通机制参考；临时西西弗斯具有直伤限制与范围系数差异。战士的范围系数一是离线归一化基准，不是伤害实测；西西弗斯系数四来自社区描述，适用范围仍待核验。
- 社区物品：十种弹珠、三个被动，覆盖直伤、范围、状态、冻结、射速等语义。
- 社区配方：燃烧+铁→炸弹；炸弹+毒→核弹；冰冻+风或冰冻+闪电→暴雪。只确认来源中记载这些结构，没有确认实际进化等级、消耗、结果等级和融合资格。
- 协同：魔法杖与范围效果、冰刺与冻结来源。协同数值未定义。
- 人工物品：五项，用于非线性等级收益、数量、多材料、循环及被动进化测试。fixture 前缀仅属项目测试命名空间。
- 所有真实物品的 `gameSlug`、`maxLevel` 和 `levelUtility` 保持 null。物品 ID 是本地规则键，不能当作游戏 Slug。
- 融合规则全为 pending；不假定两个弹珠的等级、组成或槽位如何变化。

人工规则的 fixture-combine 消耗两个 A 和一个 B，要求每个实例等级三，结果 C 为等级一；fixture-return 构造图循环；fixture-passive 构造被动数量测试。这些不是游戏真实配方。

## 4. P1 类型约定

后续在一个 C# 项目内实现纯数据类型。此处为设计约定，未创建 .NET 项目或编译类型。集合在传入算法后不可修改；优先不可变对象或进入计算前防御性复制，不把 Unity 对象存进快照。

| 类型 | 字段与类型设计 | 约束 |
| --- | --- | --- |
| `RunSnapshot` | RunId:string、StateVersion:long、ScreenVersion:long、CharacterId:string?、ScreenKind:enum、SlotLimits、Inventory、Candidates、CompletenessIssues | 本局、状态、界面版本同时参与结果有效性检查 |
| `EquipmentInstance` | InstanceId:string、ItemId:string?、Level:int?、Components:组成列表 | 实例 ID 唯一；未知物品和未知等级不转换成默认一级 |
| `FusionComponent` | ItemId:string?、Level:int? | P2 确认层级与信息后扩展，不能把组成当作额外槽位实例 |
| `CandidateAction` | Id:string、Order:int、Action:enum、ItemId:string?、InputInstanceIds:列表、TargetLevel:int?、RecipeId:string?、VariantIndex:int? | Order 与标识分别保留；升级明确目标实例；融合结果允许未知 |
| `CharacterRule` | Id:string、GameSlug:string?、Mechanics、SlotLimits、Evidence | 未知角色产生信息完整度问题，禁止自动使用战士规则 |
| `ItemRule` | Id:string、Kind:enum、Tags、MaxLevel:int?、LevelUtility:列表?、Evidence | 社区标签不代替真实效果；等级效用仅人工样本已赋值 |
| `EvolutionRecipe` | Id:string、ResultItemId:string、Variants:材料列表的列表、Requirements、Evidence | 外层 OR、内层 AND，材料数量用 Quantity:int>0 |
| `RuleEvidence` | SourceId:string?、Verification:enum、GameVersion:string? | unknown 与 synthetic 不能升级成游戏已验证 |
| `ActionEvaluation` | CandidateId:string、Status:enum、RawUtility:double?、DisplayScore:int?、Breakdown、Reasons、Routes | unassessed 的分数为 null，不能用零分代替 |
| `RouteEvaluation` | Steps、MissingMaterials、MissingUpgrades、Status、RawUtility:double?、DisplayScore:int? | 条件未知时 MissingUpgrades 允许 null，不能宣称当前可执行 |
| `AnalysisResult` | RunId、StateVersion、ScreenVersion、RulesVersion、Evaluations、RecommendedCandidateId:string?、RecommendationScope:enum | 结果来源可追溯，任一快照版本变化即作废 |

枚举设计：

- ScreenKind：Upgrade / Evolution / Fusion / Unknown。
- ActionKind：Acquire / Upgrade / Evolve / Fuse / Unknown。
- EvaluationStatus：Assessed / Partial / Unassessed / Illegal。规则不足与非法操作是不同状态。
- RecommendationScope：None / AssessedOnly / AllCandidates。未知角色影响整个局面时不得给出 AllCandidates。
- RouteStatus：Executable / NeedsUpgrades / NeedsMaterials / UnknownRequirements。

P0 JSON 使用小写 snake_case 枚举值。P1 显式映射到 C# 枚举，不依赖大小写自动转换。快照的实际槽位优先于角色默认参考；传零表示无该类槽位，null 才表示未知。

## 5. 评分口径与待校准参数

沿用开发计划的“操作后局面”评价。候选原始效用比较操作前后当前能力和前瞻潜力的增量；路线原始效用比较选完该项后的路线增量，不能把两类分数混在同一个排序中。

P1 拆解立即收益、角色契合、组合协同、发展潜力、机会成本、获取负担。角色倍率作用于对应伤害通道，不能直接把整个候选分数乘四；冻结、射速等作用不能只看标签相等就全部计分。

当前 `weights`、`displayMapping`、`closeThreshold` 为 null：

- 不在 P0 给真实物品填写基础强度或精确等级收益。
- P1 先在独立人工效用场景确定成本单调性、计分不重复、原始排序与显示排序等性质。
- 再以社区规则作解释性参考，明确缺失数值；游戏实测后才逐步校准正式权重。
- 合法性独立于效用：非法选项不进入首选排名；未评估项不是低分项。
- 不把每轮最高项拉到一百分；任何 0–100 映射都需版本化，真实数值在 P1/P5 校准。
- 完全并列时按 Order、CandidateId 依次确定默认首选，同时说明并列；顺序变化允许默认首选变化，但不能改变其原始效用。
- 初始搜索深度三、每层二十个状态属于计算预算，不是游戏规则或已验证性能。

## 6. 场景目录与执行边界

二十个场景覆盖：混合候选、角色差异、被动协同、替代配方、多步条件、已有升级、等级门槛、重复材料、多材料消耗、循环、被动进化、满槽、无已收录路线、未知融合、未知候选、非线性收益、长短期取舍、并列和过期结果。

`snapshots` 是完整基础输入；`scenarios.snapshotId` 引用基础输入，`variation` 描述测试需要执行的变更，`assertions` 是验收预期。P0 不实现通用补丁语言或把文本断言当作已执行测试。P1 逐项将其转成明确的代码构造与断言；P2/P3 再落实生命周期和现场 UI 场景。

S17–S19 涉及人工效用校准，由 P1 补充完全独立的评分输入。当前它们是明确验收要求，没有给真实弹珠安排人为排名。S20 只定义版本匹配要求，未声称已具备游戏 Hook。

校验器只检查 schema 版本、唯一 ID、合法数量、来源、人工/社区分离、引用与场景覆盖。它不证明配方事实，不模拟消耗，不计算路线或推荐。

## 7. P0 通过条件与下一步

- 来源、固定提交、适用口径和使用条件已记录。
- 两种角色差异、弹珠/被动混选、升级、替代配方和未知融合均能表达。
- 真实游戏未知项使用 null/pending，人工测试规则有独立命名空间与状态。
- 数据结构和引用检查通过，损坏数据可被校验器拒绝。
- 二十个验收场景有输入依据和预期；执行状态明确为未执行。

P0 完成时下一阶段为 P1，彼时尚无 .NET SDK。P1 现已准备工作区 SDK 并完成离线核心与场景检查，当前结果见 [P1 交付说明](p1-core.md)。目标插件运行时及加载器版本仍待 P2 确认。
