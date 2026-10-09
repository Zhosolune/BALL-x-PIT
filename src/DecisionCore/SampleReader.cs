using System.Collections.Immutable;
using System.Text.Json;

namespace BallPit.DecisionCore;

/// <summary>将 P0 离线样本显式映射为核心类型，不依赖 Unity 或外部包。</summary>
public static class SampleReader
{
    /// <summary>读取 P0 样本；JSON 结构或必需字段错误时抛出 JsonException/InvalidOperationException。</summary>
    public static Rules ReadRules(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (Text(root, "schemaVersion") != "0.1" || Text(root, "purpose") != "offline_preparation_only")
            throw new InvalidOperationException("只能读取 P0 离线准备数据。");
        var items = root.GetProperty("items").EnumerateArray().Select(item => new ItemRule(
            Text(item, "id"), Text(item, "kind") switch { "ball" => ItemKind.Ball, "passive" => ItemKind.Passive,
                _ => throw new InvalidOperationException("未知物品类别。") },
            Strings(item, "tags"), Integer(item, "maxLevel"),
            item.GetProperty("levelUtility").ValueKind == JsonValueKind.Null ? null
                : item.GetProperty("levelUtility").EnumerateArray().Select(value => value.GetDouble()).ToImmutableArray(),
            Text(item, "verification"))).ToImmutableDictionary(item => item.Id);
        var characters = root.GetProperty("characters").EnumerateArray().Select(character => new CharacterRule(
            Text(character, "id"), character.GetProperty("mechanics").GetProperty("directHitEnabled").GetBoolean(),
            character.GetProperty("mechanics").GetProperty("areaDamageMultiplier").GetDouble(), false)).ToImmutableDictionary(character => character.Id);
        var recipes = root.GetProperty("recipes").EnumerateArray().Select(recipe =>
        {
            var requirements = recipe.GetProperty("requirements");
            return new Recipe(Text(recipe, "id"), Text(recipe, "resultItemId"), recipe.GetProperty("variants").EnumerateArray()
                .Select(variant => variant.EnumerateArray().Select(material => new Material(Text(material, "itemId"),
                    material.GetProperty("quantity").GetInt32())).ToImmutableArray()).ToImmutableArray(),
                new Requirements(Integer(requirements, "minimumLevel"), Boolean(requirements, "consumesInputs"),
                    Integer(requirements, "resultLevel"), Boolean(requirements, "fusionInputsAllowed")), Text(recipe, "verification"));
        }).ToImmutableArray();
        var synergies = root.GetProperty("synergies").EnumerateArray().Select(synergy => new Synergy(Text(synergy, "id"),
            Text(synergy, "providerItemId"), Text(synergy, "targetTag"), null)).ToImmutableArray();
        return new Rules(Text(root, "rulesVersion"), items, characters, recipes, synergies);
    }

    /// <summary>读取场景目录中的基础快照；未知操作显式保留为 Unknown。</summary>
    public static ImmutableDictionary<string, Snapshot> ReadSnapshots(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("snapshots").EnumerateArray().ToImmutableDictionary(
            snapshot => Text(snapshot, "id"), snapshot => new Snapshot(Text(snapshot, "runId"),
                snapshot.GetProperty("stateVersion").GetInt64(), snapshot.GetProperty("screenVersion").GetInt64(),
                NullableText(snapshot, "characterId"), Integer(snapshot.GetProperty("slotLimits"), "ball"),
                Integer(snapshot.GetProperty("slotLimits"), "passive"),
                snapshot.GetProperty("inventory").EnumerateArray().Select(entry => new Equipment(Text(entry, "instanceId"),
                    NullableText(entry, "itemId"), Integer(entry, "level"), Strings(entry, "components"))).ToImmutableArray(),
                snapshot.GetProperty("candidates").EnumerateArray().Select(candidate => new Candidate(Text(candidate, "id"),
                    candidate.GetProperty("order").GetInt32(), Text(candidate, "action") switch
                    {
                        "acquire" => ActionKind.Acquire, "upgrade" => ActionKind.Upgrade, "evolve" => ActionKind.Evolve,
                        "fuse" => ActionKind.Fuse, "unknown" => ActionKind.Unknown,
                        _ => throw new InvalidOperationException("未知操作字符串。")
                    }, NullableText(candidate, "itemId"), Strings(candidate, "inputInstanceIds"), Integer(candidate, "targetLevel"),
                    NullableText(candidate, "recipeId"), Integer(candidate, "variantIndex"))).ToImmutableArray()));
    }

    /// <summary>读取必需的非空字符串。</summary>
    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString()
        ?? throw new InvalidOperationException($"{name} 不能为 null。");
    /// <summary>保留未知文本字段。</summary>
    private static string? NullableText(JsonElement element, string name) => element.GetProperty(name).GetString();
    /// <summary>保留未知整数，不补默认等级。</summary>
    private static int? Integer(JsonElement element, string name) => element.GetProperty(name).ValueKind == JsonValueKind.Null
        ? null : element.GetProperty(name).GetInt32();
    /// <summary>保留未知布尔条件。</summary>
    private static bool? Boolean(JsonElement element, string name) => element.GetProperty(name).ValueKind == JsonValueKind.Null
        ? null : element.GetProperty(name).GetBoolean();
    /// <summary>复制文本集合，避免计算过程中修改输入。</summary>
    private static ImmutableArray<string> Strings(JsonElement element, string name) => element.GetProperty(name)
        .EnumerateArray().Select(value => value.GetString() ?? throw new InvalidOperationException("数组文本不能为 null。"))
        .ToImmutableArray();
}
