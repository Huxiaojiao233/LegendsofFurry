#if UNITY_EDITOR
using System;
using UnityEditor;

/// <summary>
/// 统一卡牌维护工具上传目录与资源库 PNG 的纹理导入设置，确保运行时可以按 Sprite 读取卡面。
/// </summary>
public sealed class CardArtworkImporter : AssetPostprocessor
{
    private const string ArtworkDirectory = "Assets/Resources/CardArt/";
    private const string LibraryDirectory = "Assets/Resources/Library/";

    /// <summary>
    /// 在 Unity 导入上传卡面之前强制使用单张 Sprite 设置，并关闭不需要的贴图功能。
    /// </summary>
    private void OnPreprocessTexture()
    {
        string normalizedPath = assetPath.Replace('\\', '/');
        if (!normalizedPath.StartsWith(ArtworkDirectory, StringComparison.OrdinalIgnoreCase) &&
            !normalizedPath.StartsWith(LibraryDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Compressed;
    }
}
#endif
