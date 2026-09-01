#nullable enable
using System.Collections.Generic;

namespace LegendsOfFurry.Content.Contracts
{
/// <summary>Declares one externally supplied package layer and every definition it intentionally replaces.</summary>
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

/// <summary>Optional semantic-version bounds for one required expansion pack.</summary>
public sealed class ContentPackDependencyDefinition
{
    public string PackId { get; set; } = string.Empty;
    public string MinimumVersion { get; set; } = string.Empty;
    public string MaximumVersionExclusive { get; set; } = string.Empty;
}

/// <summary>An explicit replacement permission for one kind and stable ID.</summary>
public sealed class ContentOverrideDefinition
{
    public string DefinitionKind { get; set; } = string.Empty;
    public string DefinitionId { get; set; } = string.Empty;
}
}
