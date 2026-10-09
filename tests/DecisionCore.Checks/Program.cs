using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using BallPit.DecisionCore;

/// <summary>无第三方测试依赖的场景检查程序，失败时返回非零退出码。</summary>
internal static class Program
{
    /// <summary>运行 P0 场景与边界检查；--demo 输出仅使用人工数据的推荐示例。</summary>
    private static int Main(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        var rules = SampleReader.ReadRules(Path.Combine(root, "data/p0-sample.json"));
        var snapshots = SampleReader.ReadSnapshots(Path.Combine(root, "data/p0-scenarios.json"));
        var engine = new DecisionEngine(rules);
        var calibrated = FixtureRules(rules);
        var numeric = new DecisionEngine(calibrated, new PlannerOptions(PotentialWeight: 0));
        var mixed = snapshots["mixed"];
        var material = snapshots["material-context"];
        var fixture = Empty() with { Candidates = [Get("direct", 0, "fixture-direct"), Get("area", 1, "fixture-area"), Get("passive", 2, "fixture-passive-a")] };
        if (args.Contains("--demo"))
        {
            var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
            Console.WriteLine(JsonSerializer.Serialize(new { purpose = "人工数值离线演示，非游戏推荐", result = numeric.Analyze(fixture) }, options));
            return 0;
        }
        var failures = new List<string>();
        var passed = 0;
        // 每个场景单独捕获异常，避免首项失败掩盖其他问题。
        void Check(string id, Action test)
        {
            try { test(); passed++; Console.WriteLine($"通过 {id}"); }
            catch (Exception error) { failures.Add($"{id}: {error.Message}"); Console.WriteLine($"失败 {id}: {error.Message}"); }
        }
        Check("S01 混合候选与未知收益", () =>
        {
            var result = engine.Analyze(mixed);
            Equal(4, result.Evaluations.Length);
            Assert(result.Evaluations.Select(value => value.Order).SequenceEqual(new[] { 0, 1, 2, 3 }));
            Assert(result.Evaluations.All(value => value.Status == EvaluationStatus.Partial && value.RawUtility is null));
            Assert(result.RecommendedCandidateId is null);
        });
        Check("S02 角色差异只影响对应通道", () =>
        {
            var ordinary = numeric.Analyze(fixture);
            var special = numeric.Analyze(fixture with { CharacterId = "fixture-sisyphus" });
            Near(0, ordinary.Evaluations[0].Parts!.Character);
            Near(-8, special.Evaluations[0].Parts!.Character);
            Near(24, special.Evaluations[1].Parts!.Character);
            Near(0, special.Evaluations[2].Parts!.Character);
            var community = engine.Analyze(mixed with { CharacterId = "sisyphus" });
            Assert(community.Evaluations[0].Reasons.Any(reason => reason.Contains("直伤通道")));
        });
        Check("S03 范围被动协同的状态条件", () =>
        {
            Assert(engine.Analyze(mixed).Evaluations[1].Reasons.Any(reason => reason.Contains("area-staff")));
            var removed = mixed with { Inventory = mixed.Inventory.RemoveAt(1) };
            Assert(!engine.Analyze(removed).Evaluations[1].Reasons.Any(reason => reason.Contains("area-staff")));
            var ownStaff = fixture with { Inventory = [Stock("s", "fixture-staff", 1)] };
            Near(4, numeric.Analyze(ownStaff).Evaluations[1].Parts!.Synergy);
        });
        Check("S04 冻结协同前置条件", () =>
        {
            var state = snapshots["freeze-context"];
            Assert(engine.Analyze(state).Evaluations[0].Reasons.Any(reason => reason.Contains("freeze-spike")));
            Assert(!engine.Analyze(state with { Inventory = state.Inventory.RemoveAt(0) }).Evaluations[0].Reasons.Any(reason => reason.Contains("freeze-spike")));
        });
        Check("S05 替代配方不能合并材料", () =>
        {
            var routes = engine.Plan(snapshots["freeze-context"]);
            var paths = routes.Where(route => route.Steps.Length == 1 && route.Steps[0].RecipeId == "blizzard-path").ToArray();
            Equal(2, paths.Length);
            Assert(paths.All(route => route.Steps[0].Materials.Length == 2));
            Assert(paths.Any(route => route.Steps[0].Materials.Any(value => value.ItemId == "wind")));
            Assert(paths.Any(route => route.Steps[0].Materials.Any(value => value.ItemId == "lightning")));
        });
        Check("S06 多步结构不冒充可执行路线", () =>
        {
            var after = engine.Simulate(mixed, mixed.Candidates[0]).After!;
            var route = engine.Plan(after, "iron").Single(route => route.Steps.Length == 2 && route.Steps[1].ResultItemId == "nuclear-bomb");
            Assert(route.Steps[1].MissingMaterials.Any(value => value.ItemId == "poison"));
            Equal(RouteStatus.UnknownRequirements, route.Status);
            Assert(route.RawUtility is null && route.FinalState is null && route.MissingUpgrades is null);
        });
        Check("S07 升级保留实例与原快照", () =>
        {
            var after = engine.Simulate(mixed, mixed.Candidates[3]).After!;
            Equal(mixed.Inventory.Length, after.Inventory.Length);
            Equal(3, after.Inventory[0].Level!.Value);
            Equal(2, mixed.Inventory[0].Level!.Value);
            Equal("burn-1", after.Inventory[0].InstanceId);
        });
        Check("S08 等级门槛与升级需求", () =>
        {
            var state = material with { Inventory = material.Inventory.SetItem(1, material.Inventory[1] with { Level = 2 }) };
            Equal(EvaluationStatus.Illegal, engine.Simulate(state, state.Candidates[0]).Status);
            var route = engine.Plan(state).First(route => route.Steps.Length == 1 && route.Steps[0].RecipeId == "fixture-combine");
            Equal(1, route.MissingUpgrades!.Value);
            Equal(RouteStatus.NeedsUpgrades, route.Status);
        });
        Check("S09 数量需要不同实例", () =>
        {
            var state = material with { Inventory = material.Inventory.RemoveAt(1) };
            var choice = state.Candidates[0] with { Inputs = ["a-1", "b-1"] };
            Equal(EvaluationStatus.Illegal, engine.Simulate(state, choice).Status);
            var route = engine.Plan(state).First(route => route.Steps.Length == 1 && route.Steps[0].RecipeId == "fixture-combine");
            Equal(1, route.Steps[0].MissingMaterials.Single().Quantity);
            Equal(EvaluationStatus.Illegal, engine.Simulate(material, choice with { Inputs = ["a-1", "a-1", "b-1"] }).Status);
        });
        Check("S10 多材料消耗与结果等级", () =>
        {
            var result = engine.Simulate(material, material.Candidates[0]);
            Equal(EvaluationStatus.Assessed, result.Status);
            Equal(1, result.After!.Inventory.Length);
            Equal("fixture-c", result.After.Inventory[0].ItemId!);
            Equal(1, result.After.Inventory[0].Level!.Value);
            Equal(3, material.Inventory.Length);
        });
        Check("S11 循环有界且不重复消费", () =>
        {
            var routes = engine.Plan(material);
            Assert(!routes.Any(route => route.Steps.Select(step => step.RecipeId)
                .SequenceEqual(new[] { "fixture-combine", "fixture-return", "fixture-combine" })));
            var onward = routes.First(route => route.Steps.Length == 2 && route.Steps[0].RecipeId == "fixture-combine" && route.Steps[1].RecipeId == "fixture-return");
            Assert(onward.Steps[1].MissingMaterials.Any(value => value.ItemId == "fixture-b"));
            Assert(onward.Steps[1].MissingUpgrades >= 2);
            Assert(routes.All(route => route.Steps.Length <= 3));
        });
        Check("S12 被动进化独立槽位", () =>
        {
            var state = snapshots["passive-context"];
            var after = engine.Simulate(state, state.Candidates[0]).After!;
            Equal(1, after.Inventory.Length);
            Equal("fixture-passive-b", after.Inventory[0].ItemId!);
            Equal(state.BallSlots!.Value, after.BallSlots!.Value);
        });
        Check("S13 满球槽不限制被动与升级", () =>
        {
            var state = mixed with { BallSlots = 1 };
            Equal(EvaluationStatus.Illegal, engine.Simulate(state, state.Candidates[0]).Status);
            Equal(EvaluationStatus.Illegal, engine.Simulate(state, state.Candidates[1]).Status);
            Assert(engine.Simulate(state, state.Candidates[2]).After is not null);
            Assert(engine.Simulate(state, state.Candidates[3]).After is not null);
        });
        Check("S14 无路线仍有即时效用", () =>
        {
            var score = numeric.Analyze(fixture).Evaluations[0];
            Near(8, score.RawUtility!.Value);
            Equal(0, score.Routes.Length);
            Assert(engine.Analyze(mixed).Evaluations[2].RawUtility is null);
        });
        Check("S15 未知融合无虚假结果", () =>
        {
            var state = snapshots["fusion-context"];
            var result = engine.Analyze(state);
            Equal(EvaluationStatus.Unassessed, result.Evaluations[0].Status);
            Assert(result.Evaluations[0].RawUtility is null && result.RecommendedCandidateId is null);
        });
        Check("S16 未知候选保留位置并限制推荐范围", () =>
        {
            var state = fixture with { Candidates = fixture.Candidates.Add(new Candidate("unknown", 3, ActionKind.Unknown, null, [], null)) };
            var result = numeric.Analyze(state);
            Equal(RecommendationScope.AssessedOnly, result.Scope);
            Equal(3, result.Evaluations[3].Order);
            Assert(result.Evaluations[3].RawUtility is null && result.RecommendedCandidateId is not null);
        });
        Check("S17 非线性等级收益", () =>
        {
            var state = Empty() with { Inventory = [Stock("a", "fixture-a", 1)], Candidates = [new Candidate("up", 0, ActionKind.Upgrade, "fixture-a", ["a"], 2)] };
            Near(3, numeric.Analyze(state).Evaluations[0].RawUtility!.Value);
            state = state with { Inventory = [Stock("a", "fixture-a", 2)], Candidates = [state.Candidates[0] with { TargetLevel = 3 }] };
            Near(4, numeric.Analyze(state).Evaluations[0].RawUtility!.Value);
        });
        Check("S18 缺失成本和短期取舍", () =>
        {
            var state = Empty() with { Inventory = [Stock("a", "fixture-a", 3)] };
            var cheap = new DecisionEngine(calibrated, new PlannerOptions(MissingItemCost: 1, UpgradeCost: 0, StepCost: 0));
            var costly = new DecisionEngine(calibrated, new PlannerOptions(MissingItemCost: 20, UpgradeCost: 0, StepCost: 0));
            var first = cheap.Plan(state).First(route => route.Steps.Length == 1 && route.Steps[0].RecipeId == "fixture-combine");
            var second = costly.Plan(state).First(route => route.Steps.Length == 1 && route.Steps[0].RecipeId == "fixture-combine");
            Assert(first.RawUtility > second.RawUtility);
            var choices = state with { Candidates = [Get("material", 0, "fixture-b"), Get("instant", 1, "fixture-direct")] };
            Equal("instant", costly.Analyze(choices).RecommendedCandidateId!);
            var fewRoutes = new DecisionEngine(calibrated with { Recipes = [] }, new PlannerOptions(PotentialWeight: 0));
            Near(numeric.Analyze(fixture).Evaluations[0].RawUtility!.Value, fewRoutes.Analyze(fixture).Evaluations[0].RawUtility!.Value);
        });
        Check("S19 并列稳定且显示分不影响排序", () =>
        {
            var result = numeric.Analyze(fixture);
            Equal("direct", result.RecommendedCandidateId!);
            Assert(result.Close);
            Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(numeric.Analyze(fixture)));
            var swapped = fixture with { Candidates = fixture.Candidates.SetItem(0, fixture.Candidates[0] with { Order = 1 }).SetItem(1, fixture.Candidates[1] with { Order = 0 }) };
            Equal("area", numeric.Analyze(swapped).RecommendedCandidateId!);
            var saturated = new DecisionEngine(calibrated, new PlannerOptions(PotentialWeight: 0, DisplayScale: 0.01));
            var state = fixture with { Candidates = [Get("low", 0, "fixture-a"), Get("high", 1, "fixture-direct")] };
            var ranked = saturated.Analyze(state);
            Equal(ranked.Evaluations[0].DisplayScore!.Value, ranked.Evaluations[1].DisplayScore!.Value);
            Equal("high", ranked.RecommendedCandidateId!);
        });
        Check("S20 结果版本与换局隔离", () =>
        {
            var result = numeric.Analyze(fixture);
            Assert(result.Matches(fixture, calibrated.Version));
            Assert(!result.Matches(fixture with { RunId = "next" }, calibrated.Version));
            Assert(!result.Matches(fixture with { StateVersion = 2 }, calibrated.Version));
            Assert(!result.Matches(fixture with { ScreenVersion = 2 }, calibrated.Version));
            Assert(!result.Matches(fixture, "next-rules"));
        });
        Check("B01 未知角色和等级不能默认评分", () =>
        {
            Assert(numeric.Analyze(fixture with { CharacterId = null }).RecommendedCandidateId is null);
            var state = fixture with { Inventory = [new Equipment("unknown-level", "fixture-a", null, [])] };
            Assert(numeric.Analyze(state).RecommendedCandidateId is null);
        });
        Check("B02 真正零槽、未知槽与非法升级", () =>
        {
            Equal(EvaluationStatus.Illegal, numeric.Simulate(fixture with { BallSlots = 0 }, fixture.Candidates[0]).Status);
            Equal(EvaluationStatus.Unassessed, numeric.Simulate(fixture with { BallSlots = null }, fixture.Candidates[0]).Status);
            var state = fixture with { Inventory = [Stock("a", "fixture-a", 3)] };
            Equal(EvaluationStatus.Illegal, numeric.Simulate(state, new Candidate("bad", 0, ActionKind.Upgrade, "fixture-a", ["a"], 4)).Status);
            Equal(EvaluationStatus.Illegal, numeric.Simulate(state, new Candidate("bad", 0, ActionKind.Upgrade, "fixture-a", ["a"], 3)).Status);
        });
        Check("B03 融合组成不作为重复材料", () =>
        {
            var state = material with { Inventory = material.Inventory.SetItem(0, material.Inventory[0] with { Components = ["fixture-a", "fixture-b"] }) };
            Equal(EvaluationStatus.Unassessed, engine.Simulate(state, state.Candidates[0]).Status);
            Assert(numeric.Analyze(fixture with { Inventory = state.Inventory }).RecommendedCandidateId is null);
        });
        Check("B04 坏参数与重复候选必须拒绝", () =>
        {
            Throws(() => new DecisionEngine(calibrated, new PlannerOptions(MissingItemCost: -1)));
            Throws(() => new DecisionEngine(calibrated, new PlannerOptions(DisplayScale: double.NaN)));
            Throws(() => numeric.Analyze(fixture with { Candidates = fixture.Candidates.Add(fixture.Candidates[0]) }));
        });
        Check("B05 成本仅扣一次且不变更原材料", () =>
        {
            var state = material with { CharacterId = "fixture-warrior" };
            var result = numeric.Analyze(state).Evaluations[0];
            Near(11 - (9 + 9 + 8), result.RawUtility!.Value);
            Near(0, result.Parts!.OpportunityCost);
            Equal(3, state.Inventory.Length);
        });
        Check("B06 搜索预算和满槽可达性", () =>
        {
            var bounded = new DecisionEngine(calibrated, new PlannerOptions(MaxDepth: 1, StatesPerDepth: 1, MaxExpansions: 1));
            Assert(bounded.Plan(Empty()).Length <= 1);
            Assert(numeric.Plan(Empty() with { BallSlots = 0, PassiveSlots = 0 }).IsEmpty);
        });
        Check("B07 重复制作不同实例不误判循环", () =>
        {
            var repeat = new Recipe("make-c", "fixture-c", [[new Material("fixture-a", 1)]], new Requirements(1, true, 1, false), "synthetic");
            var aggregate = new Recipe("use-two-c", "fixture-b", [[new Material("fixture-c", 2)]], new Requirements(1, true, 1, false), "synthetic");
            var planner = new DecisionEngine(calibrated with { Recipes = [repeat, aggregate] });
            var state = Empty() with { Inventory = [Stock("a1", "fixture-a", 1), Stock("a2", "fixture-a", 1)] };
            var route = planner.Plan(state).First(route => route.Steps.Select(step => step.RecipeId).SequenceEqual(new[] { "make-c", "make-c", "use-two-c" }));
            Assert(route.Steps.All(step => step.MissingMaterials.IsEmpty));
            Equal(1, route.FinalState!.Inventory.Length);
            Equal("fixture-b", route.FinalState.Inventory[0].ItemId!);
        });
        Console.WriteLine($"离线场景：{passed} 通过，{failures.Count} 失败。没有游戏运行验证。");
        return failures.Count == 0 ? 0 : 1;
    }

    /// <summary>建立独立人工校准规则，不给真实游戏物品填入人工收益。</summary>
    private static Rules FixtureRules(Rules sample)
    {
        var items = sample.Items.Values.Where(item => item.Verification == "synthetic").ToImmutableDictionary(item => item.Id)
            .Add("fixture-direct", new ItemRule("fixture-direct", ItemKind.Ball, ["direct_hit"], 3, [8, 10, 12], "synthetic", DirectShare: 1))
            .Add("fixture-area", new ItemRule("fixture-area", ItemKind.Ball, ["area_damage"], 3, [8, 10, 12], "synthetic", AreaShare: 1))
            .Add("fixture-staff", new ItemRule("fixture-staff", ItemKind.Passive, ["boost_area"], 3, [1, 2, 3], "synthetic"));
        var characters = new[] { new CharacterRule("fixture-warrior", true, 1, true), new CharacterRule("fixture-sisyphus", false, 4, true) }
            .ToImmutableDictionary(character => character.Id);
        return new Rules("synthetic-calibration-0.1", items, characters,
            sample.Recipes.Where(recipe => recipe.Verification == "synthetic").ToImmutableArray(),
            [new Synergy("fixture-area-staff", "fixture-staff", "area_damage", 4)]);
    }
    /// <summary>构造与游戏无关的空局面。</summary>
    private static Snapshot Empty() => new("synthetic-run", 1, 1, "fixture-warrior", 5, 5, [], []);
    /// <summary>构造一级获取候选。</summary>
    private static Candidate Get(string id, int order, string item) => new(id, order, ActionKind.Acquire, item, [], 1);
    /// <summary>构造无融合组成的人工装备实例。</summary>
    private static Equipment Stock(string id, string item, int level) => new(id, item, level, []);
    /// <summary>断言条件，失败时报告调用位置。</summary>
    private static void Assert(bool condition, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        if (!condition) throw new InvalidOperationException($"断言失败，行 {line}。");
    }
    /// <summary>比较独立预期值与实际值。</summary>
    private static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"预期 {expected}，实际 {actual}。");
    }
    /// <summary>用容差核验数值分项。</summary>
    private static void Near(double expected, double actual) => Assert(Math.Abs(expected - actual) < 0.000001);
    /// <summary>校验非法输入被明确拒绝。</summary>
    private static void Throws(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new InvalidOperationException("应抛出 ArgumentException。");
    }
}
