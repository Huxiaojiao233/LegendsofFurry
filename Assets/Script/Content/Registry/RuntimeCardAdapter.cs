using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using LegendsOfFurry.Content.Contracts;
using UnityEngine;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 将数据库卡牌定义投影为 HandCardView 可读取的纯运行时显示数据。
/// </summary>
public static class RuntimeCardAdapter
{
    private static readonly HashSet<string> MissingArtworkWarnings = new HashSet<string>(StringComparer.Ordinal);
    private static readonly Dictionary<string, Sprite> ExternalArtworkCache =
        new Dictionary<string, Sprite>(StringComparer.Ordinal);

    /// <summary>
    /// 创建不保存为 Unity 资产的卡面视图；权威数据仍是传入的 CardDefinition。
    /// </summary>
    /// <param name="definition">数据库发布包中的卡牌定义。</param>
    /// <returns>可供现有手牌界面和费用代码读取的临时对象。</returns>
    public static CardData CreateView(CardDefinition definition)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        CardData card = CardData.Runtime(
            definition.CardId,
            definition.DisplayName,
            definition.Pools.Count > 0 ? definition.Pools[0].PoolId : string.Empty,
            definition.FamilyId,
            definition.RarityId,
            BuildCostText(definition.Cost),
            definition.Target.Range,
            ParseTargetMode(definition.Target.SelectionMode),
            definition.IsAttack,
            definition.Description,
            definition.ExhaustOnPlay,
            definition.Temporary,
            definition.Curse,
            definition.Unplayable);
        card.artwork = LoadManagedSprite(definition.ArtworkKey, "artwork");
        return card;
    }

    /// <summary>
    /// 通过已发布资源表把卡图 Key 转换为 Sprite。
    /// 卡图 Key 可以指向 artwork PNG，也可以指向含 face.png 的 .card 工程。
    /// </summary>
    /// <param name="artworkKey">卡牌定义引用的稳定资源 Key。</param>
    /// <param name="expectedKind">期望的资源类型；卡图还额外接受 kind=card。</param>
    /// <returns>成功加载的卡面 Sprite；未配置或资源缺失时返回 null。</returns>
    public static Sprite LoadManagedSprite(string artworkKey, string expectedKind = "artwork")
    {
        if (string.IsNullOrWhiteSpace(artworkKey) || !ContentRuntime.IsLoaded)
        {
            return null;
        }

        if (!ContentRuntime.Registry.TryGetAsset(artworkKey, out AssetDefinition asset) ||
            !IsUsableArtworkKind(asset, expectedKind) ||
            string.IsNullOrWhiteSpace(asset.RelativePath))
        {
            WarnMissingArtworkOnce(artworkKey, "发布包中没有对应的卡图资源记录");
            return null;
        }

        if (ExternalArtworkCache.TryGetValue(artworkKey, out Sprite cached)) return cached;

        Sprite sprite;
        if (ContentRuntime.TryGetExternalAssetPath(artworkKey, out string externalPath) &&
            TryCreateSpriteFromFile(artworkKey, externalPath, out sprite))
        {
            return sprite;
        }

        string resourcePath = ToResourcesLoadPath(asset.RelativePath);
        sprite = Resources.Load<Sprite>(resourcePath);
        if (sprite != null) return sprite;

        WarnMissingArtworkOnce(artworkKey, $"找不到卡图文件：{resourcePath}");
        return null;
    }

    /// <summary>卡牌卡图允许引用 artwork PNG 或 .card 工程；其他调用方仍按传入类型匹配。</summary>
    private static bool IsUsableArtworkKind(AssetDefinition asset, string expectedKind)
    {
        if (string.Equals(asset.AssetKind, expectedKind, StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(expectedKind, "artwork", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(asset.AssetKind, "card", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 将资源表中的新旧相对路径规范化为不含扩展名的 Resources.Load 路径。
    /// </summary>
    /// <param name="relativePath">数据库资源记录的路径。</param>
    /// <returns>Unity Resources API 可接受的路径。</returns>
    private static string ToResourcesLoadPath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        const string resourcesPrefix = "Assets/Resources/";
        if (normalized.StartsWith(resourcesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(resourcesPrefix.Length);
        }
        return Path.ChangeExtension(normalized, null).Replace('\\', '/');
    }

    /// <summary>从磁盘 PNG 或 .card（取 face.png）解码 Sprite，并写入运行时缓存。</summary>
    private static bool TryCreateSpriteFromFile(string artworkKey, string filePath, out Sprite sprite)
    {
        sprite = null;
        try
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            if (LooksLikeCardPackage(filePath) && !TryExtractFacePng(bytes, out bytes))
            {
                WarnMissingArtworkOnce(artworkKey, $"卡牌工程缺少 face.png：{filePath}");
                return false;
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.name = artworkKey;
            if (!texture.LoadImage(bytes, false))
            {
                UnityEngine.Object.Destroy(texture);
                WarnMissingArtworkOnce(artworkKey, $"无法解码图片：{filePath}");
                return false;
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f);
            sprite.name = artworkKey;
            ExternalArtworkCache[artworkKey] = sprite;
            return true;
        }
        catch (Exception exception)
        {
            WarnMissingArtworkOnce(artworkKey, $"卡图加载失败：{exception.Message}");
            return false;
        }
    }

    private static bool LooksLikeCardPackage(string filePath)
    {
        return !string.IsNullOrEmpty(filePath) &&
               filePath.EndsWith(".card", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>从 .card zip 中取出 face.png 字节。</summary>
    private static bool TryExtractFacePng(byte[] zipBytes, out byte[] png)
    {
        png = null;
        try
        {
            using MemoryStream stream = new MemoryStream(zipBytes, writable: false);
            using ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string name = Path.GetFileName(entry.FullName.Replace('\\', '/'));
                if (!string.Equals(name, "face.png", StringComparison.OrdinalIgnoreCase)) continue;
                using Stream entryStream = entry.Open();
                using MemoryStream buffer = new MemoryStream();
                entryStream.CopyTo(buffer);
                png = buffer.ToArray();
                return png.Length > 0;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// 对同一个缺失卡面只输出一次警告，避免刷新手牌时重复污染 Unity 控制台。
    /// </summary>
    /// <param name="artworkKey">缺失资源的稳定 Key。</param>
    /// <param name="reason">便于定位的缺失原因。</param>
    private static void WarnMissingArtworkOnce(string artworkKey, string reason)
    {
        if (MissingArtworkWarnings.Add(artworkKey))
        {
            Debug.LogWarning($"卡面加载失败 [{artworkKey}]：{reason}。");
        }
    }

    /// <summary>
    /// 将结构化费用转换成旧界面显示文本；结算仍读取结构化字段而非再次解析文本。
    /// </summary>
    /// <param name="cost">数据库中的行动点和法力费用。</param>
    /// <returns>与现有卡面兼容的费用文本。</returns>
    private static string BuildCostText(CardCostDefinition cost)
    {
        string action = cost.SpendAllAction ? "x" : Math.Max(0, cost.ActionCost).ToString();
        if (!cost.SpendAllMana && cost.ManaCost <= 0)
        {
            return action;
        }
        string mana = cost.SpendAllMana ? "x" : Math.Max(0, cost.ManaCost).ToString();
        return action + "+" + mana;
    }

    /// <summary>
    /// 将内容合同的目标选择模式映射为现有输入系统的四种入口。
    /// </summary>
    /// <param name="selectionMode">self、unit、cell、direction 或 none。</param>
    /// <returns>现有目标选择枚举。</returns>
    private static CardTargetMode ParseTargetMode(string selectionMode)
    {
        return selectionMode switch
        {
            "unit" => CardTargetMode.Unit,
            "cell" => CardTargetMode.AreaCell,
            "direction" => CardTargetMode.Direction,
            _ => CardTargetMode.Self
        };
    }
}
}
