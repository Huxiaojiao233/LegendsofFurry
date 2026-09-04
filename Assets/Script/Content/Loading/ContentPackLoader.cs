using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LegendsOfFurry.Content.Contracts;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>把内置快照与零个或多个实体扩展包合成后的结果。</summary>
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

    /// <summary>返回外部包提供资源的、已经校验过的绝对文件路径。</summary>
    public bool TryGetExternalAssetPath(string assetKey, out string path) =>
        externalAssetPaths.TryGetValue(assetKey ?? string.Empty, out path);
}

/// <summary>一个实体包搜索根目录，以及无效内容是否必须中止启动。</summary>
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

/// <summary>结构化的包发现或兼容性信息，适合写入日志和诊断界面。</summary>
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

/// <summary>已纳入合成注册表的某个实体扩展包的只读诊断信息。</summary>
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

/// <summary>发现直接子目录中的内容包，校验清单和资源，再把它们合成。</summary>
public static class ContentPackLoader
{
    public const int SupportedPackFormatVersion = 2;
    public const string ManifestFileName = "pack.json";
    public const string PackageExtension = ".lofepackage";

    /// <summary>加载一份已发布的基础内容根目录，以及各搜索根下发现的全部扩展包。</summary>
    public static ContentLoadResult Load(string baseContentRoot, IEnumerable<string> packRoots)
    {
        return Load(baseContentRoot, (packRoots ?? Array.Empty<string>())
            .Select(path => new ContentPackRoot(path, true)), Array.Empty<string>());
    }

    /// <summary>加载必需和可选根目录；停用或无效的可选包会跳过并写入诊断。</summary>
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

    /// <summary>读取可选用户配置中需要停用的稳定包 ID 列表。</summary>
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
            foreach (string packageFile in Directory.GetFiles(root, "*" + PackageExtension)
                         .OrderBy(item => item, StringComparer.Ordinal))
            {
                string fileName = Path.GetFileName(packageFile);
                if (string.IsNullOrEmpty(fileName) || fileName.StartsWith("_", StringComparison.Ordinal))
                    continue;
#if !UNITY_EDITOR
                if (string.Equals(Path.GetFileNameWithoutExtension(fileName), "planner_pack", StringComparison.OrdinalIgnoreCase))
                    continue;
#endif
                TryAddDiscovered(result, diagnostics, disabled, packRoot.Required, packageFile, () =>
                    LoadPack(Path.Combine(ExtractPackage(packageFile), ManifestFileName), packRoot.Required));
            }
            foreach (string directory in Directory.GetDirectories(root).OrderBy(item => item, StringComparer.Ordinal))
            {
                string folderName = Path.GetFileName(directory);
                if (string.IsNullOrEmpty(folderName) || folderName.StartsWith("_", StringComparison.Ordinal))
                    continue;
#if !UNITY_EDITOR
                if (string.Equals(folderName, "planner_pack", StringComparison.OrdinalIgnoreCase))
                    continue;
#endif
                string manifestPath = Path.Combine(directory, ManifestFileName);
                if (!File.Exists(manifestPath)) continue;
                TryAddDiscovered(result, diagnostics, disabled, packRoot.Required, directory, () =>
                    LoadPack(manifestPath, packRoot.Required));
            }
        }
        return result;
    }

    private static void TryAddDiscovered(
        List<ContentPackLoadInfo> result,
        ICollection<ContentLoadDiagnostic> diagnostics,
        ISet<string> disabled,
        bool required,
        string sourcePath,
        Func<ContentPackLoadInfo> load)
    {
        try
        {
            ContentPackLoadInfo info = load();
            if (disabled.Contains(info.Definition.PackId))
            {
                diagnostics.Add(new ContentLoadDiagnostic("info", info.Definition.PackId, sourcePath,
                    "扩展包已由用户配置禁用。"));
                return;
            }
            result.Add(info);
        }
        catch (Exception exception) when (!required)
        {
            diagnostics.Add(new ContentLoadDiagnostic("error", string.Empty, sourcePath, exception.Message));
        }
    }

    /// <summary>把 .lofepackage 解到缓存目录；文件哈希变了会重新解压。</summary>
    internal static string ExtractPackage(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            throw new FileNotFoundException("找不到内容包。", packagePath);
        string hash = ComputeFileSha256(packagePath);
        string cacheRoot = Path.Combine(Path.GetTempPath(), "LofePackages", hash);
        string manifestPath = Path.Combine(cacheRoot, ManifestFileName);
        if (File.Exists(manifestPath)) return cacheRoot;
        if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, true);
        Directory.CreateDirectory(cacheRoot);
        ExtractZipArchive(packagePath, cacheRoot);
        if (!File.Exists(manifestPath))
            throw new InvalidDataException($"内容包缺少 {ManifestFileName}：{packagePath}");
        return cacheRoot;
    }

    private static void ExtractZipArchive(string packagePath, string destination)
    {
        using FileStream stream = File.OpenRead(packagePath);
        using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            string relative = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrEmpty(relative) || relative.Contains("..", StringComparison.Ordinal))
                throw new InvalidDataException($"内容包包含非法路径：{entry.FullName}");
            string target = Path.GetFullPath(Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar)));
            string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"内容包路径逃逸：{entry.FullName}");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            using Stream source = entry.Open();
            using FileStream output = File.Create(target);
            source.CopyTo(output);
        }
    }

    private static ContentPackLoadInfo LoadPack(string manifestPath, bool required)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
        ContentPackManifestDto manifest = ContentPackageLoader.ParseJson<ContentPackManifestDto>(
            ContentPackageLoader.ReadRequiredText(manifestPath), manifestPath);
        if (manifest.formatVersion < 1 || manifest.formatVersion > SupportedPackFormatVersion)
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
                result[asset.AssetKey] = path;
            }
        }
        return result;
    }

    /// <summary>构建门禁校验单张资源文件的 SHA-256；运行时启动不再整包哈希，避免闪屏后主线程卡死。</summary>
    public static string ComputeFileSha256(string path)
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
