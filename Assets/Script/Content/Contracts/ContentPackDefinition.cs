#nullable enable
using System.Collections.Generic;

namespace LegendsOfFurry.Content.Contracts
{
/// <summary>声明一层外部内容包，以及它明确要替换的全部定义。</summary>
public sealed class ContentPackDefinition
{
    public string PackId { get; set; } = string.Empty;
    public string PackVersion { get; set; } = string.Empty;
    public int LoadOrder { get; set; }
    public List<string> Dependencies { get; set; } = new List<string>();
    public List<ContentPackDependencyDefinition> DependencyVersions { get; set; } = new List<ContentPackDependencyDefinition>();
    public List<ContentOverrideDefinition> Overrides { get; set; } = new List<ContentOverrideDefinition>();
    public ContentPackage Content { get; set; } = new ContentPackage();
}

/// <summary>某个必需扩展包的可选语义化版本上下界。</summary>
public sealed class ContentPackDependencyDefinition
{
    public string PackId { get; set; } = string.Empty;
    public string MinimumVersion { get; set; } = string.Empty;
    public string MaximumVersionExclusive { get; set; } = string.Empty;
}

/// <summary>针对某一类型和稳定 ID 的显式替换许可。</summary>
public sealed class ContentOverrideDefinition
{
    public string DefinitionKind { get; set; } = string.Empty;
    public string DefinitionId { get; set; } = string.Empty;
}
}
