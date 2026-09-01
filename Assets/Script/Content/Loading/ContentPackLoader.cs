using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LegendsOfFurry.Content.Contracts;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>Result of composing the built-in snapshot with zero or more physical expansion packs.</summary>
public sealed class ContentLoadResult
{
    private readonly Dictionary<string, string> externalAssetPaths;

    internal ContentLoadResult(
        ContentPackage package,
        IReadOnlyList<ContentPackLoadInfo> loadedPacks,
        Dictionary<string, string> assetPaths,
        IReadOnlyList<ContentLoadDiagnostic> diagnostics,
        string fingerprint)
    {
        Package = package;
        LoadedPacks = loadedPacks;
        externalAssetPaths = assetPaths;
        Diagnostics = diagnostics;
        Fingerprint = fingerprint;
    }

    public ContentPackage Package { get; }
    public IReadOnlyList<ContentPackLoadInfo> LoadedPacks { get; }
    public IReadOnlyList<ContentLoadDiagnostic> Diagnostics { get; }
    public string Fingerprint { get; }

    /// <summary>Returns a validated absolute file path for an asset supplied by an external pack.</summary>
    public bool TryGetExternalAssetPath(string assetKey, out string path) =>
        externalAssetPaths.TryGetValue(assetKey ?? string.Empty, out path);
}

/// <summary>One physical pack search root and whether invalid content must abort startup.</summary>
public sealed class ContentPackRoot
{
    public ContentPackRoot(string directory, bool required)
    {
        Directory = directory;
        Required = required;
    }

    public string Directory { get; }
    public bool Required { get; }
}

/// <summary>Structured pack discovery or compatibility information suitable for logs and diagnostics UI.</summary>
public sealed class ContentLoadDiagnostic
{
    public ContentLoadDiagnostic(string severity, string packId, string path, string message)
    {
        Severity = severity;
        PackId = packId ?? string.Empty;
        Path = path ?? string.Empty;
        Message = message ?? string.Empty;
    }

    public string Severity { get; }
    public string PackId { get; }
    public string Path { get; }
    public string Message { get; }
}

/// <summary>Read-only diagnostics for one physical expansion pack included in the composed registry.</summary>
public sealed class ContentPackLoadInfo
{
    internal ContentPackLoadInfo(ContentPackDefinition definition, string directory, bool required, string catalogHash)
    {
        Definition = definition;
        Directory = directory;
        Required = required;
        CatalogHash = catalogHash ?? string.Empty;
    }

    public ContentPackDefinition Definition { get; }
    public string Directory { get; }
    public bool Required { get; }
    public string CatalogHash { get; }
}

/// <summary>Discovers immediate pack directories, validates their manifests and assets, then composes them.</summary>
public static class ContentPackLoader
{
    public const int SupportedPackFormatVersion = 1;
    public const string ManifestFileName = "pack.json";

    /// <summary>Loads a base published content root plus every pack found under the supplied roots.</summary>
    public static ContentLoadResult Load(string baseContentRoot, IEnumerable<string> packRoots)
    {
        return Load(baseContentRoot, (packRoots ?? Array.Empty<string>())
            .Select(path => new ContentPackRoot(path, true)), Array.Empty<string>());
    }

    /// <summary>Loads required and optional roots, skipping disabled or invalid optional packs with diagnostics.</summary>
    public static ContentLoadResult Load(
        string baseContentRoot,
        IEnumerable<ContentPackRoot> packRoots,
        IEnumerable<string> disabledPackIds)
    {
        ContentPackage basePackage = ContentPackageLoader.LoadFromDirectory(baseContentRoot);
        List<ContentLoadDiagnostic> diagnostics = new List<ContentLoadDiagnostic>();
        HashSet<string> disabled = new HashSet<string>(disabledPackIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        List<ContentPackLoadInfo> discovered = Discover(packRoots, disabled, diagnostics);
        RemoveDuplicateOptionalPacks(discovered, diagnostics);
        RemoveInvalidOptionalDependencies(discovered, diagnostics);
        IReadOnlyList<ContentPackDefinition> orderedDefinitions = ContentPackageComposer.OrderPacks(
            discovered.Select(item => item.Definition));
        Dictionary<string, ContentPackLoadInfo> infoById = discovered.ToDictionary(
            item => item.Definition.PackId, StringComparer.Ordinal);
        List<ContentPackLoadInfo> candidates = orderedDefinitions.Select(item => infoById[item.PackId]).ToList();
        List<ContentPackLoadInfo> accepted = new List<ContentPackLoadInfo>();
        Dictionary<string, string> assetPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        ContentRuntimeCapabilityValidator.ValidateOrThrow(basePackage);
        ContentPackage composed = basePackage;
        foreach (ContentPackLoadInfo candidate in candidates)
        {
            try
            {
                HashSet<string> acceptedIds = accepted.Select(item => item.Definition.PackId)
                    .ToHashSet(StringComparer.Ordinal);
                string missing = candidate.Definition.Dependencies.FirstOrDefault(item => !acceptedIds.Contains(item));
                if (!string.IsNullOrEmpty(missing))
                    throw new InvalidDataException($"扩展包 {candidate.Definition.PackId} 的依赖未成功启用：{missing}。");
                Dictionary<string, string> candidateAssets = BuildExternalAssetPaths(new[] { candidate });
                List<ContentPackDefinition> next = accepted.Select(item => item.Definition).Append(candidate.Definition).ToList();
                ContentPackage candidatePackage = ContentPackageComposer.Compose(basePackage, next);
                ContentRuntimeCapabilityValidator.ValidateOrThrow(candidatePackage);
                accepted.Add(candidate);
                composed = candidatePackage;
                foreach (KeyValuePair<string, string> asset in candidateAssets) assetPaths[asset.Key] = asset.Value;
            }
            catch (Exception exception) when (!candidate.Required)
            {
                diagnostics.Add(new ContentLoadDiagnostic("error", candidate.Definition.PackId,
                    candidate.Directory, exception.Message));
            }
        }
        return new ContentLoadResult(composed, accepted, assetPaths, diagnostics,
            ComputeFingerprint(basePackage, accepted));
    }

    /// <summary>Reads the optional user setting that lists stable pack IDs to disable.</summary>
    public static IReadOnlyCollection<string> ReadDisabledPackIds(string settingsPath)
    {
        if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath)) return Array.Empty<string>();
        ContentPackSettingsDto dto = ContentPackageLoader.ParseJson<ContentPackSettingsDto>(
            ContentPackageLoader.ReadRequiredText(settingsPath), settingsPath);
        HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
        foreach (string packId in dto.disabledPackIds ?? Array.Empty<string>())
        {
            if (!ContentId.IsValid(packId))
                throw new InvalidDataException($"扩展包启停配置包含无效 ID：{packId}。");
            result.Add(packId);
        }
        return result;
    }

    private static List<ContentPackLoadInfo> Discover(
        IEnumerable<ContentPackRoot> packRoots,
        ISet<string> disabled,
        ICollection<ContentLoadDiagnostic> diagnostics)
    {
        List<ContentPackLoadInfo> result = new List<ContentPackLoadInfo>();
        foreach (ContentPackRoot packRoot in packRoots ?? Array.Empty<ContentPackRoot>())
        {
            string rootValue = packRoot?.Directory;
            if (string.IsNullOrWhiteSpace(rootValue)) continue;
            string root = Path.GetFullPath(rootValue);
            if (!Directory.Exists(root)) continue;
            foreach (string directory in Directory.GetDirectories(root).OrderBy(item => item, StringComparer.Ordinal))
            {
                string manifestPath = Path.Combine(directory, ManifestFileName);
                if (!File.Exists(manifestPath)) continue;
                try
                {
                    ContentPackLoadInfo info = LoadPack(manifestPath, packRoot.Required);
                    if (disabled.Contains(info.Definition.PackId))
                    {
                        diagnostics.Add(new ContentLoadDiagnostic("info", info.Definition.PackId, directory,
                            "扩展包已由用户配置禁用。"));
                        continue;
                    }
                    result.Add(info);
                }
                catch (Exception exception) when (!packRoot.Required)
                {
                    diagnostics.Add(new ContentLoadDiagnostic("error", string.Empty, directory, exception.Message));
                }
            }
        }
        return result;
    }

    private static ContentPackLoadInfo LoadPack(string manifestPath, bool required)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
        ContentPackManifestDto manifest = ContentPackageLoader.ParseJson<ContentPackManifestDto>(
            ContentPackageLoader.ReadRequiredText(manifestPath), manifestPath);
        if (manifest.formatVersion != SupportedPackFormatVersion)
            throw new InvalidDataException($"扩展包格式版本不支持：{manifest.formatVersion}。");
        if (!ContentId.IsValid(manifest.packId) || string.IsNullOrWhiteSpace(manifest.packVersion))
            throw new InvalidDataException($"扩展包 manifest 的 ID 或版本无效：{manifest.packId}。");
        ContentPackage content = ContentPackageLoader.LoadCatalog(directory, manifest.catalogFile,
            manifest.catalogSha256, manifest.schemaVersion, manifest.packVersion);
        ContentPackDefinition definition = new ContentPackDefinition
        {
            PackId = manifest.packId,
            PackVersion = manifest.packVersion,
            LoadOrder = manifest.loadOrder,
            Content = content
        };
        if (manifest.dependencies != null) definition.Dependencies.AddRange(manifest.dependencies);
        if (manifest.dependencyVersions != null)
            foreach (ContentPackDependencyDefinitionDto item in manifest.dependencyVersions)
            {
                if (item == null) continue;
                ContentPackDependencyDefinition dependency = item.ToContract();
                definition.DependencyVersions.Add(dependency);
                if (!definition.Dependencies.Contains(dependency.PackId)) definition.Dependencies.Add(dependency.PackId);
            }
        if (manifest.overrides != null)
            foreach (ContentOverrideDefinitionDto item in manifest.overrides)
                if (item != null) definition.Overrides.Add(item.ToContract());
        return new ContentPackLoadInfo(definition, directory, required, manifest.catalogSha256);
    }

    private static void RemoveInvalidOptionalDependencies(
        List<ContentPackLoadInfo> packs,
        ICollection<ContentLoadDiagnostic> diagnostics)
    {
        bool changed;
        do
        {
            changed = false;
            Dictionary<string, ContentPackLoadInfo> byId = packs.ToDictionary(
                item => item.Definition.PackId, StringComparer.Ordinal);
            foreach (ContentPackLoadInfo pack in packs.ToArray())
            {
                string error = ValidateDependencies(pack, byId);
                if (string.IsNullOrEmpty(error)) continue;
                if (pack.Required) throw new InvalidDataException(error);
                packs.Remove(pack);
                diagnostics.Add(new ContentLoadDiagnostic("error", pack.Definition.PackId, pack.Directory, error));
                changed = true;
            }
        } while (changed);
    }

    private static void RemoveDuplicateOptionalPacks(
        List<ContentPackLoadInfo> packs,
        ICollection<ContentLoadDiagnostic> diagnostics)
    {
        foreach (IGrouping<string, ContentPackLoadInfo> group in packs.GroupBy(
                     item => item.Definition.PackId, StringComparer.Ordinal).Where(item => item.Count() > 1).ToArray())
        {
            ContentPackLoadInfo[] required = group.Where(item => item.Required).ToArray();
            if (required.Length > 1)
                throw new InvalidDataException($"必需扩展包 ID 重复：{group.Key}。");
            ContentPackLoadInfo keep = required.SingleOrDefault();
            if (keep == null)
            {
                foreach (ContentPackLoadInfo duplicate in group)
                {
                    packs.Remove(duplicate);
                    diagnostics.Add(new ContentLoadDiagnostic("error", group.Key, duplicate.Directory,
                        "多个可选扩展包使用了同一个 packId，已全部禁用。"));
                }
                continue;
            }
            foreach (ContentPackLoadInfo duplicate in group.Where(item => item != keep))
            {
                packs.Remove(duplicate);
                diagnostics.Add(new ContentLoadDiagnostic("error", group.Key, duplicate.Directory,
                    "可选扩展包与必需扩展包 ID 冲突，已禁用可选包。"));
            }
        }
    }

    private static string ValidateDependencies(
        ContentPackLoadInfo pack,
        IReadOnlyDictionary<string, ContentPackLoadInfo> byId)
    {
        foreach (string dependencyId in pack.Definition.Dependencies)
            if (!ContentId.IsValid(dependencyId) || !byId.ContainsKey(dependencyId))
                return $"扩展包 {pack.Definition.PackId} 缺少依赖：{dependencyId}。";
        foreach (ContentPackDependencyDefinition requirement in pack.Definition.DependencyVersions)
        {
            if (!ContentId.IsValid(requirement.PackId) || !byId.TryGetValue(requirement.PackId, out ContentPackLoadInfo dependency))
                return $"扩展包 {pack.Definition.PackId} 缺少版本依赖：{requirement.PackId}。";
            if (!VersionSatisfies(dependency.Definition.PackVersion, requirement))
                return $"扩展包 {pack.Definition.PackId} 的依赖 {requirement.PackId} 版本不兼容：{dependency.Definition.PackVersion}。";
        }
        return string.Empty;
    }

    private static bool VersionSatisfies(string value, ContentPackDependencyDefinition requirement)
    {
        if (!TryParseVersion(value, out Version actual)) return false;
        if (!string.IsNullOrWhiteSpace(requirement.MinimumVersion) &&
            (!TryParseVersion(requirement.MinimumVersion, out Version minimum) || actual < minimum)) return false;
        if (!string.IsNullOrWhiteSpace(requirement.MaximumVersionExclusive) &&
            (!TryParseVersion(requirement.MaximumVersionExclusive, out Version maximum) || actual >= maximum)) return false;
        return true;
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        string core = (value ?? string.Empty).Split('-', '+')[0];
        return Version.TryParse(core, out version);
    }

    private static string ComputeFingerprint(ContentPackage basePackage, IReadOnlyList<ContentPackLoadInfo> packs)
    {
        StringBuilder input = new StringBuilder();
        input.Append(basePackage.SchemaVersion).Append(':').Append(basePackage.ContentVersion);
        foreach (ContentPackLoadInfo pack in packs)
            input.Append('|').Append(pack.Definition.PackId).Append('@').Append(pack.Definition.PackVersion)
                .Append('#').Append(pack.CatalogHash);
        using SHA256 algorithm = SHA256.Create();
        byte[] digest = algorithm.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input.ToString()));
        return string.Concat(digest.Select(value => value.ToString("x2")));
    }

    private static Dictionary<string, string> BuildExternalAssetPaths(IReadOnlyList<ContentPackLoadInfo> packs)
    {
        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ContentPackLoadInfo pack in packs)
        {
            foreach (AssetDefinition asset in pack.Definition.Content.Assets)
            {
                string path = ContentPackageLoader.ResolveContainedPath(pack.Directory, asset.RelativePath,
                    $"扩展包 {pack.Definition.PackId} 资源");
                if (!File.Exists(path))
                    throw new FileNotFoundException($"扩展包资源不存在：{asset.AssetKey}", path);
                if (!string.IsNullOrWhiteSpace(asset.Sha256) &&
                    !string.Equals(ComputeFileSha256(path), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"扩展包资源校验失败：{asset.AssetKey}。");
                result[asset.AssetKey] = path;
            }
        }
        return result;
    }

    private static string ComputeFileSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using SHA256 algorithm = SHA256.Create();
        byte[] digest = algorithm.ComputeHash(stream);
        return string.Concat(digest.Select(value => value.ToString("x2")));
    }
}

[Serializable]
internal sealed class ContentPackSettingsDto
{
    public string[] disabledPackIds;
}
}
