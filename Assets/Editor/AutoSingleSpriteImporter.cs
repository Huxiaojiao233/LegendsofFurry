using UnityEditor;

public class AutoSingleSpriteImporter : AssetPostprocessor
{
    // 只处理这个目录
    private const string TargetFolder = "Assets/Resources/ClassIMG/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(TargetFolder))
            return;

        TextureImporter importer = (TextureImporter)assetImporter;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;

        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
    }
}