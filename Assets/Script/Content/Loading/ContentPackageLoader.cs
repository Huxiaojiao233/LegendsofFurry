using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LegendsOfFurry.Content.Contracts;
using UnityEngine;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 从 StreamingAssets 的版本化目录读取 current、manifest 和 catalog，并在映射前验证路径与 SHA-256。
/// </summary>
public static class ContentPackageLoader
{
    public const int MinimumSchemaVersion = 1;
    public const int SupportedSchemaVersion = 2;

    /// <summary>
    /// 从当前 Unity 应用的 StreamingAssets/Content 目录加载已发布内容包。
    /// 当前同步实现面向 Windows 编辑器和 Windows 构建；其他平台将在平台适配阶段改用 UnityWebRequest。
    /// </summary>
    /// <returns>已经通过版本和校验值检查的共享内容合同。</returns>
    public static ContentPackage LoadFromStreamingAssets()
    {
        string root = Path.Combine(Application.streamingAssetsPath, "Content");
        return LoadFromDirectory(root);
    }

    /// <summary>
    /// 从指定内容根目录读取当前版本，主要供编辑器测试和构建前验证复用。
    /// </summary>
    /// <param name="contentRoot">包含 current.json 和 versions 子目录的绝对或相对路径。</param>
    /// <returns>通过完整性检查的内容包。</returns>
    public static ContentPackage LoadFromDirectory(string contentRoot)
    {
        if (string.IsNullOrWhiteSpace(contentRoot))
        {
            throw new ArgumentException("内容根目录不能为空。", nameof(contentRoot));
        }

        string root = Path.GetFullPath(contentRoot);
        string currentPath = Path.Combine(root, "current.json");
        CurrentContentPointerDto pointer = ParseJson<CurrentContentPointerDto>(ReadRequiredText(currentPath), currentPath);
        string manifestPath = ResolveContainedPath(root, pointer.manifestPath, "manifest");
        ContentManifestDto manifest = ParseJson<ContentManifestDto>(ReadRequiredText(manifestPath), manifestPath);
        if (manifest.schemaVersion < MinimumSchemaVersion || manifest.schemaVersion > SupportedSchemaVersion)
        {
            throw new InvalidDataException($"内容 schema {manifest.schemaVersion} 与运行时支持版本 {SupportedSchemaVersion} 不一致。");
        }

        if (!string.Equals(pointer.contentVersion, manifest.contentVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException("current.json 与 manifest.json 的内容版本不一致。");
        }

        return LoadCatalog(Path.GetDirectoryName(manifestPath), manifest.catalogFile,
            manifest.catalogSha256, manifest.schemaVersion, manifest.contentVersion);
    }

    /// <summary>Loads one hash-protected catalog relative to its owning manifest directory.</summary>
    internal static ContentPackage LoadCatalog(
        string ownerDirectory,
        string catalogFile,
        string expectedSha256,
        int schemaVersion,
        string contentVersion)
    {
        if (schemaVersion < MinimumSchemaVersion || schemaVersion > SupportedSchemaVersion)
            throw new InvalidDataException($"内容 schema {schemaVersion} 与运行时支持版本 {SupportedSchemaVersion} 不一致。");
        string catalogPath = ResolveContainedPath(ownerDirectory, catalogFile, "catalog");
        string catalogJson = ReadRequiredText(catalogPath);
        string actualSha256 = ComputeSha256(catalogJson);
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"内容目录校验失败：期望 {expectedSha256}，实际 {actualSha256}。");
        ContentPackageDto dto = ParseJson<ContentPackageDto>(catalogJson, catalogPath);
        if (dto.parts != null && dto.parts.Length > 0)
            dto = MergeSplitCatalog(ownerDirectory, dto);
        ContentPackage package = dto.ToContract();
        if (package.SchemaVersion != schemaVersion ||
            !string.Equals(package.ContentVersion, contentVersion, StringComparison.Ordinal))
            throw new InvalidDataException("catalog.json 的 schema 或内容版本与 manifest 不一致。");
        return package;
    }

    /// <summary>按索引加载分文件切片并合并为完整 DTO；切片哈希必须与索引声明一致。</summary>
    private static ContentPackageDto MergeSplitCatalog(string ownerDirectory, ContentPackageDto index)
    {
        ContentPackageDto merged = new ContentPackageDto
        {
            schemaVersion = index.schemaVersion,
            contentVersion = index.contentVersion
        };
        foreach (ContentCatalogPartRefDto part in index.parts)
        {
            if (part == null || string.IsNullOrWhiteSpace(part.file)) continue;
            string partPath = ResolveContainedPath(ownerDirectory, part.file, part.kind ?? "part");
            string partJson = ReadRequiredText(partPath);
            if (!string.IsNullOrWhiteSpace(part.sha256))
            {
                string actual = ComputeSha256(partJson);
                if (!string.Equals(actual, part.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"内容切片校验失败：{part.file} 期望 {part.sha256}，实际 {actual}。");
            }
            ContentPackageDto slice = ParseJson<ContentPackageDto>(partJson, partPath);
            if (slice.cards != null) merged.cards = slice.cards;
            if (slice.cardPools != null) merged.cardPools = slice.cardPools;
            if (slice.statuses != null) merged.statuses = slice.statuses;
            if (slice.decks != null) merged.decks = slice.decks;
            if (slice.rarities != null) merged.rarities = slice.rarities;
            if (slice.assets != null) merged.assets = slice.assets;
            if (slice.classProfiles != null) merged.classProfiles = slice.classProfiles;
            if (slice.characters != null) merged.characters = slice.characters;
            if (slice.equipment != null) merged.equipment = slice.equipment;
            if (slice.gameSettings != null) merged.gameSettings = slice.gameSettings;
        }
        return merged;
    }

    /// <summary>
    /// 读取必需 UTF-8 文本文件，并为缺失文件提供带绝对路径的错误。
    /// </summary>
    /// <param name="path">需要读取的文件绝对路径。</param>
    /// <returns>文件完整文本。</returns>
    internal static string ReadRequiredText(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("缺少已发布内容文件。", path);
        }

        return File.ReadAllText(path, Encoding.UTF8);
    }

    /// <summary>
    /// 使用 Unity JsonUtility 解析字段 DTO，并将空结果转成可定位的格式错误。
    /// </summary>
    /// <typeparam name="T">带 Serializable 字段的 DTO 类型。</typeparam>
    /// <param name="json">需要解析的 JSON 文本。</param>
    /// <param name="sourcePath">用于错误信息的来源文件路径。</param>
    /// <returns>解析后的 DTO。</returns>
    internal static T ParseJson<T>(string json, string sourcePath) where T : class
    {
        try
        {
            T value = JsonUtility.FromJson<T>(json);
            if (value == null)
            {
                throw new InvalidDataException($"内容文件解析结果为空：{sourcePath}");
            }
            return value;
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"内容文件不是有效 JSON：{sourcePath}", exception);
        }
    }

    /// <summary>
    /// 将相对路径解析到指定根目录，并拒绝绝对路径和目录逃逸。
    /// </summary>
    /// <param name="root">允许访问的根目录。</param>
    /// <param name="relativePath">内容文件提供的相对路径。</param>
    /// <param name="label">用于错误消息的文件类别。</param>
    /// <returns>确认位于根目录内的绝对文件路径。</returns>
    internal static string ResolveContainedPath(string root, string relativePath, string label)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"{label} 路径必须是非空相对路径。");
        }

        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string resolved = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!resolved.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{label} 路径逃逸了内容根目录：{relativePath}");
        }

        return resolved;
    }

    /// <summary>
    /// 计算 UTF-8 内容的小写十六进制 SHA-256，与桌面发布器算法保持一致。
    /// </summary>
    /// <param name="text">需要计算摘要的目录 JSON。</param>
    /// <returns>64 位小写十六进制摘要。</returns>
    internal static string ComputeSha256(string text)
    {
        using (SHA256 algorithm = SHA256.Create())
        {
            byte[] digest = algorithm.ComputeHash(Encoding.UTF8.GetBytes(text));
            StringBuilder builder = new StringBuilder(digest.Length * 2);
            foreach (byte value in digest)
            {
                builder.Append(value.ToString("x2"));
            }
            return builder.ToString();
        }
    }
}
}
