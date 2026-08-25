#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace LegendsOfFurry.Content.Contracts
{

/// <summary>
/// 表示内容校验发现的一条可定位问题。
/// </summary>
public sealed class ValidationIssue
{
    public string Severity { get; set; } = "error";
    public string Code { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 汇总一次内容校验的全部问题和阻断状态。
/// </summary>
public sealed class ContentValidationResult
{
    public List<ValidationIssue> Issues { get; set; } = new List<ValidationIssue>();
    public bool HasErrors => Issues.Any(issue => issue.Severity == "error");
}
}
