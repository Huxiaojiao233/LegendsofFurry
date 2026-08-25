using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LegendsOfFurry.Content.Contracts;
using UnityEngine;

#pragma warning disable 0649 // Unity JsonUtility 会通过反射填充重复节点参数 DTO 字段。

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 表示一次同步行为图调度的最终状态，失败与主动取消不会被当作成功结算。
/// </summary>
public enum ContentBehaviorExecutionStatus
{
    Succeeded,
    Failed,
    Cancelled,
    AwaitingInput
}

/// <summary>
/// 隔离行为图结构调度与具体条件、目标选择器和效果实现。
/// </summary>
public interface IContentBehaviorRuntime
{
    /// <summary>
    /// 计算一个条件节点并返回布尔结果；无法识别或参数无效时返回 false。
    /// </summary>
    /// <param name="node">当前条件节点。</param>
    /// <param name="context">本次卡牌执行上下文。</param>
    /// <param name="result">成功计算后的条件结果。</param>
    /// <returns>条件节点是否成功完成计算。</returns>
    bool TryEvaluateCondition(
        BehaviorNodeDefinition node,
        ContentCardExecutionContext context,
        out bool result);

    /// <summary>
    /// 解析 foreach 节点的确定性目标集合；无法识别或参数无效时返回 false。
    /// </summary>
    /// <param name="node">当前目标遍历节点。</param>
    /// <param name="context">本次卡牌执行上下文。</param>
    /// <param name="targets">按稳定顺序返回的目标集合。</param>
    /// <returns>目标选择器是否成功完成解析。</returns>
    bool TryResolveTargets(
        BehaviorNodeDefinition node,
        ContentCardExecutionContext context,
        out IReadOnlyList<object> targets);

    /// <summary>
    /// 执行一个叶子效果节点；未知 key、参数错误或运行时对象缺失时返回 false。
    /// </summary>
    /// <param name="node">当前效果节点。</param>
    /// <param name="context">本次卡牌执行上下文。</param>
    /// <returns>效果是否成功且只结算一次。</returns>
    bool TryExecuteEffect(BehaviorNodeDefinition node, ContentCardExecutionContext context);
}

/// <summary>
/// 按稳定节点顺序解释行为树的结构节点，并把具体业务操作交给注册表运行时。
/// </summary>
public static class ContentBehaviorGraphInterpreter
{
    private static readonly ContentValueExpressionResolver RepeatValueResolver = new ContentValueExpressionResolver();
    private const int MaximumDepth = 128;
    private const int MaximumRepeatCount = 1000;

    /// <summary>
    /// 执行一个已通过发布校验的行为图；仍会防御缺根、循环、过深结构和非法重复次数。
    /// </summary>
    /// <param name="behavior">需要执行的单个 Trigger 行为图。</param>
    /// <param name="context">本次卡牌执行上下文。</param>
    /// <param name="runtime">条件、目标和效果的注册表运行时。</param>
    /// <param name="cancellationToken">战斗结束或对象销毁时使用的取消令牌。</param>
    /// <returns>成功、失败或取消状态。</returns>
    public static ContentBehaviorExecutionStatus Execute(
        BehaviorDefinition behavior,
        ContentCardExecutionContext context,
        IContentBehaviorRuntime runtime,
        CancellationToken cancellationToken = default)
    {
        if (behavior == null || context == null || runtime == null || !behavior.Enabled)
        {
            return ContentBehaviorExecutionStatus.Failed;
        }
        BehaviorNodeDefinition[] roots = behavior.Nodes
            .Where(node => string.IsNullOrEmpty(node.ParentNodeId))
            .OrderBy(node => node.SortOrder)
            .ThenBy(node => node.NodeId, StringComparer.Ordinal)
            .ToArray();
        if (roots.Length != 1)
        {
            return ContentBehaviorExecutionStatus.Failed;
        }

        Dictionary<string, BehaviorNodeDefinition[]> children = behavior.Nodes
            .Where(node => !string.IsNullOrEmpty(node.ParentNodeId))
            .GroupBy(node => node.ParentNodeId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(node => node.SortOrder)
                    .ThenBy(node => node.NodeId, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        return ExecuteNode(roots[0], context, runtime, children, cancellationToken, 0);
    }

    /// <summary>
    /// 递归调度一个结构或叶子节点，并通过深度上限防止损坏内容造成栈溢出。
    /// </summary>
    /// <param name="node">当前节点。</param>
    /// <param name="context">本次卡牌执行上下文。</param>
    /// <param name="runtime">具体操作运行时。</param>
    /// <param name="children">按父节点建立并稳定排序的子节点索引。</param>
    /// <param name="cancellationToken">外部取消令牌。</param>
    /// <param name="depth">当前递归深度。</param>
    /// <returns>当前节点及其子树的执行状态。</returns>
    private static ContentBehaviorExecutionStatus ExecuteNode(
        BehaviorNodeDefinition node,
        ContentCardExecutionContext context,
        IContentBehaviorRuntime runtime,
        IReadOnlyDictionary<string, BehaviorNodeDefinition[]> children,
        CancellationToken cancellationToken,
        int depth)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ContentBehaviorExecutionStatus.Cancelled;
        }
        if (depth > MaximumDepth)
        {
            return ContentBehaviorExecutionStatus.Failed;
        }

        switch (node.NodeKind)
        {
            case "sequence":
                return ExecuteChildren(node.NodeId, "children", context, runtime, children, cancellationToken, depth + 1);
            case "effect":
                if (!runtime.TryExecuteEffect(node, context))
                {
                    return ContentBehaviorExecutionStatus.Failed;
                }
                return context.IsAwaitingInteraction
                    ? ContentBehaviorExecutionStatus.AwaitingInput
                    : ContentBehaviorExecutionStatus.Succeeded;
            case "condition":
                if (!runtime.TryEvaluateCondition(node, context, out bool conditionResult))
                {
                    return ContentBehaviorExecutionStatus.Failed;
                }
                return ExecuteChildren(
                    node.NodeId,
                    conditionResult ? "then" : "else",
                    context,
                    runtime,
                    children,
                    cancellationToken,
                    depth + 1);
            case "repeat":
                return ExecuteRepeat(node, context, runtime, children, cancellationToken, depth + 1);
            case "foreach":
                return ExecuteForEach(node, context, runtime, children, cancellationToken, depth + 1);
            default:
                return ContentBehaviorExecutionStatus.Failed;
        }
    }

    /// <summary>
    /// 依节点稳定顺序执行指定父节点和分支下的全部直接子节点。
    /// </summary>
    /// <param name="parentNodeId">父节点 ID。</param>
    /// <param name="branchKey">需要执行的 children、then 或 else 分支。</param>
    /// <param name="context">本次卡牌执行上下文。</param>
    /// <param name="runtime">具体操作运行时。</param>
    /// <param name="children">子节点索引。</param>
    /// <param name="cancellationToken">外部取消令牌。</param>
    /// <param name="depth">下一层递归深度。</param>
    /// <returns>全部子节点成功时返回成功；首次失败或取消立即停止。</returns>
    private static ContentBehaviorExecutionStatus ExecuteChildren(
        string parentNodeId,
        string branchKey,
        ContentCardExecutionContext context,
        IContentBehaviorRuntime runtime,
        IReadOnlyDictionary<string, BehaviorNodeDefinition[]> children,
        CancellationToken cancellationToken,
        int depth)
    {
        if (!children.TryGetValue(parentNodeId, out BehaviorNodeDefinition[] directChildren))
        {
            return ContentBehaviorExecutionStatus.Succeeded;
        }
        foreach (BehaviorNodeDefinition child in directChildren.Where(item => item.BranchKey == branchKey))
        {
            ContentBehaviorExecutionStatus status = ExecuteNode(
                child, context, runtime, children, cancellationToken, depth);
            if (status != ContentBehaviorExecutionStatus.Succeeded)
            {
                return status;
            }
        }
        return ContentBehaviorExecutionStatus.Succeeded;
    }

    /// <summary>
    /// 读取受限常量次数并重复执行 children 分支，次数超过安全上限时拒绝执行。
    /// </summary>
    /// <param name="node">当前重复节点。</param>
    /// <param name="context">本次卡牌执行上下文。</param>
    /// <param name="runtime">具体操作运行时。</param>
    /// <param name="children">子节点索引。</param>
    /// <param name="cancellationToken">外部取消令牌。</param>
    /// <param name="depth">下一层递归深度。</param>
    /// <returns>全部轮次成功、失败或取消状态。</returns>
    private static ContentBehaviorExecutionStatus ExecuteRepeat(
        BehaviorNodeDefinition node,
        ContentCardExecutionContext context,
        IContentBehaviorRuntime runtime,
        IReadOnlyDictionary<string, BehaviorNodeDefinition[]> children,
        CancellationToken cancellationToken,
        int depth)
    {
        if (!ContentSafeJsonParser.TryParse(node.ParametersJson, out object parsed) ||
            parsed is not Dictionary<string, object> values ||
            !values.TryGetValue("count", out object countExpression) ||
            !RepeatValueResolver.TryResolveParsed(countExpression, context,
                context.CurrentGraphTarget as Unit ?? context.SelectedUnit ?? context.Source, out int count) ||
            count < 0 || count > MaximumRepeatCount)
        {
            return ContentBehaviorExecutionStatus.Failed;
        }
        for (int index = 0; index < count; index++)
        {
            ContentBehaviorExecutionStatus status = ExecuteChildren(
                node.NodeId, "children", context, runtime, children, cancellationToken, depth);
            if (status != ContentBehaviorExecutionStatus.Succeeded)
            {
                return status;
            }
        }
        return ContentBehaviorExecutionStatus.Succeeded;
    }

    /// <summary>
    /// 按选择器返回的稳定顺序遍历目标，并在结束或异常退出时恢复上一层当前目标。
    /// </summary>
    /// <param name="node">当前 foreach 节点。</param>
    /// <param name="context">本次卡牌执行上下文。</param>
    /// <param name="runtime">具体操作运行时。</param>
    /// <param name="children">子节点索引。</param>
    /// <param name="cancellationToken">外部取消令牌。</param>
    /// <param name="depth">下一层递归深度。</param>
    /// <returns>全部目标成功、失败或取消状态。</returns>
    private static ContentBehaviorExecutionStatus ExecuteForEach(
        BehaviorNodeDefinition node,
        ContentCardExecutionContext context,
        IContentBehaviorRuntime runtime,
        IReadOnlyDictionary<string, BehaviorNodeDefinition[]> children,
        CancellationToken cancellationToken,
        int depth)
    {
        if (!runtime.TryResolveTargets(node, context, out IReadOnlyList<object> targets) || targets == null)
        {
            return ContentBehaviorExecutionStatus.Failed;
        }
        object previousTarget = context.CurrentGraphTarget;
        try
        {
            foreach (object target in targets)
            {
                context.SetCurrentGraphTarget(target);
                ContentBehaviorExecutionStatus status = ExecuteChildren(
                    node.NodeId, "children", context, runtime, children, cancellationToken, depth);
                if (status != ContentBehaviorExecutionStatus.Succeeded)
                {
                    return status;
                }
            }
            return ContentBehaviorExecutionStatus.Succeeded;
        }
        finally
        {
            context.SetCurrentGraphTarget(previousTarget);
        }
    }

}
}

#pragma warning restore 0649
