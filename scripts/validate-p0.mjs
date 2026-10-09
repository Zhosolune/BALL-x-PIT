/** P0 离线规则与场景目录校验；不计算推荐，不验证真实游戏机制。 */
import { readFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';

/** 条件不满足时抛出包含数据位置的错误。 */
function requireCondition(condition, message) {
  if (!condition) throw new Error(message);
}

/** 检查数组和唯一标识，返回便于引用检查的索引。 */
function indexById(entries, label) {
  requireCondition(Array.isArray(entries), `${label} 必须是数组`);
  const index = new Map();
  for (const entry of entries) {
    requireCondition(entry && typeof entry.id === 'string' && entry.id.length > 0, `${label} 缺少 id`);
    requireCondition(!index.has(entry.id), `${label} 重复 id：${entry.id}`);
    index.set(entry.id, entry);
  }
  return index;
}

/** 判断数值是否为正整数，等级和数量不能用零或小数表示。 */
function positiveInteger(value) {
  return Number.isInteger(value) && value > 0;
}

/** 校验验证状态与来源归属，阻止人工数据伪装成社区数据。 */
function checkEvidence(entry, sources) {
  requireCondition(sources.has(entry.sourceId), `${entry.id} 来源不存在`);
  requireCondition(['community_reported', 'synthetic'].includes(entry.verification), `${entry.id} 验证状态错误`);
  requireCondition((entry.verification === 'synthetic') === (entry.sourceId === 'fixture'), `${entry.id} 人工数据来源不匹配`);
}

/**
 * 校验 P0 样本中的结构、来源与物品/配方引用。
 * @param {object} sample 从 p0-sample.json 解析的离线样本。
 * @returns {{sources: Map, characters: Map, items: Map, recipes: Map}} 已校验的索引。
 * @throws {Error} 字段无效、引用缺失或人工数据混入游戏规则时抛出。
 */
export function validateSample(sample) {
  requireCondition(sample.schemaVersion === '0.1', '样本 schemaVersion 不支持');
  requireCondition(sample.purpose === 'offline_preparation_only', '样本只能用于离线准备');
  requireCondition(sample.supportedGameVersion === null, 'P0 不得宣称已支持游戏版本');
  requireCondition(typeof sample.rulesVersion === 'string' && sample.rulesVersion.length > 0, '规则版本缺失');
  const sources = indexById(sample.sources, '来源');
  const characters = indexById(sample.characters, '角色');
  const items = indexById(sample.items, '物品');
  const recipes = indexById(sample.recipes, '配方');
  for (const source of sources.values()) {
    requireCondition(typeof source.url === 'string' && source.url.length > 0, `${source.id} 来源地址缺失`);
    requireCondition(typeof source.usage === 'string' && source.usage.length > 0, `${source.id} 使用声明缺失`);
    requireCondition(typeof source.revision === 'string' && source.revision.length > 0, `${source.id} 来源版本缺失`);
  }
  for (const character of characters.values()) {
    checkEvidence(character, sources);
    requireCondition(character.gameSlug === null, `${character.id} 游戏角色映射尚未验证`);
    requireCondition(positiveInteger(character.slotLimits.ball) && positiveInteger(character.slotLimits.passive), `${character.id} 槽位无效`);
    requireCondition(typeof character.mechanics.directHitEnabled === 'boolean', `${character.id} 直伤规则缺失`);
    requireCondition(Number.isFinite(character.mechanics.areaDamageMultiplier) && character.mechanics.areaDamageMultiplier > 0, `${character.id} 范围系数无效`);
  }
  for (const item of items.values()) {
    checkEvidence(item, sources);
    requireCondition(['ball', 'passive'].includes(item.kind), `${item.id} 物品类别无效`);
    requireCondition(Array.isArray(item.tags) && item.tags.every(tag => typeof tag === 'string'), `${item.id} 标签无效`);
    requireCondition(item.gameSlug === null, `${item.id} 游戏物品映射尚未验证`);
    if (item.verification === 'synthetic') {
      requireCondition(positiveInteger(item.maxLevel), `${item.id} 人工最大等级无效`);
      requireCondition(Array.isArray(item.levelUtility) && item.levelUtility.length === item.maxLevel && item.levelUtility.every(Number.isFinite), `${item.id} 人工等级效用不完整`);
    } else {
      requireCondition(item.maxLevel === null && item.levelUtility === null, `${item.id} 未验证等级规则必须保持未知`);
    }
  }
  for (const recipe of recipes.values()) {
    checkEvidence(recipe, sources);
    requireCondition(items.has(recipe.resultItemId), `${recipe.id} 结果物品不存在`);
    requireCondition(Array.isArray(recipe.variants) && recipe.variants.length > 0, `${recipe.id} 配方为空`);
    const result = items.get(recipe.resultItemId);
    requireCondition(result.verification === recipe.verification, `${recipe.id} 结果混用人工和社区数据`);
    for (const variant of recipe.variants) {
      requireCondition(Array.isArray(variant) && variant.length > 0, `${recipe.id} 材料为空`);
      const usedItems = new Set();
      for (const material of variant) {
        const item = items.get(material.itemId);
        requireCondition(item && item.kind === result.kind, `${recipe.id} 材料引用或类别错误`);
        requireCondition(item.verification === recipe.verification, `${recipe.id} 材料混用人工和社区数据`);
        requireCondition(positiveInteger(material.quantity), `${recipe.id} 材料数量无效`);
        requireCondition(!usedItems.has(material.itemId), `${recipe.id} 重复材料应合并到 quantity`);
        usedItems.add(material.itemId);
      }
    }
    const requirements = recipe.requirements;
    requireCondition(requirements && ['minimumLevel', 'consumesInputs', 'resultLevel', 'fusionInputsAllowed'].every(key => Object.hasOwn(requirements, key)), `${recipe.id} 条件字段不完整`);
    if (recipe.verification === 'synthetic') {
      requireCondition(positiveInteger(requirements.minimumLevel) && positiveInteger(requirements.resultLevel), `${recipe.id} 人工等级条件无效`);
      requireCondition(typeof requirements.consumesInputs === 'boolean' && typeof requirements.fusionInputsAllowed === 'boolean', `${recipe.id} 人工消耗或融合条件无效`);
    } else {
      requireCondition(Object.values(requirements).every(value => value === null), `${recipe.id} 未验证条件必须保持未知`);
    }
  }
  for (const synergy of indexById(sample.synergies, '协同').values()) {
    checkEvidence(synergy, sources);
    requireCondition(items.has(synergy.providerItemId), `${synergy.id} 协同来源物品不存在`);
    requireCondition([...items.values()].some(item => item.tags.includes(synergy.targetTag)), `${synergy.id} 协同目标标签不存在`);
    requireCondition(synergy.weight === null, `${synergy.id} 协同权重尚未校准`);
  }
  requireCondition(sample.fusion?.verification === 'pending', '融合机制仍待验证');
  requireCondition(['compositionReadable', 'levelPolicy', 'recipeEligibility', 'slotPolicy'].every(key => Object.hasOwn(sample.fusion, key) && sample.fusion[key] === null), '融合未知字段不得填默认值');
  requireCondition(sample.scoring?.weights === null && sample.scoring.displayMapping === null && sample.scoring.closeThreshold === null, 'P0 评分参数尚未校准');
  requireCondition(positiveInteger(sample.scoring.maxRouteDepth) && positiveInteger(sample.scoring.maxStatesPerDepth), '搜索预算无效');
  return { sources, characters, items, recipes };
}

/**
 * 校验基础快照和验收场景的引用；不执行 variation 中描述的算法。
 * @param {object} catalog 从 p0-scenarios.json 解析的场景目录。
 * @param {object} sample 与目录对应的 P0 规则样本。
 * @returns {{snapshots: number, scenarios: number, coverage: string[]}} 场景数量与覆盖类别。
 * @throws {Error} 版本、标识、输入引用或必需覆盖类别无效时抛出。
 */
export function validateScenarios(catalog, sample) {
  const { characters, items, recipes } = validateSample(sample);
  requireCondition(catalog.schemaVersion === sample.schemaVersion && catalog.rulesVersion === sample.rulesVersion, '场景与样本版本不一致');
  requireCondition(catalog.purpose === 'acceptance_catalog_not_executed', '场景目录不得宣称算法已经执行');
  const snapshots = indexById(catalog.snapshots, '快照');
  const scenarios = indexById(catalog.scenarios, '场景');
  for (const snapshot of snapshots.values()) {
    requireCondition(characters.has(snapshot.characterId), `${snapshot.id} 角色不存在`);
    requireCondition(typeof snapshot.runId === 'string' && snapshot.runId.length > 0, `${snapshot.id} 本局标识缺失`);
    requireCondition(positiveInteger(snapshot.stateVersion) && positiveInteger(snapshot.screenVersion), `${snapshot.id} 状态版本无效`);
    requireCondition(['upgrade', 'evolution', 'fusion'].includes(snapshot.screenKind), `${snapshot.id} 界面类型无效`);
    requireCondition(['ball', 'passive'].every(kind => Number.isInteger(snapshot.slotLimits[kind]) && snapshot.slotLimits[kind] >= 0), `${snapshot.id} 实际槽位无效`);
    // 装备按实例建立索引；两个同种材料不能由一个实例冒充。
    const inventory = indexById(snapshot.inventory.map(entry => ({ ...entry, id: entry.instanceId })), `${snapshot.id} 装备`);
    for (const entry of inventory.values()) {
      requireCondition(items.has(entry.itemId) && positiveInteger(entry.level), `${entry.id} 装备物品或等级无效`);
      requireCondition(Array.isArray(entry.components) && entry.components.every(id => items.has(id)), `${entry.id} 融合组成引用错误`);
      const item = items.get(entry.itemId);
      requireCondition(item.maxLevel === null || entry.level <= item.maxLevel, `${entry.id} 超过已定义最大等级`);
    }
    const orders = new Set();
    for (const candidate of indexById(snapshot.candidates, `${snapshot.id} 候选`).values()) {
      requireCondition(Number.isInteger(candidate.order) && candidate.order >= 0 && !orders.has(candidate.order), `${candidate.id} 画面顺序错误`);
      orders.add(candidate.order);
      requireCondition(['acquire', 'upgrade', 'evolve', 'fuse', 'unknown'].includes(candidate.action), `${candidate.id} 操作类型无效`);
      requireCondition(Array.isArray(candidate.inputInstanceIds) && new Set(candidate.inputInstanceIds).size === candidate.inputInstanceIds.length, `${candidate.id} 输入实例重复`);
      requireCondition(candidate.inputInstanceIds.every(id => inventory.has(id)), `${candidate.id} 输入实例不存在`);
      requireCondition(candidate.itemId === null || items.has(candidate.itemId), `${candidate.id} 候选物品不存在`);
      if (['acquire', 'upgrade', 'evolve'].includes(candidate.action)) {
        requireCondition(items.has(candidate.itemId) && positiveInteger(candidate.targetLevel), `${candidate.id} 目标物品或等级缺失`);
      }
      if (candidate.action === 'acquire') requireCondition(candidate.inputInstanceIds.length === 0, `${candidate.id} 获取操作不能消耗实例`);
      if (candidate.action === 'upgrade') {
        requireCondition(candidate.inputInstanceIds.length === 1, `${candidate.id} 升级需要一个目标实例`);
        const input = inventory.get(candidate.inputInstanceIds[0]);
        requireCondition(input.itemId === candidate.itemId && candidate.targetLevel > input.level, `${candidate.id} 升级目标不一致`);
      }
      if (candidate.action === 'evolve') {
        const recipe = recipes.get(candidate.recipeId);
        requireCondition(recipe && recipe.resultItemId === candidate.itemId, `${candidate.id} 配方引用错误`);
        requireCondition(Number.isInteger(candidate.variantIndex) && candidate.variantIndex >= 0 && candidate.variantIndex < recipe.variants.length, `${candidate.id} 替代配方索引错误`);
      } else {
        requireCondition(candidate.recipeId === null && candidate.variantIndex === null, `${candidate.id} 非进化操作不能绑定进化配方`);
      }
      if (candidate.action === 'fuse') requireCondition(candidate.inputInstanceIds.length >= 2 && candidate.itemId === null && candidate.targetLevel === null, `${candidate.id} 未验证融合结果必须保持未知`);
    }
  }
  const coverage = new Set();
  for (const scenario of scenarios.values()) {
    requireCondition(snapshots.has(scenario.snapshotId), `${scenario.id} 基础快照不存在`);
    requireCondition(typeof scenario.title === 'string' && typeof scenario.variation === 'string' && scenario.variation.length > 0, `${scenario.id} 场景描述缺失`);
    requireCondition(Array.isArray(scenario.assertions) && scenario.assertions.length > 0 && scenario.assertions.every(value => typeof value === 'string' && value.length > 0), `${scenario.id} 验收断言缺失`);
    requireCondition(Array.isArray(scenario.coverage) && scenario.coverage.every(value => typeof value === 'string'), `${scenario.id} 覆盖标签无效`);
    scenario.coverage.forEach(tag => coverage.add(tag));
  }
  for (const required of ['character', 'mixed', 'synergy', 'alternative', 'upgrade', 'quantity', 'consumption', 'cycle', 'fusion', 'unknown', 'slots', 'scoring', 'stale_result']) {
    requireCondition(coverage.has(required), `缺少验收类别：${required}`);
  }
  requireCondition(scenarios.size >= 20, '验收场景至少二十个');
  return { snapshots: snapshots.size, scenarios: scenarios.size, coverage: [...coverage].sort() };
}

/** 加载工作区样本并报告结构检查结果，任何失败以非零状态退出。 */
async function main() {
  const sample = JSON.parse(await readFile(new URL('../data/p0-sample.json', import.meta.url), 'utf8'));
  const catalog = JSON.parse(await readFile(new URL('../data/p0-scenarios.json', import.meta.url), 'utf8'));
  const result = validateScenarios(catalog, sample);
  console.log(`P0 数据检查通过：${sample.characters.length} 个角色、${sample.items.length} 个物品、${sample.recipes.length} 个配方、${result.snapshots} 份快照、${result.scenarios} 个验收场景。`);
  console.log('这仅是结构与引用检查；推荐算法和真实游戏规则尚未验证。');
}

// 被导入用于损坏样本检查时，不执行命令行入口。
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main().catch(error => { console.error(`P0 数据检查失败：${error.message}`); process.exitCode = 1; });
}
