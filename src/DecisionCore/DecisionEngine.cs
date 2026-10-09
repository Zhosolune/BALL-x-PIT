using System.Collections.Immutable;

namespace BallPit.DecisionCore;

/// <summary>只分析纯数据的离线决策核心；人工校准结果不代表游戏胜率。</summary>
public sealed class DecisionEngine
{
    private readonly Rules _rules;
    private readonly PlannerOptions _options;

    /// <summary>创建有界规划器；非法预算、成本或规则时抛出 ArgumentException。</summary>
    public DecisionEngine(Rules rules, PlannerOptions? options = null)
    {
        _rules = rules;
        _options = options ?? new PlannerOptions();
        if (_options.MaxDepth is < 1 or > 10 || _options.StatesPerDepth is < 1 or > 1000 || _options.MaxExpansions is < 1 or > 100000
            || !double.IsFinite(_options.DisplayScale) || _options.DisplayScale <= 0
            || new[] { _options.MissingItemCost, _options.UpgradeCost, _options.StepCost, _options.PotentialWeight, _options.CloseThreshold }
                .Any(value => !double.IsFinite(value) || value < 0))
            throw new ArgumentException("规划预算、显示比例和成本必须是有限且合法的数值。");
        foreach (var item in rules.Items.Values)
            if (item.LevelUtility is { } levels && (levels.Length == 0 || levels.Any(value => !double.IsFinite(value)))
                || !double.IsFinite(item.DirectShare) || !double.IsFinite(item.AreaShare)
                || item.DirectShare < 0 || item.AreaShare < 0 || item.DirectShare + item.AreaShare > 1)
                throw new ArgumentException($"{item.Id} 的效用或伤害通道无效。");
        if (rules.Characters.Values.Any(character => !double.IsFinite(character.AreaMultiplier) || character.AreaMultiplier <= 0)
            || rules.Synergies.Any(synergy => !rules.Items.ContainsKey(synergy.ProviderItemId)
                || synergy.Weight is { } weight && !double.IsFinite(weight)))
            throw new ArgumentException("角色范围系数或协同规则无效。");
        foreach (var recipe in rules.Recipes)
        {
            if (!rules.Items.ContainsKey(recipe.ResultItemId) || recipe.Variants.IsEmpty
                || recipe.Requirements.MinimumLevel is <= 0 || recipe.Requirements.ResultLevel is <= 0)
                throw new ArgumentException($"{recipe.Id} 配方结果或条件无效。");
            foreach (var variant in recipe.Variants)
                if (variant.IsEmpty || variant.Select(material => material.ItemId).Distinct().Count() != variant.Length
                    || variant.Any(material => material.Quantity is < 1 or > 100 || !rules.Items.ContainsKey(material.ItemId)
                        || rules.Items[material.ItemId].Kind != rules.Items[recipe.ResultItemId].Kind))
                    throw new ArgumentException($"{recipe.Id} 材料无效。");
        }
    }

    /// <summary>模拟一个操作，不修改原快照；未知融合不猜测结果，非法操作返回 Illegal。</summary>
    public Transition Simulate(Snapshot snapshot, Candidate candidate)
    {
        if (snapshot.Inventory.Any(entry => entry.Level is <= 0)
            || snapshot.BallSlots is < 0 || snapshot.PassiveSlots is < 0)
            return Illegal("快照等级和槽位不能为负，已知等级必须为正。");
        if (snapshot.Inventory.Select(entry => entry.InstanceId).Distinct().Count() != snapshot.Inventory.Length
            || candidate.Inputs.Distinct().Count() != candidate.Inputs.Length)
            return Illegal("装备或输入实例重复。");
        if (candidate.Action is ActionKind.Unknown or ActionKind.Fuse)
            return new Transition(EvaluationStatus.Unassessed, null, "未知候选或融合机制，无法模拟结果。");
        if (candidate.ItemId is null || !_rules.Items.TryGetValue(candidate.ItemId, out var item))
            return new Transition(EvaluationStatus.Unassessed, null, "物品未识别。");
        if (candidate.TargetLevel is null)
            return new Transition(EvaluationStatus.Unassessed, null, "目标等级未知。");
        if (candidate.TargetLevel <= 0 || item.MaxLevel is { } maxLevel && candidate.TargetLevel > maxLevel)
            return Illegal("目标等级超出已定义范围。");
        var inputs = snapshot.Inventory.Where(entry => candidate.Inputs.Contains(entry.InstanceId)).ToImmutableArray();
        if (inputs.Length != candidate.Inputs.Length) return Illegal("目标装备实例不存在。");
        var inventory = snapshot.Inventory;
        var status = item.MaxLevel is null ? EvaluationStatus.Partial : EvaluationStatus.Assessed;
        if (candidate.Action == ActionKind.Acquire)
        {
            if (!inputs.IsEmpty || candidate.TargetLevel != 1) return Illegal("获取操作必须为一级且不消耗实例。");
            var capacity = HasSpace(snapshot, item.Kind, 1);
            if (capacity is null) return new Transition(EvaluationStatus.Unassessed, null, "槽位或装备类别未知。");
            if (!capacity.Value) return Illegal("对应槽位已满。");
            if (inventory.Any(entry => entry.InstanceId == $"result:{candidate.Id}")) return Illegal("结果实例标识冲突。");
            inventory = inventory.Add(new Equipment($"result:{candidate.Id}", item.Id, 1, []));
        }
        else if (candidate.Action == ActionKind.Upgrade)
        {
            if (inputs.Length != 1 || inputs[0].ItemId != item.Id) return Illegal("升级目标与物品不一致。");
            if (inputs[0].Level is null || !inputs[0].Components.IsEmpty)
                return new Transition(EvaluationStatus.Unassessed, null, "原等级或融合升级机制未知。");
            if (candidate.TargetLevel <= inputs[0].Level) return Illegal("升级必须提高等级。");
            inventory = inventory.Replace(inputs[0], inputs[0] with { Level = candidate.TargetLevel });
        }
        else if (candidate.Action == ActionKind.Evolve)
        {
            var recipe = _rules.Recipes.FirstOrDefault(value => value.Id == candidate.RecipeId);
            if (recipe is null || recipe.ResultItemId != item.Id || candidate.VariantIndex is not { } variant
                || variant < 0 || variant >= recipe.Variants.Length) return Illegal("进化配方或替代材料索引无效。");
            var required = recipe.Requirements;
            if (!Known(required)) return new Transition(EvaluationStatus.Partial, null, "进化等级、消耗或融合资格尚未验证。");
            if (candidate.TargetLevel != required.ResultLevel) return Illegal("结果等级与配方不一致。");
            if (inputs.Any(entry => !entry.Components.IsEmpty))
                return new Transition(EvaluationStatus.Unassessed, null, "融合组成的配方资格尚未实现。");
            var materials = recipe.Variants[variant];
            if (inputs.Length != materials.Sum(material => material.Quantity)
                || materials.Any(material => inputs.Count(entry => entry.ItemId == material.ItemId) != material.Quantity))
                return Illegal("材料数量或类别不匹配。");
            if (inputs.Any(entry => entry.Level is null)) return new Transition(EvaluationStatus.Unassessed, null, "材料等级未知。");
            if (inputs.Any(entry => entry.Level < required.MinimumLevel)) return Illegal("材料等级不足。");
            if (required.ConsumesInputs == true) inventory = inventory.RemoveRange(inputs);
            var afterConsumption = snapshot with { Inventory = inventory };
            var capacity = HasSpace(afterConsumption, item.Kind, 1);
            if (capacity is null) return new Transition(EvaluationStatus.Unassessed, null, "结果槽位未知。");
            if (!capacity.Value) return Illegal("结果无可用槽位。");
            if (inventory.Any(entry => entry.InstanceId == $"result:{candidate.Id}")) return Illegal("结果实例标识冲突。");
            inventory = inventory.Add(new Equipment($"result:{candidate.Id}", item.Id, candidate.TargetLevel, []));
        }
        return new Transition(status, snapshot with { Inventory = inventory }, "已按快照模拟；社区收益仍待验证。");
    }

    /// <summary>为快照分析候选分数、可解释路线和首选；标识重复时抛出 ArgumentException。</summary>
    public Analysis Analyze(Snapshot snapshot)
    {
        if (snapshot.Candidates.Select(candidate => candidate.Id).Distinct().Count() != snapshot.Candidates.Length
            || snapshot.Candidates.Select(candidate => candidate.Order).Distinct().Count() != snapshot.Candidates.Length)
            throw new ArgumentException("候选标识和画面顺序必须唯一。");
        var before = StateValue(snapshot);
        var beforePotential = Best(Plan(snapshot));
        var evaluations = snapshot.Candidates.Select(candidate =>
        {
            var transition = Simulate(snapshot, candidate);
            var reasons = Explain(snapshot, candidate).Add(transition.Reason);
            if (transition.After is null)
                return new Evaluation(candidate.Id, candidate.Order, transition.Status, null, null, null, reasons, []);
            var after = StateValue(transition.After);
            var routes = Plan(transition.After, candidate.ItemId);
            if (!before.Complete || !after.Complete || transition.Status != EvaluationStatus.Assessed)
                return new Evaluation(candidate.Id, candidate.Order, EvaluationStatus.Partial, null, null, null,
                    reasons.Add("信息不足：缺少已校准收益或已验证机制，不输出精确分数。"), routes);
            var parts = new ScoreParts(after.Base - before.Base, after.Character - before.Character,
                after.Synergy - before.Synergy, _options.PotentialWeight * (Best(Plan(transition.After)) - beforePotential), 0, 0);
            // 材料消耗损失已体现在状态效用差中，不再扣一次机会成本。
            return new Evaluation(candidate.Id, candidate.Order, EvaluationStatus.Assessed, parts.Total, Display(parts.Total), parts,
                reasons.Add($"立即效用增量 {parts.Immediate:F2}；角色 {parts.Character:F2}；协同 {parts.Synergy:F2}；发展潜力增量 {parts.Potential:F2}。"), routes);
        }).ToImmutableArray();
        var ranked = evaluations.Where(value => value.Status == EvaluationStatus.Assessed && value.RawUtility is not null)
            .OrderByDescending(value => value.RawUtility).ThenBy(value => value.Order).ThenBy(value => value.CandidateId, StringComparer.Ordinal).ToArray();
        var scope = ranked.Length == 0 ? RecommendationScope.None
            : evaluations.Any(value => value.Status is EvaluationStatus.Partial or EvaluationStatus.Unassessed)
                ? RecommendationScope.AssessedOnly : RecommendationScope.AllCandidates;
        var close = ranked.Length > 1 && ranked[0].RawUtility - ranked[1].RawUtility <= _options.CloseThreshold;
        return new Analysis(snapshot.RunId, snapshot.StateVersion, snapshot.ScreenVersion, _rules.Version, evaluations,
            ranked.FirstOrDefault()?.CandidateId, scope, close);
    }

    /// <summary>有界规划配方；已知规则模拟完整消耗，未知规则只保留条件结构且不评分。</summary>
    public ImmutableArray<Route> Plan(Snapshot snapshot, string? focusItemId = null)
    {
        var start = StateValue(snapshot);
        var frontier = new List<SearchNode> { new(snapshot, [], [], 0, 0, 0, ImmutableHashSet.Create(StateKey(snapshot))) };
        var routes = new List<Route>();
        var expansions = 0;
        for (var depth = 0; depth < _options.MaxDepth && frontier.Count > 0; depth++)
        {
            var next = new List<SearchNode>();
            foreach (var node in frontier)
            foreach (var recipe in _rules.Recipes.OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                if (!Known(recipe.Requirements)) continue;
                if (focusItemId is not null && node.Steps.IsEmpty && !recipe.Variants.Any(variant => variant.Any(material => material.ItemId == focusItemId))) continue;
                for (var variant = 0; variant < recipe.Variants.Length; variant++)
                {
                    if (++expansions > _options.MaxExpansions) break;
                    var child = Expand(node, recipe, variant);
                    if (child is null) continue;
                    next.Add(child);
                    var final = StateValue(child.State);
                    double? utility = start.Complete && final.Complete
                        ? final.Total - start.Total - Costs(child) : null;
                    var status = child.Missing > 0 ? RouteStatus.NeedsMaterials : child.Upgrades > 0 ? RouteStatus.NeedsUpgrades : RouteStatus.Executable;
                    routes.Add(new Route(child.Steps, status, utility, utility is { } score ? Display(score) : null, child.Upgrades, child.State));
                }
                if (expansions > _options.MaxExpansions) break;
            }
            // 搜索不按路线条数加分；相同终点但材料/路径成本不同的方案保留独立记录。
            frontier = next.OrderByDescending(node => StateValue(node.State).Total - Costs(node))
                .ThenBy(node => string.Join("/", node.Used), StringComparer.Ordinal).Take(_options.StatesPerDepth).ToList();
            if (expansions > _options.MaxExpansions) break;
        }
        routes.AddRange(StructuralRoutes(snapshot, focusItemId));
        return routes.OrderByDescending(route => route.RawUtility).ThenBy(route => route.Steps.Length)
            .ThenBy(route => string.Join("/", route.Steps.Select(step => step.RecipeId)), StringComparer.Ordinal).ToImmutableArray();
    }

    /// <summary>展开已知规则，显式补充条件材料并更新等级、消耗和槽位。</summary>
    private SearchNode? Expand(SearchNode node, Recipe recipe, int variantIndex)
    {
        var requiredLevel = recipe.Requirements.MinimumLevel!.Value;
        var inventory = node.State.Inventory;
        var inputIds = ImmutableArray.CreateBuilder<string>();
        var missing = ImmutableArray.CreateBuilder<Material>();
        var upgrades = 0;
        foreach (var material in recipe.Variants[variantIndex])
        {
            var item = _rules.Items[material.ItemId];
            if (item.MaxLevel is { } max && max < requiredLevel) return null;
            var owned = inventory.Where(entry => entry.ItemId == material.ItemId && entry.Components.IsEmpty)
                .OrderByDescending(entry => entry.Level).ThenBy(entry => entry.InstanceId, StringComparer.Ordinal).Take(material.Quantity).ToArray();
            if (owned.Any(entry => entry.Level is null)) return null;
            foreach (var entry in owned)
            {
                upgrades += Math.Max(0, requiredLevel - entry.Level!.Value);
                inventory = inventory.Replace(entry, entry with { Level = Math.Max(requiredLevel, entry.Level.Value) });
                inputIds.Add(entry.InstanceId);
            }
            var deficit = material.Quantity - owned.Length;
            if (deficit > 0) missing.Add(new Material(item.Id, deficit));
            for (var i = 0; i < deficit; i++)
            {
                if (HasSpace(node.State with { Inventory = inventory }, item.Kind, 1) != true) return null;
                var id = $"planned:{node.Steps.Length}:{recipe.Id}:{variantIndex}:{item.Id}:{i}";
                if (inventory.Any(entry => entry.InstanceId == id)) return null;
                inventory = inventory.Add(new Equipment(id, item.Id, requiredLevel, []));
                inputIds.Add(id);
                upgrades += requiredLevel - 1;
            }
        }
        var candidate = new Candidate($"plan:{node.Steps.Length}:{recipe.Id}:{variantIndex}", 0, ActionKind.Evolve,
            recipe.ResultItemId, inputIds.ToImmutable(), recipe.Requirements.ResultLevel, recipe.Id, variantIndex);
        var transition = Simulate(node.State with { Inventory = inventory }, candidate);
        if (transition.After is null) return null;
        // 同一配方可以为两个独立材料重复制作；仅回到同一装备状态才是循环。
        var stateKey = StateKey(transition.After);
        if (node.Visited.Contains(stateKey)) return null;
        var step = new RouteStep(recipe.Id, recipe.ResultItemId, recipe.Variants[variantIndex], missing.ToImmutable(), upgrades);
        return new SearchNode(transition.After, node.Steps.Add(step), node.Used.Add(recipe.Id),
            node.Missing + missing.Sum(material => material.Quantity), node.Upgrades + upgrades, node.Cost + _options.StepCost,
            node.Visited.Add(stateKey));
    }

    /// <summary>未核验条件只追踪配方结构；缺失材料是下界，不宣称可执行或精确成本。</summary>
    private IEnumerable<Route> StructuralRoutes(Snapshot snapshot, string? focus)
    {
        var routes = new List<Route>();
        var budget = _options.MaxExpansions;
        void Visit(ImmutableArray<RouteStep> path, ImmutableHashSet<string> used, string? previous)
        {
            if (path.Length >= _options.MaxDepth || budget <= 0) return;
            foreach (var recipe in _rules.Recipes.Where(recipe => !Known(recipe.Requirements)).OrderBy(recipe => recipe.Id, StringComparer.Ordinal))
            {
                if (used.Contains(recipe.Id)) continue;
                foreach (var variant in recipe.Variants)
                {
                    if (budget-- <= 0) return;
                    if (previous is not null && !variant.Any(material => material.ItemId == previous)) continue;
                    if (previous is null && !variant.Any(material => snapshot.Inventory.Any(entry => entry.ItemId == material.ItemId))) continue;
                    var missing = variant.Select(material => new Material(material.ItemId, Math.Max(0, material.Quantity
                        - (material.ItemId == previous ? 1 : snapshot.Inventory.Count(entry => entry.ItemId == material.ItemId && entry.Components.IsEmpty)))))
                        .Where(material => material.Quantity > 0).ToImmutableArray();
                    var steps = path.Add(new RouteStep(recipe.Id, recipe.ResultItemId, variant, missing, null));
                    routes.Add(new Route(steps, RouteStatus.UnknownRequirements, null, null, null, null));
                    Visit(steps, used.Add(recipe.Id), recipe.ResultItemId);
                }
            }
        }
        Visit([], ImmutableHashSet<string>.Empty, focus);
        return routes;
    }

    /// <summary>计算基础、角色和协同效用；缺失任一必要数值时不能给出总分。</summary>
    private StateUtility StateValue(Snapshot snapshot)
    {
        var knownCharacter = snapshot.CharacterId is not null && _rules.Characters.TryGetValue(snapshot.CharacterId, out _);
        var character = knownCharacter ? _rules.Characters[snapshot.CharacterId!] : null;
        var complete = character?.Synthetic == true;
        double basic = 0, characterEffect = 0, synergy = 0;
        foreach (var entry in snapshot.Inventory)
        {
            if (entry.ItemId is null || !_rules.Items.TryGetValue(entry.ItemId, out var item) || entry.Level is not { } level
                || level <= 0 || item.LevelUtility is not { } levels || level > levels.Length || !entry.Components.IsEmpty
                || item.Verification != "synthetic") { complete = false; continue; }
            var value = levels[level - 1];
            basic += value;
            if (character is not null)
                characterEffect += value * (item.DirectShare * (character.DirectEnabled ? 0 : -1)
                    + item.AreaShare * (character.AreaMultiplier - 1));
        }
        foreach (var rule in _rules.Synergies)
        {
            if (!snapshot.Inventory.Any(entry => entry.ItemId == rule.ProviderItemId)) continue;
            var matching = snapshot.Inventory.Count(entry => entry.ItemId is not null && _rules.Items.TryGetValue(entry.ItemId, out var item) && item.Tags.Contains(rule.TargetTag));
            if (matching == 0) continue;
            if (rule.Weight is not { } weight) complete = false;
            else synergy += weight * matching;
        }
        return new StateUtility(basic, characterEffect, synergy, complete);
    }

    /// <summary>输出条件机制说明，未知权重不变成数字。</summary>
    private ImmutableArray<string> Explain(Snapshot snapshot, Candidate candidate)
    {
        var reasons = ImmutableArray.CreateBuilder<string>();
        if (snapshot.CharacterId is null || !_rules.Characters.TryGetValue(snapshot.CharacterId, out var character))
            reasons.Add("角色未知，无法确认角色契合度。");
        else if (candidate.ItemId is not null && _rules.Items.TryGetValue(candidate.ItemId, out var item))
        {
            if (item.Tags.Contains("direct_hit") && !character.DirectEnabled) reasons.Add("角色禁用直接命中伤害，直伤通道收益降低。");
            if (item.Tags.Contains("area_damage")) reasons.Add($"角色范围机制参考系数 {character.AreaMultiplier}，仅作用于对应通道。");
        }
        var after = Simulate(snapshot, candidate).After;
        if (after is null) return reasons.ToImmutable();
        foreach (var rule in _rules.Synergies)
            if (after.Inventory.Any(entry => entry.ItemId == rule.ProviderItemId)
                && after.Inventory.Any(entry => entry.ItemId is not null && _rules.Items.TryGetValue(entry.ItemId, out var target) && target.Tags.Contains(rule.TargetTag)))
                reasons.Add($"协同 {rule.Id}：需要持有对应物品和 {rule.TargetTag} 来源；数值以校准规则为准。");
        return reasons.ToImmutable();
    }

    /// <summary>检查实际槽位；未知装备类别或槽位时不猜测空位。</summary>
    private bool? HasSpace(Snapshot snapshot, ItemKind kind, int extra)
    {
        var limit = kind == ItemKind.Ball ? snapshot.BallSlots : snapshot.PassiveSlots;
        if (limit is null || limit < 0 || snapshot.Inventory.Any(entry => entry.ItemId is null || !_rules.Items.ContainsKey(entry.ItemId))) return null;
        return snapshot.Inventory.Count(entry => _rules.Items[entry.ItemId!].Kind == kind) + extra <= limit;
    }
    /// <summary>检查所有进化条件都有明确值。</summary>
    private static bool Known(Requirements requirements) => requirements.MinimumLevel is > 0
        && requirements.ResultLevel is > 0 && requirements.ConsumesInputs is not null && requirements.FusionInputsAllowed is not null;
    /// <summary>固定映射到 0–100，排名仍使用原始效用。</summary>
    private int Display(double score) => (int)Math.Round(50 + 50 * Math.Tanh(score / _options.DisplayScale));
    /// <summary>返回最佳正发展收益，无路线不意味着当前物品效用为零。</summary>
    private static double Best(IEnumerable<Route> routes) => Math.Max(0, routes.Where(route => route.RawUtility.HasValue).Select(route => route.RawUtility!.Value).DefaultIfEmpty(0).Max());
    /// <summary>获取材料与升级成本分别累计，不重复扣执行损失。</summary>
    private double Costs(SearchNode node) => node.Cost + node.Missing * _options.MissingItemCost + node.Upgrades * _options.UpgradeCost;
    /// <summary>构造非法操作结果。</summary>
    private static Transition Illegal(string reason) => new(EvaluationStatus.Illegal, null, reason);
    /// <summary>按装备内容比较状态，不把临时实例标识变化误认为新发展。</summary>
    private static string StateKey(Snapshot snapshot) => System.Text.Json.JsonSerializer.Serialize(snapshot.Inventory
        .Select(entry => new { entry.ItemId, entry.Level, Components = entry.Components.Order(StringComparer.Ordinal).ToArray() })
        .OrderBy(entry => entry.ItemId, StringComparer.Ordinal).ThenBy(entry => entry.Level)
        .ThenBy(entry => string.Join("/", entry.Components), StringComparer.Ordinal));
    /// <summary>不可变搜索节点保留分支成本、路径和已访问状态，避免循环。</summary>
    private sealed record SearchNode(Snapshot State, ImmutableArray<RouteStep> Steps, ImmutableArray<string> Used, int Missing,
        int Upgrades, double Cost, ImmutableHashSet<string> Visited);
    /// <summary>内部状态效用，用分项保留消耗与角色变化。</summary>
    private sealed record StateUtility(double Base, double Character, double Synergy, bool Complete)
    {
        /// <summary>完整状态的效用总值。</summary>
        public double Total => Base + Character + Synergy;
    }
}
