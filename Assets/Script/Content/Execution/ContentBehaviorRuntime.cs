using System;
using System.Collections.Generic;
using System.Linq;
using LegendsOfFurry.Content.Contracts;
using UnityEngine;

#pragma warning disable 0649 // Unity JsonUtility 会通过反射填充预检 DTO 字段。

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 把行为图解释器连接到条件、目标选择器和基础效果注册表，是正式 OnPlay 图的运行时适配层。
/// </summary>
public sealed class ContentBehaviorRuntime : IContentBehaviorRuntime
{
    private static readonly ContentValueExpressionResolver RepeatValueResolver = new ContentValueExpressionResolver();
    private readonly ContentConditionResolver conditions;
    private readonly ContentTargetSelectorResolver targets;

    /// <summary>
    /// 创建正式行为运行时，并允许测试注入独立的条件或目标注册表。
    /// </summary>
    /// <param name="conditionResolver">可选条件注册表。</param>
    /// <param name="targetResolver">可选目标选择器注册表。</param>
    public ContentBehaviorRuntime(
        ContentConditionResolver conditionResolver = null,
        ContentTargetSelectorResolver targetResolver = null)
    {
        conditions = conditionResolver ?? new ContentConditionResolver();
        targets = targetResolver ?? new ContentTargetSelectorResolver();
    }

    /// <summary>
    /// 在扣费前验证根节点、父子分支、结构节点和所有操作 key 均可由当前版本执行。
    /// </summary>
    /// <param name="behavior">需要预检的单个行为图。</param>
    /// <returns>整棵图结构和能力均受支持时返回 true。</returns>
    public bool CanExecuteBehavior(BehaviorDefinition behavior)
    {
        if (behavior == null || !behavior.Enabled || behavior.Nodes == null)
        {
            return false;
        }
        BehaviorNodeDefinition[] roots = behavior.Nodes
            .Where(node => string.IsNullOrEmpty(node.ParentNodeId))
            .ToArray();
        if (roots.Length != 1)
        {
            return false;
        }
        Dictionary<string, BehaviorNodeDefinition> nodes = new Dictionary<string, BehaviorNodeDefinition>(StringComparer.Ordinal);
        foreach (BehaviorNodeDefinition node in behavior.Nodes)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.NodeId) || !nodes.TryAdd(node.NodeId, node))
            {
                return false;
            }
        }
        foreach (BehaviorNodeDefinition node in behavior.Nodes)
        {
            if (!CanExecuteNode(node) || HasInvalidParentOrBranch(node, nodes) || HasParentCycle(node, nodes))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 使用条件注册表计算一个 Condition 节点。
    /// </summary>
    /// <param name="node">当前条件节点。</param>
    /// <param name="context">本次卡牌执行上下文。</param>
    /// <param name="result">成功计算后的真假结果。</param>
    /// <returns>条件 key 和参数可执行时返回 true。</returns>
    public bool TryEvaluateCondition(BehaviorNodeDefinition node, ContentCardExecutionContext context, out bool result)
    {
        return conditions.TryEvaluate(node, context, out result);
    }

    /// <summary>
    /// 使用目标选择器注册表计算一个 ForEach 节点的稳定目标集合。
    /// </summary>
    /// <param name="node">当前目标遍历节点。</param>
    /// <param name="context">本次卡牌执行上下文。</param>
    /// <param name="resolvedTargets">稳定目标集合。</param>
    /// <returns>选择器和查询服务可用时返回 true。</returns>
    public bool TryResolveTargets(
        BehaviorNodeDefinition node,
        ContentCardExecutionContext context,
        out IReadOnlyList<object> resolvedTargets)
    {
        return targets.TryResolve(node, context, out resolvedTargets);
    }

    /// <summary>
    /// 把叶子效果交给当前基础效果注册路径执行。
    /// </summary>
    /// <param name="node">当前效果节点。</param>
    /// <param name="context">本次卡牌执行上下文。</param>
    /// <returns>效果成功且只结算一次时返回 true。</returns>
    public bool TryExecuteEffect(BehaviorNodeDefinition node, ContentCardExecutionContext context)
    {
        bool succeeded = ContentCardEffectExecutor.TryExecuteEffect(node, context);
        context?.RecordCombatLog(node, succeeded);
        return succeeded;
    }

    /// <summary>检查单个节点类别、操作 key 和基础参数是否受当前运行时支持。</summary>
    private bool CanExecuteNode(BehaviorNodeDefinition node)
    {
        switch (node.NodeKind)
        {
            case "sequence":
                return string.IsNullOrEmpty(node.OperationKey) || node.OperationKey == "sequence";
            case "effect":
                return ContentCapabilityCatalog.Phase3ExecutableEffectKeys.Contains(node.OperationKey) &&
                       ContentCardEffectExecutor.CanExecuteEffectParameters(node);
            case "condition":
                return conditions.CanEvaluateNode(node);
            case "foreach":
                return targets.CanResolveNode(node);
            case "repeat":
                return (string.IsNullOrEmpty(node.OperationKey) || node.OperationKey == "repeat") &&
                       HasValidRepeatExpression(node.ParametersJson);
            default:
                return false;
        }
    }

    /// <summary>检查父节点存在且 BranchKey 符合父节点类别约定。</summary>
    private static bool HasInvalidParentOrBranch(
        BehaviorNodeDefinition node,
        IReadOnlyDictionary<string, BehaviorNodeDefinition> nodes)
    {
        if (string.IsNullOrEmpty(node.ParentNodeId))
        {
            return false;
        }
        if (!nodes.TryGetValue(node.ParentNodeId, out BehaviorNodeDefinition parent) || parent.NodeKind == "effect")
        {
            return true;
        }
        return parent.NodeKind == "condition"
            ? node.BranchKey != "then" && node.BranchKey != "else"
            : node.BranchKey != "children";
    }

    /// <summary>沿父链检查循环，防止损坏内容绕过递归深度保护。</summary>
    private static bool HasParentCycle(
        BehaviorNodeDefinition node,
        IReadOnlyDictionary<string, BehaviorNodeDefinition> nodes)
    {
        HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal) { node.NodeId };
        string parentId = node.ParentNodeId;
        while (!string.IsNullOrEmpty(parentId))
        {
            if (!visited.Add(parentId))
            {
                return true;
            }
            if (!nodes.TryGetValue(parentId, out BehaviorNodeDefinition parent))
            {
                return true;
            }
            parentId = parent.ParentNodeId;
        }
        return false;
    }

    /// <summary>验证 Repeat 次数为 0 到 1000 的常量表达式。</summary>
    private static bool HasValidRepeatExpression(string json)
    {
        return ContentSafeJsonParser.TryParse(json, out object parsed) &&
               parsed is Dictionary<string, object> values &&
               values.TryGetValue("count", out object count) &&
               RepeatValueResolver.CanResolveParsedStructure(count);
    }
}
}

#pragma warning restore 0649
