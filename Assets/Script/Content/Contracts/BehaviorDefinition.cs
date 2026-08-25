#nullable enable
using System.Collections.Generic;

namespace LegendsOfFurry.Content.Contracts
{

/// <summary>
/// 描述一个内容所有者在指定触发时机执行的行为图。
/// </summary>
public sealed class BehaviorDefinition
{
    public string BehaviorId { get; set; } = string.Empty;
    public string OwnerKind { get; set; } = "card";
    public string OwnerId { get; set; } = string.Empty;
    public string TriggerKey { get; set; } = "on_play";
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    public List<BehaviorNodeDefinition> Nodes { get; set; } = new List<BehaviorNodeDefinition>();
}

/// <summary>
/// 描述行为图中的一个顺序、条件、循环、目标遍历或效果节点。
/// </summary>
public sealed class BehaviorNodeDefinition
{
    public string NodeId { get; set; } = string.Empty;
    public string? ParentNodeId { get; set; }
    public string BranchKey { get; set; } = "children";
    public int SortOrder { get; set; }
    public string NodeKind { get; set; } = "effect";
    public string OperationKey { get; set; } = string.Empty;
    public string ParametersJson { get; set; } = "{}";
}
}
