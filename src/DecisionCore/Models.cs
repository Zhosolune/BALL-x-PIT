using System.Collections.Immutable;

namespace BallPit.DecisionCore;

/// <summary>候选操作类别；未知项保持原画面位置。</summary>
public enum ActionKind { Acquire, Upgrade, Evolve, Fuse, Unknown }
/// <summary>合法性和信息完整度分开表达。</summary>
public enum EvaluationStatus { Assessed, Partial, Unassessed, Illegal }
/// <summary>首选适用范围，未知项不作为零分项。</summary>
public enum RecommendationScope { None, AssessedOnly, AllCandidates }
/// <summary>路线能否立即执行的状态。</summary>
public enum RouteStatus { Executable, NeedsUpgrades, NeedsMaterials, UnknownRequirements }
/// <summary>物品类别，用于独立槽位计算。</summary>
public enum ItemKind { Ball, Passive }

/// <summary>不可变装备实例；组成不占额外槽位，未知等级保持 null。</summary>
public sealed record Equipment(string InstanceId, string? ItemId, int? Level, ImmutableArray<string> Components);
/// <summary>不可变候选操作，升级与消耗均指向实例标识。</summary>
public sealed record Candidate(string Id, int Order, ActionKind Action, string? ItemId,
    ImmutableArray<string> Inputs, int? TargetLevel, string? RecipeId = null, int? VariantIndex = null);
/// <summary>完整局内快照；槽位来自读取结果而非角色默认值。</summary>
public sealed record Snapshot(string RunId, long StateVersion, long ScreenVersion, string? CharacterId,
    int? BallSlots, int? PassiveSlots, ImmutableArray<Equipment> Inventory, ImmutableArray<Candidate> Candidates);
/// <summary>带来源状态的物品规则；数值通道仅用于明确的离线人工数据。</summary>
public sealed record ItemRule(string Id, ItemKind Kind, ImmutableArray<string> Tags, int? MaxLevel,
    ImmutableArray<double>? LevelUtility, string Verification, double DirectShare = 0, double AreaShare = 0);
/// <summary>角色机制。Synthetic 表示只用于人工算法校准，非真实角色验证。</summary>
public sealed record CharacterRule(string Id, bool DirectEnabled, double AreaMultiplier, bool Synthetic);
/// <summary>配方内材料数量；同一物品合并成一个正整数数量。</summary>
public sealed record Material(string ItemId, int Quantity);
/// <summary>可空的进化条件，未知字段不得转换成默认值。</summary>
public sealed record Requirements(int? MinimumLevel, bool? ConsumesInputs, int? ResultLevel, bool? FusionInputsAllowed);
/// <summary>配方外层为替代方案，内层为同时需要的材料。</summary>
public sealed record Recipe(string Id, string ResultItemId, ImmutableArray<ImmutableArray<Material>> Variants,
    Requirements Requirements, string Verification);
/// <summary>条件协同，权重未知时只输出解释。</summary>
public sealed record Synergy(string Id, string ProviderItemId, string TargetTag, double? Weight);
/// <summary>离线规则集。人工数值不能视为正式游戏评分参数。</summary>
public sealed record Rules(string Version, ImmutableDictionary<string, ItemRule> Items,
    ImmutableDictionary<string, CharacterRule> Characters, ImmutableArray<Recipe> Recipes, ImmutableArray<Synergy> Synergies);
/// <summary>操作模拟结果，不修改输入快照。</summary>
public sealed record Transition(EvaluationStatus Status, Snapshot? After, string Reason);
/// <summary>每个进化步骤保留材料与缺失量，避免丢失多材料关系。</summary>
public sealed record RouteStep(string RecipeId, string ResultItemId, ImmutableArray<Material> Materials,
    ImmutableArray<Material> MissingMaterials, int? MissingUpgrades);
/// <summary>路线评价；未知规则的分数及升级次数均允许为空。</summary>
public sealed record Route(ImmutableArray<RouteStep> Steps, RouteStatus Status, double? RawUtility,
    int? DisplayScore, int? MissingUpgrades, Snapshot? FinalState);
/// <summary>按实际状态增量输出各项贡献，不对整个候选粗略乘角色倍率。</summary>
public sealed record ScoreParts(double Immediate, double Character, double Synergy, double Potential,
    double AcquisitionCost, double OpportunityCost)
{
    /// <summary>各分项总效用；成本以正数存储。</summary>
    public double Total => Immediate + Character + Synergy + Potential - AcquisitionCost - OpportunityCost;
}
/// <summary>候选评价；未评估和非法候选均不进入首选排名。</summary>
public sealed record Evaluation(string CandidateId, int Order, EvaluationStatus Status, double? RawUtility,
    int? DisplayScore, ScoreParts? Parts, ImmutableArray<string> Reasons, ImmutableArray<Route> Routes);
/// <summary>带输入版本和规则版本的分析结果，可在应用到 UI 前检验。</summary>
public sealed record Analysis(string RunId, long StateVersion, long ScreenVersion, string RulesVersion,
    ImmutableArray<Evaluation> Evaluations, string? RecommendedCandidateId, RecommendationScope Scope, bool Close)
{
    /// <summary>检查结果是否仍属于当前局面和规则版本。</summary>
    public bool Matches(Snapshot snapshot, string rulesVersion) => RunId == snapshot.RunId
        && StateVersion == snapshot.StateVersion && ScreenVersion == snapshot.ScreenVersion && RulesVersion == rulesVersion;
}
/// <summary>人工校准参数；固定映射不会把每轮首选强制变成一百分。</summary>
public sealed record PlannerOptions(int MaxDepth = 3, int StatesPerDepth = 20, int MaxExpansions = 1000,
    double MissingItemCost = 8, double UpgradeCost = 2, double StepCost = 1,
    double PotentialWeight = 0.5, double CloseThreshold = 1, double DisplayScale = 5);
