using System;
using System.Linq;

#pragma warning disable 0649 // 表达式字段由安全 JSON 映射填充。

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 为概率和随机范围表达式提供可注入随机源，使战斗测试与回放不依赖 Unity 全局随机状态。
/// </summary>
public interface IContentRandomSource
{
    /// <summary>
    /// 返回包含上下界的随机整数。
    /// </summary>
    /// <param name="minimum">最小可能值。</param>
    /// <param name="maximum">最大可能值。</param>
    /// <returns>位于闭区间内的随机整数。</returns>
    int NextInclusive(int minimum, int maximum);

    /// <summary>
    /// 返回大于等于零且小于一的随机小数。
    /// </summary>
    /// <returns>[0,1) 区间随机值。</returns>
    float NextUnit();
}

/// <summary>
/// 描述一个受控整数表达式树；只允许注册表中的 kind，不接受任意代码或公式字符串。
/// </summary>
public sealed class ContentValueExpression
{
    public string kind;
    public int value;
    public string target;
    public string statusId;
    public string runtimeKey;
    public ContentValueExpression left;
    public ContentValueExpression right;
    public ContentValueExpression minimum;
    public ContentValueExpression maximum;
}

/// <summary>
/// 计算一个具体表达式 key；处理器必须显式报告失败，不能用默认零掩盖坏数据。
/// </summary>
public interface IContentValueExpressionHandler
{
    /// <summary>
    /// 计算当前表达式节点。
    /// </summary>
    /// <param name="expression">当前表达式节点。</param>
    /// <param name="context">本次卡牌执行上下文。</param>
    /// <param name="target">效果当前作用单位。</param>
    /// <param name="depth">当前递归深度。</param>
    /// <param name="value">成功计算的整数值。</param>
    /// <returns>参数和运行时状态均有效时返回 true。</returns>
    bool TryResolve(
        ContentValueExpression expression,
        ContentCardExecutionContext context,
        Unit target,
        int depth,
        out int value);
}

/// <summary>
/// 通过白名单注册表计算首期整数表达式，并对未知 key、溢出、除零和过深递归返回失败。
/// </summary>
public sealed class ContentValueExpressionResolver
{
    private const int MaximumDepth = 64;
    private readonly ContentOperationRegistry<IContentValueExpressionHandler> registry =
        new ContentOperationRegistry<IContentValueExpressionHandler>();

    /// <summary>
    /// 创建并注册当前版本所有已实现的整数表达式处理器。
    /// </summary>
    public ContentValueExpressionResolver()
    {
        Register("constant", ResolveConstant);
        Register("source_max_health", ResolveSourceMaxHealth);
        Register("source_current_health", ResolveSourceCurrentHealth);
        Register("target_max_health", ResolveTargetMaxHealth);
        Register("target_current_health", ResolveTargetCurrentHealth);
        Register("source_armor", ResolveSourceArmor);
        Register("target_armor", ResolveTargetArmor);
        Register("status_stacks", ResolveStatusStacks);
        Register("owner_status_stacks", ResolveOwnerStatusStacks);
        Register("card_runtime_value", ResolveCardRuntimeValue);
        Register("spent_action_points", ResolveSpentActionPoints);
        Register("spent_mana", ResolveSpentMana);
        Register("damage_dealt", ResolveDamageDealt);
        Register("health_damage_dealt", ResolveHealthDamageDealt);
        Register("random_range", ResolveRandomRange);
        Register("add", ResolveAdd);
        Register("subtract", ResolveSubtract);
        Register("multiply", ResolveMultiply);
        Register("divide", ResolveDivide);
        Register("min", ResolveMinimum);
        Register("max", ResolveMaximum);
    }

    /// <summary>
    /// 从 JSON 解析并计算一个受控表达式树。
    /// </summary>
    /// <param name="json">表达式 JSON 对象。</param>
    /// <param name="context">本次卡牌执行上下文。</param>
    /// <param name="target">效果当前作用单位。</param>
    /// <param name="value">成功计算的整数值。</param>
    /// <returns>JSON、key、参数和运行时状态全部有效时返回 true。</returns>
    public bool TryResolveJson(string json, ContentCardExecutionContext context, Unit target, out int value)
    {
        value = 0;
        return ContentSafeJsonParser.TryParse(json, out object parsed) &&
               TryMapExpression(parsed, 0, out ContentValueExpression expression) &&
               TryResolve(expression, context, target, out value);
    }

    /// <summary>
    /// 计算安全 JSON 解析器已经生成的表达式对象，供效果参数读取器避免重复解析整段 JSON。
    /// </summary>
    internal bool TryResolveParsed(object parsed, ContentCardExecutionContext context, Unit target, out int value)
    {
        value = 0;
        return TryMapExpression(parsed, 0, out ContentValueExpression expression) &&
               TryResolve(expression, context, target, out value);
    }

    /// <summary>
    /// 把安全 JSON 解析器已经生成的对象映射为表达式树，供条件参数读取嵌套 left/right/value。
    /// </summary>
    internal static bool TryMapFromParsed(object parsed, out ContentValueExpression expression)
    {
        return TryMapExpression(parsed, 0, out expression);
    }

    /// <summary>
    /// 验证安全 JSON 解析器已经生成的表达式对象是否只包含已注册结构。
    /// </summary>
    internal bool CanResolveParsedStructure(object parsed)
    {
        return TryMapExpression(parsed, 0, out ContentValueExpression expression) &&
               CanResolveStructure(expression);
    }

    /// <summary>
    /// 计算已经映射的表达式对象。
    /// </summary>
    /// <param name="expression">表达式根节点。</param>
    /// <param name="context">本次卡牌执行上下文。</param>
    /// <param name="target">效果当前作用单位。</param>
    /// <param name="value">成功计算的整数值。</param>
    /// <returns>完整表达式可计算时返回 true。</returns>
    public bool TryResolve(
        ContentValueExpression expression,
        ContentCardExecutionContext context,
        Unit target,
        out int value)
    {
        return TryResolveNested(expression, context, target, 0, out value);
    }

    /// <summary>
    /// 判断当前运行时是否拥有指定表达式处理器，供内容启动预检使用。
    /// </summary>
    /// <param name="key">表达式 kind。</param>
    /// <returns>已注册时返回 true。</returns>
    public bool Contains(string key)
    {
        return registry.Contains(key);
    }

    /// <summary>
    /// 在不读取战斗状态的前提下验证表达式树的 key、必需子节点和最大深度。
    /// </summary>
    /// <param name="expression">需要预检的表达式根节点。</param>
    /// <returns>结构完整且所有 kind 已注册时返回 true。</returns>
    public bool CanResolveStructure(ContentValueExpression expression)
    {
        return CanResolveStructure(expression, 0);
    }

    /// <summary>
    /// 取得全部已注册表达式 key 的只读有序快照。
    /// </summary>
    /// <returns>注册 key 列表。</returns>
    public System.Collections.Generic.IReadOnlyList<string> GetRegisteredKeys()
    {
        return registry.GetRegisteredKeys();
    }

    /// <summary>
    /// 注册一个委托表达式处理器，并通过适配对象加入通用操作注册表。
    /// </summary>
    /// <param name="key">稳定表达式 key。</param>
    /// <param name="handler">具体计算委托。</param>
    private void Register(string key, ExpressionHandlerDelegate handler)
    {
        registry.Register(key, new DelegateExpressionHandler(handler));
    }

    /// <summary>
    /// 执行递归节点查询，并统一限制最大表达式深度。
    /// </summary>
    /// <param name="expression">当前表达式节点。</param>
    /// <param name="context">本次执行上下文。</param>
    /// <param name="target">当前目标。</param>
    /// <param name="depth">当前递归深度。</param>
    /// <param name="value">计算值。</param>
    /// <returns>节点存在、深度安全且处理器成功时返回 true。</returns>
    private bool TryResolveNested(
        ContentValueExpression expression,
        ContentCardExecutionContext context,
        Unit target,
        int depth,
        out int value)
    {
        value = 0;
        return expression != null && context != null && depth <= MaximumDepth &&
               registry.TryGet(expression.kind, out IContentValueExpressionHandler handler) &&
               handler.TryResolve(expression, context, target, depth, out value);
    }

    /// <summary>递归验证表达式结构，并拒绝未知 key 与过深树。</summary>
    private bool CanResolveStructure(ContentValueExpression expression, int depth)
    {
        if (expression == null || depth > MaximumDepth || !registry.Contains(expression.kind))
        {
            return false;
        }
        switch (expression.kind)
        {
            case "add":
            case "subtract":
            case "multiply":
            case "divide":
            case "min":
            case "max":
                return CanResolveStructure(expression.left, depth + 1) &&
                       CanResolveStructure(expression.right, depth + 1);
            case "random_range":
                return CanResolveStructure(expression.minimum, depth + 1) &&
                       CanResolveStructure(expression.maximum, depth + 1);
            case "status_stacks":
                return !string.IsNullOrWhiteSpace(expression.statusId);
            case "card_runtime_value":
                return !string.IsNullOrWhiteSpace(expression.runtimeKey);
            default:
                return true;
        }
    }

    /// <summary>把安全 JSON 解析器产生的纯对象递归映射为受控表达式 DTO。</summary>
    private static bool TryMapExpression(object parsed, int depth, out ContentValueExpression expression)
    {
        expression = null;
        if (depth > MaximumDepth || parsed is not System.Collections.Generic.Dictionary<string, object> values ||
            !TryGetString(values, "kind", out string kind))
        {
            return false;
        }
        expression = new ContentValueExpression { kind = kind };
        if (values.TryGetValue("value", out object rawValue) && !TryGetInt(rawValue, out expression.value)) return false;
        TryGetString(values, "target", out expression.target);
        TryGetString(values, "statusId", out expression.statusId);
        TryGetString(values, "runtimeKey", out expression.runtimeKey);
        if (!TryMapOptionalChild(values, "left", depth, out expression.left) ||
            !TryMapOptionalChild(values, "right", depth, out expression.right) ||
            !TryMapOptionalChild(values, "minimum", depth, out expression.minimum) ||
            !TryMapOptionalChild(values, "maximum", depth, out expression.maximum))
            return false;
        return true;
    }

    /// <summary>映射一个可选子表达式；字段缺失时成功返回 null。</summary>
    private static bool TryMapOptionalChild(
        System.Collections.Generic.IReadOnlyDictionary<string, object> values,
        string key,
        int depth,
        out ContentValueExpression child)
    {
        child = null;
        return !values.TryGetValue(key, out object parsed) || TryMapExpression(parsed, depth + 1, out child);
    }

    /// <summary>从 JSON 对象读取可选字符串字段。</summary>
    private static bool TryGetString(
        System.Collections.Generic.IReadOnlyDictionary<string, object> values,
        string key,
        out string value)
    {
        value = null;
        if (!values.TryGetValue(key, out object parsed)) return false;
        value = parsed as string;
        return value != null;
    }

    /// <summary>把 JSON 整数数值安全转换为 Int32。</summary>
    private static bool TryGetInt(object parsed, out int value)
    {
        value = 0;
        if (parsed is long integer && integer >= int.MinValue && integer <= int.MaxValue)
        {
            value = (int)integer;
            return true;
        }
        if (parsed is double number && number >= int.MinValue && number <= int.MaxValue && Math.Truncate(number) == number)
        {
            value = (int)number;
            return true;
        }
        return false;
    }

    /// <summary>返回常量节点的整数值。</summary>
    private bool ResolveConstant(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        value = expression.value;
        return true;
    }

    /// <summary>读取施放者最大生命。</summary>
    private bool ResolveSourceMaxHealth(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        value = context.Source == null ? 0 : context.Source.MaxHealth;
        return context.Source != null;
    }

    /// <summary>读取施放者当前生命。</summary>
    private bool ResolveSourceCurrentHealth(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        value = context.Source == null ? 0 : context.Source.CurrentHealth;
        return context.Source != null;
    }

    /// <summary>读取当前效果目标最大生命。</summary>
    private bool ResolveTargetMaxHealth(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        value = target == null ? 0 : target.MaxHealth;
        return target != null;
    }

    /// <summary>读取当前效果目标当前生命。</summary>
    private bool ResolveTargetCurrentHealth(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        value = target == null ? 0 : target.CurrentHealth;
        return target != null;
    }

    /// <summary>读取施放者当前护甲。</summary>
    private bool ResolveSourceArmor(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        value = context.Source == null ? 0 : context.Source.Armor;
        return context.Source != null;
    }

    /// <summary>读取当前效果目标护甲。</summary>
    private bool ResolveTargetArmor(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        value = target == null ? 0 : target.Armor;
        return target != null;
    }

    /// <summary>按目标选择和旧状态 ID 读取当前状态层数。</summary>
    private bool ResolveStatusStacks(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        Unit owner = SelectUnit(expression.target, context, target);
        value = 0;
        return owner != null && !string.IsNullOrWhiteSpace(expression.statusId) &&
               AssignValue(owner.State.Get(expression.statusId), out value);
    }

    /// <summary>Reads the stacks of the status instance that owns the currently executing modifier graph.</summary>
    private bool ResolveOwnerStatusStacks(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        value = context.Owner?.RuntimeInstance is RuntimeStatusInstance instance ? instance.Stacks : 0;
        return context.Owner?.RuntimeInstance is RuntimeStatusInstance;
    }

    /// <summary>读取当前卡牌实例的持久运行时整数，供蓄力类卡跨次打出累积数值。</summary>
    private bool ResolveCardRuntimeValue(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        value = 0;
        return context.Card != null && !string.IsNullOrWhiteSpace(expression.runtimeKey) &&
               AssignValue(context.Card.GetRuntimeValue(expression.runtimeKey), out value);
    }

    /// <summary>读取本次实际消耗行动点快照。</summary>
    private bool ResolveSpentActionPoints(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        value = context.Resources.SpentActionPoints;
        return true;
    }

    /// <summary>读取本次实际消耗法力快照。</summary>
    private bool ResolveSpentMana(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        value = context.Resources.SpentMana;
        return true;
    }

    /// <summary>汇总本卡已记录的护甲吸收与生命伤害。</summary>
    private bool ResolveDamageDealt(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        return TrySum(context.DamageRecords.Select(record => record.AbsorbedByArmor + record.HealthDamage), out value);
    }

    /// <summary>汇总本卡已记录的实际生命伤害。</summary>
    private bool ResolveHealthDamageDealt(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        return TrySum(context.DamageRecords.Select(record => record.HealthDamage), out value);
    }

    /// <summary>使用注入随机源计算包含上下界的整数随机范围。</summary>
    private bool ResolveRandomRange(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        value = 0;
        if (!TryResolveNested(expression.minimum, context, target, depth + 1, out int minimum) ||
            !TryResolveNested(expression.maximum, context, target, depth + 1, out int maximum) ||
            minimum > maximum || context.RandomSource == null)
        {
            return false;
        }
        value = context.RandomSource.NextInclusive(minimum, maximum);
        return value >= minimum && value <= maximum;
    }

    /// <summary>安全相加两个子表达式。</summary>
    private bool ResolveAdd(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        return TryBinary(expression, context, target, depth, (left, right) => checked(left + right), out value);
    }

    /// <summary>安全相减两个子表达式。</summary>
    private bool ResolveSubtract(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        return TryBinary(expression, context, target, depth, (left, right) => checked(left - right), out value);
    }

    /// <summary>安全相乘两个子表达式。</summary>
    private bool ResolveMultiply(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        return TryBinary(expression, context, target, depth, (left, right) => checked(left * right), out value);
    }

    /// <summary>执行整数除法并拒绝除数为零。</summary>
    private bool ResolveDivide(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        value = 0;
        if (!TryResolvePair(expression, context, target, depth, out int left, out int right) || right == 0)
        {
            return false;
        }
        value = left / right;
        return true;
    }

    /// <summary>返回两个子表达式的较小值。</summary>
    private bool ResolveMinimum(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        return TryBinary(expression, context, target, depth, Math.Min, out value);
    }

    /// <summary>返回两个子表达式的较大值。</summary>
    private bool ResolveMaximum(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
    {
        return TryBinary(expression, context, target, depth, Math.Max, out value);
    }

    /// <summary>
    /// 解析两个子表达式并执行可能溢出的整数运算。
    /// </summary>
    private bool TryBinary(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, Func<int, int, int> operation, out int value)
    {
        value = 0;
        if (!TryResolvePair(expression, context, target, depth, out int left, out int right))
        {
            return false;
        }
        try
        {
            value = operation(left, right);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>解析二元表达式左右两个直接子节点。</summary>
    private bool TryResolvePair(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int left, out int right)
    {
        left = 0;
        right = 0;
        return TryResolveNested(expression.left, context, target, depth + 1, out left) &&
               TryResolveNested(expression.right, context, target, depth + 1, out right);
    }

    /// <summary>按 self、selected_unit 或 current_graph_target 选择状态拥有者。</summary>
    private static Unit SelectUnit(string selector, ContentCardExecutionContext context, Unit fallback)
    {
        return selector switch
        {
            "self" => context.Source,
            "selected_unit" => context.SelectedUnit,
            "current_graph_target" => context.CurrentGraphTarget as Unit,
            _ => fallback
        };
    }

    /// <summary>在布尔表达式中赋值一个整数输出，减少状态层数处理器的重复分支。</summary>
    private static bool AssignValue(int source, out int value)
    {
        value = source;
        return true;
    }

    /// <summary>使用 checked 汇总整数序列并在溢出时报告失败。</summary>
    private static bool TrySum(System.Collections.Generic.IEnumerable<int> values, out int result)
    {
        result = 0;
        try
        {
            foreach (int value in values)
            {
                result = checked(result + value);
            }
            return true;
        }
        catch (OverflowException)
        {
            result = 0;
            return false;
        }
    }

    private delegate bool ExpressionHandlerDelegate(
        ContentValueExpression expression,
        ContentCardExecutionContext context,
        Unit target,
        int depth,
        out int value);

    /// <summary>把内部强类型委托适配为公开表达式处理器接口。</summary>
    private sealed class DelegateExpressionHandler : IContentValueExpressionHandler
    {
        private readonly ExpressionHandlerDelegate handler;

        /// <summary>保存需要调用的表达式处理委托。</summary>
        public DelegateExpressionHandler(ExpressionHandlerDelegate value)
        {
            handler = value;
        }

        /// <summary>转发表达式计算并原样返回成功状态和值。</summary>
        public bool TryResolve(ContentValueExpression expression, ContentCardExecutionContext context, Unit target, int depth, out int value)
        {
            return handler(expression, context, target, depth, out value);
        }
    }
}

/// <summary>
/// 默认 Unity 随机源，仅用于真实战斗；测试和回放应向上下文注入确定性实现。
/// </summary>
public sealed class UnityContentRandomSource : IContentRandomSource
{
    public static UnityContentRandomSource Instance { get; } = new UnityContentRandomSource();

    /// <summary>阻止外部创建额外默认随机源实例。</summary>
    private UnityContentRandomSource()
    {
    }

    /// <summary>使用 Unity 随机状态返回包含上下界的整数。</summary>
    public int NextInclusive(int minimum, int maximum)
    {
        long span = (long)maximum - minimum + 1L;
        double unit = Math.Min(UnityEngine.Random.value, 0.9999999403953552d);
        return (int)(minimum + (long)Math.Floor(unit * span));
    }

    /// <summary>使用 Unity 随机状态返回 [0,1) 小数。</summary>
    public float NextUnit()
    {
        return UnityEngine.Random.value;
    }
}
}

#pragma warning restore 0649
