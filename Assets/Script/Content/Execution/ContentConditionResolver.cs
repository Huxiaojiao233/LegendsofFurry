using System;
using System.Collections.Generic;
using System.Linq;
using LegendsOfFurry.Content.Contracts;

#pragma warning disable 0649 // 条件参数由安全 JSON 映射填充。

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 描述逻辑条件列表中的一个受控子条件。
/// </summary>
public sealed class ContentNestedCondition
{
    public string key;
    public ContentConditionParameters parameters;
}

/// <summary>
/// 保存首期条件处理器共用的结构化参数，不包含任意可执行表达式文本。
/// </summary>
public sealed class ContentConditionParameters
{
    public string target;
    public string team;
    public string statusId;
    public string tag;
    public string poolId;
    public string rarityId;
    public string comparison;
    public float chance;
    public ContentValueExpression left;
    public ContentValueExpression right;
    public ContentValueExpression value;
    public ContentNestedCondition[] conditions;
}

/// <summary>
/// 计算一个条件 key；未知或参数无效必须报告失败，与条件结果 false 明确区分。
/// </summary>
public interface IContentConditionHandler
{
    /// <summary>
    /// 计算当前条件。
    /// </summary>
    /// <param name="parameters">结构化条件参数。</param>
    /// <param name="context">本次卡牌执行上下文。</param>
    /// <param name="target">当前条件目标单位。</param>
    /// <param name="depth">逻辑条件递归深度。</param>
    /// <param name="result">成功计算后的真假结果。</param>
    /// <returns>处理器成功理解并计算条件时返回 true。</returns>
    bool TryEvaluate(
        ContentConditionParameters parameters,
        ContentCardExecutionContext context,
        Unit target,
        int depth,
        out bool result);
}

/// <summary>
/// 通过白名单注册表计算卡牌条件，未知 key、坏参数和过深逻辑树会在执行前明确失败。
/// </summary>
public sealed class ContentConditionResolver
{
    private const int MaximumDepth = 64;
    private readonly ContentOperationRegistry<IContentConditionHandler> registry =
        new ContentOperationRegistry<IContentConditionHandler>();
    private readonly ContentValueExpressionResolver values;

    /// <summary>
    /// 创建条件注册表，并复用传入或默认的数值表达式解析器。
    /// </summary>
    /// <param name="valueResolver">可选的共享数值表达式解析器。</param>
    public ContentConditionResolver(ContentValueExpressionResolver valueResolver = null)
    {
        values = valueResolver ?? new ContentValueExpressionResolver();
        Register("all", ResolveAll);
        Register("any", ResolveAny);
        Register("not", ResolveNot);
        Register("target_is_alive", ResolveTargetIsAlive);
        Register("target_is_dead", ResolveTargetIsDead);
        Register("target_is_self", ResolveTargetIsSelf);
        Register("target_team_is", ResolveTargetTeamIs);
        Register("target_has_armor", ResolveTargetHasArmor);
        Register("target_has_status", ResolveTargetHasStatus);
        Register("card_has_tag", ResolveCardHasTag);
        Register("card_in_pool", ResolveCardInPool);
        Register("card_rarity_is", ResolveCardRarityIs);
        Register("target_killed_by_this_card", ResolveTargetKilledByThisCard);
        Register("damage_was_applied", ResolveDamageWasApplied);
        Register("health_damage_greater_than", ResolveHealthDamageGreaterThan);
        Register("resource_compare", ResolveExpressionComparison);
        Register("health_compare", ResolveExpressionComparison);
        Register("armor_compare", ResolveExpressionComparison);
        Register("spent_action_compare", ResolveSpentActionComparison);
        Register("spent_mana_compare", ResolveSpentManaComparison);
        Register("random_chance", ResolveRandomChance);
    }

    /// <summary>
    /// 解析行为节点参数并计算条件，默认目标优先使用 foreach 当前单位，再使用已选单位。
    /// </summary>
    /// <param name="node">NodeKind=condition 的行为节点。</param>
    /// <param name="context">本次卡牌执行上下文。</param>
    /// <param name="result">成功计算后的真假结果。</param>
    /// <returns>节点类型、key、JSON 和参数全部有效时返回 true。</returns>
    public bool TryEvaluate(
        BehaviorNodeDefinition node,
        ContentCardExecutionContext context,
        out bool result)
    {
        result = false;
        if (node == null || node.NodeKind != "condition" || context == null)
        {
            return false;
        }
        try
        {
            if (!TryReadParameters(node.ParametersJson, out ContentConditionParameters parameters))
            {
                return false;
            }
            Unit target = context.CurrentGraphTarget as Unit ?? context.SelectedUnit;
            return TryEvaluateNested(node.OperationKey, parameters, context, target, 0, out result);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// 判断指定条件 key 是否有可执行处理器，供启动预检区分未知与尚未实现能力。
    /// </summary>
    /// <param name="key">条件操作 key。</param>
    /// <returns>已注册时返回 true。</returns>
    public bool Contains(string key)
    {
        return registry.Contains(key);
    }

    /// <summary>
    /// 在扣费前验证条件 key、JSON、逻辑子条件和嵌套表达式结构。
    /// </summary>
    /// <param name="node">需要预检的 Condition 节点。</param>
    /// <returns>结构完整且所有能力已注册时返回 true。</returns>
    public bool CanEvaluateNode(BehaviorNodeDefinition node)
    {
        if (node == null || node.NodeKind != "condition" || !registry.Contains(node.OperationKey))
        {
            return false;
        }
        try
        {
            if (!TryReadParameters(node.ParametersJson, out ContentConditionParameters parameters))
            {
                return false;
            }
            return CanEvaluateParameters(node.OperationKey, parameters, 0);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// 返回全部已实现条件 key 的只读有序快照。
    /// </summary>
    /// <returns>注册 key 列表。</returns>
    public System.Collections.Generic.IReadOnlyList<string> GetRegisteredKeys()
    {
        return registry.GetRegisteredKeys();
    }

    /// <summary>用安全 JSON 解析器读取条件参数，避免 JsonUtility 对递归类型走 10 层序列化深度。</summary>
    private static bool TryReadParameters(string json, out ContentConditionParameters parameters)
    {
        parameters = new ContentConditionParameters();
        if (string.IsNullOrWhiteSpace(json)) return true;
        if (!ContentSafeJsonParser.TryParse(json, out object parsed)) return false;
        return TryMapParameters(parsed, 0, out parameters);
    }

    private static bool TryMapParameters(object parsed, int depth, out ContentConditionParameters parameters)
    {
        parameters = null;
        if (depth > MaximumDepth || parsed is not Dictionary<string, object> values)
        {
            return false;
        }

        parameters = new ContentConditionParameters();
        TryReadString(values, "target", out parameters.target);
        TryReadString(values, "team", out parameters.team);
        TryReadString(values, "statusId", out parameters.statusId);
        TryReadString(values, "tag", out parameters.tag);
        TryReadString(values, "poolId", out parameters.poolId);
        TryReadString(values, "rarityId", out parameters.rarityId);
        TryReadString(values, "comparison", out parameters.comparison);
        if (values.TryGetValue("chance", out object chance) && !TryReadFloat(chance, out parameters.chance))
        {
            return false;
        }
        if (!TryMapOptionalExpression(values, "left", out parameters.left) ||
            !TryMapOptionalExpression(values, "right", out parameters.right) ||
            !TryMapOptionalExpression(values, "value", out parameters.value) ||
            !TryMapNestedConditions(values, depth, out parameters.conditions))
        {
            return false;
        }

        return true;
    }

    private static bool TryMapOptionalExpression(
        Dictionary<string, object> values, string key, out ContentValueExpression expression)
    {
        expression = null;
        return !values.TryGetValue(key, out object parsed) ||
               ContentValueExpressionResolver.TryMapFromParsed(parsed, out expression);
    }

    private static bool TryMapNestedConditions(
        Dictionary<string, object> values, int depth, out ContentNestedCondition[] conditions)
    {
        conditions = null;
        if (!values.TryGetValue("conditions", out object parsed) || parsed == null) return true;
        if (parsed is not List<object> items) return false;
        conditions = new ContentNestedCondition[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not Dictionary<string, object> child ||
                !TryReadString(child, "key", out string key))
            {
                return false;
            }

            ContentConditionParameters nested = new ContentConditionParameters();
            if (child.TryGetValue("parameters", out object nestedParsed) && nestedParsed != null &&
                !TryMapParameters(nestedParsed, depth + 1, out nested))
            {
                return false;
            }

            conditions[i] = new ContentNestedCondition { key = key, parameters = nested };
        }

        return true;
    }

    private static bool TryReadString(Dictionary<string, object> values, string key, out string value)
    {
        value = null;
        if (!values.TryGetValue(key, out object parsed) || parsed == null) return false;
        value = parsed as string;
        return value != null;
    }

    private static bool TryReadFloat(object parsed, out float value)
    {
        value = 0f;
        switch (parsed)
        {
            case double number:
                value = (float)number;
                return true;
            case long integer:
                value = integer;
                return true;
            default:
                return false;
        }
    }

    /// <summary>注册一个强类型条件委托。</summary>
    private void Register(string key, ConditionHandlerDelegate handler)
    {
        registry.Register(key, new DelegateConditionHandler(handler));
    }

    /// <summary>递归查询条件处理器并统一实施深度限制。</summary>
    private bool TryEvaluateNested(
        string key,
        ContentConditionParameters parameters,
        ContentCardExecutionContext context,
        Unit target,
        int depth,
        out bool result)
    {
        result = false;
        return depth <= MaximumDepth && registry.TryGet(key, out IContentConditionHandler handler) &&
               handler.TryEvaluate(parameters ?? new ContentConditionParameters(), context, target, depth, out result);
    }

    /// <summary>递归验证一个条件及其逻辑子条件和表达式参数。</summary>
    private bool CanEvaluateParameters(string key, ContentConditionParameters parameters, int depth)
    {
        if (depth > MaximumDepth || !registry.Contains(key) || parameters == null)
        {
            return false;
        }
        switch (key)
        {
            case "all":
            case "any":
                return (parameters.conditions ?? Array.Empty<ContentNestedCondition>()).All(condition =>
                    condition != null && CanEvaluateParameters(condition.key, condition.parameters, depth + 1));
            case "not":
                return parameters.conditions != null && parameters.conditions.Length == 1 &&
                       parameters.conditions[0] != null &&
                       CanEvaluateParameters(
                           parameters.conditions[0].key,
                           parameters.conditions[0].parameters,
                           depth + 1);
            case "resource_compare":
            case "health_compare":
            case "armor_compare":
                return IsKnownComparison(parameters.comparison) &&
                       values.CanResolveStructure(parameters.left) &&
                       values.CanResolveStructure(parameters.right);
            case "spent_action_compare":
            case "spent_mana_compare":
                return IsKnownComparison(parameters.comparison) && values.CanResolveStructure(parameters.value);
            case "health_damage_greater_than":
                return values.CanResolveStructure(parameters.value);
            case "target_team_is":
                return parameters.team is "ally" or "enemy" or "any";
            case "target_has_status":
                return !string.IsNullOrWhiteSpace(parameters.statusId);
            case "card_has_tag":
                return !string.IsNullOrWhiteSpace(parameters.tag);
            case "card_in_pool":
                return !string.IsNullOrWhiteSpace(parameters.poolId);
            case "card_rarity_is":
                return !string.IsNullOrWhiteSpace(parameters.rarityId);
            case "random_chance":
                return parameters.chance >= 0f && parameters.chance <= 1f;
            default:
                return true;
        }
    }

    /// <summary>要求全部嵌套条件为真；空集合按逻辑恒真处理。</summary>
    private bool ResolveAll(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
    {
        result = true;
        foreach (ContentNestedCondition condition in parameters.conditions ?? Array.Empty<ContentNestedCondition>())
        {
            if (condition == null || !TryEvaluateNested(condition.key, condition.parameters, context, target, depth + 1, out bool child))
            {
                result = false;
                return false;
            }
            if (!child)
            {
                result = false;
                return true;
            }
        }
        return true;
    }

    /// <summary>要求至少一个嵌套条件为真；空集合按逻辑恒假处理。</summary>
    private bool ResolveAny(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
    {
        result = false;
        foreach (ContentNestedCondition condition in parameters.conditions ?? Array.Empty<ContentNestedCondition>())
        {
            if (condition == null || !TryEvaluateNested(condition.key, condition.parameters, context, target, depth + 1, out bool child))
            {
                return false;
            }
            if (child)
            {
                result = true;
                return true;
            }
        }
        return true;
    }

    /// <summary>对唯一嵌套条件取反，并拒绝零个或多个子条件。</summary>
    private bool ResolveNot(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
    {
        result = false;
        if (parameters.conditions == null || parameters.conditions.Length != 1 || parameters.conditions[0] == null ||
            !TryEvaluateNested(parameters.conditions[0].key, parameters.conditions[0].parameters, context, target, depth + 1, out bool child))
        {
            return false;
        }
        result = !child;
        return true;
    }

    /// <summary>判断当前目标存在且存活。</summary>
    private bool ResolveTargetIsAlive(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
    {
        result = target != null && target.IsAlive;
        return target != null;
    }

    /// <summary>判断当前目标存在且死亡。</summary>
    private bool ResolveTargetIsDead(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
    {
        result = target != null && !target.IsAlive;
        return target != null;
    }

    /// <summary>判断当前目标与施放者是同一单位。</summary>
    private bool ResolveTargetIsSelf(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
    {
        result = target != null && target == context.Source;
        return target != null && context.Source != null;
    }

    /// <summary>按 ally、enemy 或 any 判断当前目标与施放者的阵营关系。</summary>
    private bool ResolveTargetTeamIs(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
    {
        result = false;
        if (target == null || context.Source == null)
        {
            return false;
        }
        bool sameFaction = target.Faction == context.Source.Faction;
        if (parameters.team == "ally") result = sameFaction;
        else if (parameters.team == "enemy") result = !sameFaction;
        else if (parameters.team == "any") result = true;
        else return false;
        return true;
    }

    /// <summary>判断当前目标护甲是否大于零。</summary>
    private bool ResolveTargetHasArmor(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
    {
        result = target != null && target.Armor > 0;
        return target != null;
    }

    /// <summary>按旧状态枚举稳定名称判断当前目标是否拥有状态。</summary>
    private bool ResolveTargetHasStatus(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
    {
        result = false;
        if (target == null || string.IsNullOrWhiteSpace(parameters.statusId))
        {
            return false;
        }
        result = target.State.Has(parameters.statusId);
        return true;
    }

    /// <summary>判断当前卡牌定义是否包含指定标签。</summary>
    private bool ResolveCardHasTag(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
    {
        result = context.Card?.Definition?.Tags.Contains(parameters.tag) == true;
        return context.Card?.Definition != null && !string.IsNullOrWhiteSpace(parameters.tag);
    }

    /// <summary>判断当前卡牌定义是否属于指定卡池。</summary>
    private bool ResolveCardInPool(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
    {
        result = context.Card?.Definition?.Pools.Any(item => item.PoolId == parameters.poolId) == true;
        return context.Card?.Definition != null && !string.IsNullOrWhiteSpace(parameters.poolId);
    }

    /// <summary>判断当前卡牌定义稀有度是否精确匹配稳定 ID。</summary>
    private bool ResolveCardRarityIs(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
    {
        result = context.Card?.Definition?.RarityId == parameters.rarityId;
        return context.Card?.Definition != null && !string.IsNullOrWhiteSpace(parameters.rarityId);
    }

    /// <summary>判断当前目标是否由本卡已记录伤害击杀。</summary>
    private bool ResolveTargetKilledByThisCard(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
    {
        result = context.WasKilledByThisCard(target);
        return target != null;
    }

    /// <summary>判断本卡是否产生过护甲吸收或实际生命伤害。</summary>
    private bool ResolveDamageWasApplied(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
    {
        result = context.DamageRecords.Any(record => record.AbsorbedByArmor > 0 || record.HealthDamage > 0);
        return true;
    }

    /// <summary>判断本卡累计生命伤害是否大于结构化阈值表达式。</summary>
    private bool ResolveHealthDamageGreaterThan(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
    {
        result = false;
        if (!values.TryResolve(parameters.value, context, target, out int threshold))
        {
            return false;
        }
        long damage = context.DamageRecords.Sum(record => (long)record.HealthDamage);
        result = damage > threshold;
        return true;
    }

    /// <summary>比较左右两个受控数值表达式。</summary>
    private bool ResolveExpressionComparison(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
    {
        result = false;
        return values.TryResolve(parameters.left, context, target, out int left) &&
               values.TryResolve(parameters.right, context, target, out int right) &&
               TryCompare(left, right, parameters.comparison, out result);
    }

    /// <summary>比较本次实际消耗行动点与结构化阈值表达式。</summary>
    private bool ResolveSpentActionComparison(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
    {
        result = false;
        return values.TryResolve(parameters.value, context, target, out int right) &&
               TryCompare(context.Resources.SpentActionPoints, right, parameters.comparison, out result);
    }

    /// <summary>比较本次实际消耗法力与结构化阈值表达式。</summary>
    private bool ResolveSpentManaComparison(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
    {
        result = false;
        return values.TryResolve(parameters.value, context, target, out int right) &&
               TryCompare(context.Resources.SpentMana, right, parameters.comparison, out result);
    }

    /// <summary>使用上下文随机源按 [0,1] 概率判断条件。</summary>
    private bool ResolveRandomChance(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
    {
        result = false;
        if (parameters.chance < 0f || parameters.chance > 1f || context.RandomSource == null)
        {
            return false;
        }
        result = context.RandomSource.NextUnit() < parameters.chance;
        return true;
    }

    /// <summary>执行白名单整数比较符，未知比较符返回失败。</summary>
    private static bool TryCompare(int left, int right, string comparison, out bool result)
    {
        result = comparison switch
        {
            "eq" or "equal" or "==" => left == right,
            "ne" or "not_equal" or "!=" => left != right,
            "gt" or ">" => left > right,
            "gte" or ">=" => left >= right,
            "lt" or "<" => left < right,
            "lte" or "<=" => left <= right,
            _ => false
        };
        return comparison is "eq" or "equal" or "==" or "ne" or "not_equal" or "!=" or
            "gt" or ">" or "gte" or ">=" or "lt" or "<" or "lte" or "<=";
    }

    /// <summary>判断比较符是否属于条件注册表允许的白名单。</summary>
    private static bool IsKnownComparison(string comparison)
    {
        return comparison is "eq" or "equal" or "==" or "ne" or "not_equal" or "!=" or
            "gt" or ">" or "gte" or ">=" or "lt" or "<" or "lte" or "<=";
    }

    private delegate bool ConditionHandlerDelegate(
        ContentConditionParameters parameters,
        ContentCardExecutionContext context,
        Unit target,
        int depth,
        out bool result);

    /// <summary>把内部条件委托适配为公开处理器接口。</summary>
    private sealed class DelegateConditionHandler : IContentConditionHandler
    {
        private readonly ConditionHandlerDelegate handler;

        /// <summary>保存需要调用的条件委托。</summary>
        public DelegateConditionHandler(ConditionHandlerDelegate value)
        {
            handler = value;
        }

        /// <summary>转发条件计算并保留失败与条件 false 的区别。</summary>
        public bool TryEvaluate(ContentConditionParameters parameters, ContentCardExecutionContext context, Unit target, int depth, out bool result)
        {
            return handler(parameters, context, target, depth, out result);
        }
    }
}
}

#pragma warning restore 0649
