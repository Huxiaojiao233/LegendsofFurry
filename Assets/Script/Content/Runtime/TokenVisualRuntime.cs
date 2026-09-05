using UnityEngine;
using LegendsOfFurry.Content.Contracts;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 把内容包里的角色贴图和外框纯色刷到运行时生成的棋子上。
/// 不使用工程内 Tokens 材质，始终创建 URP 运行时材质。
/// </summary>
public static class TokenVisualRuntime
{
    private const string RuntimePrefix = "RuntimeToken";
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly Color FallbackFrame = new Color(0.35f, 0.35f, 0.38f, 1f);

    /// <summary>给 UI Image 用的头像 Sprite，走内容包 portrait key。</summary>
    public static Sprite LoadPortraitSprite(UnitDefinition definition)
    {
        if (definition == null) return null;
        string key = definition.PortraitKey;
        if (string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(definition.UnitId))
            key = "portrait." + definition.UnitId;
        return RuntimeCardAdapter.LoadManagedSprite(key, "portrait");
    }

    /// <summary>按角色定义刷新棋子顶面贴图和侧面外框颜色。</summary>
    public static void Apply(Component host, UnitDefinition definition)
    {
        if (host == null) return;
        Texture2D portrait = LoadPortrait(definition);
        Color? frameColor = definition != null ? ParseColor(definition.TokenFrameColor) : null;
        ApplyToMesh(host.gameObject, portrait, frameColor);
    }

    private static Texture2D LoadPortrait(UnitDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.PortraitKey)) return null;
        Texture2D texture = LoadTexture(definition.PortraitKey, "portrait");
        if (texture == null)
            Debug.LogWarning($"棋子贴图未加载 [{definition.UnitId}]：{definition.PortraitKey}。");
        return texture;
    }

    private static Texture2D LoadTexture(string assetKey, string expectedKind)
    {
        Sprite sprite = RuntimeCardAdapter.LoadManagedSprite(assetKey, expectedKind);
        if (sprite == null) return null;
        Texture2D texture = sprite.texture;
        if (texture == null) return null;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        return texture;
    }

    private static Color? ParseColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        return ColorUtility.TryParseHtmlString(hex.Trim(), out Color color) ? color : null;
    }

    private static void ApplyToMesh(GameObject host, Texture2D portrait, Color? frameColor)
    {
        CombatantTokenFactory.EnsureTokenComponents(host);
        MeshRenderer renderer = CombatantTokenFactory.ResolveVisualRenderer(host);
        if (renderer == null) return;

        if (CombatantTokenFactory.HasAuthoredVisual(host))
        {
            ApplyToAuthoredRenderer(renderer, portrait, frameColor);
            return;
        }

        Material[] previous = renderer.sharedMaterials;
        Material frame = TakeRuntimeMaterial(previous, 0) ?? CreateUnlit("Frame");
        Material face = TakeRuntimeMaterial(previous, 1) ?? CreateUnlit("Face");
        if (frame == null || face == null) return;
        AssignColor(frame, frameColor ?? FallbackFrame);
        AssignTexture(face, portrait);
        AssignColor(face, Color.white);
        renderer.sharedMaterials = new[] { frame, face };
        renderer.enabled = true;
    }

    /// <summary>手摆棋子保留 ProBuilder 材质，只改顶面贴图和可选外框颜色。</summary>
    private static void ApplyToAuthoredRenderer(MeshRenderer renderer, Texture2D portrait, Color? frameColor)
    {
        Material[] materials = renderer.materials;
        if (materials == null || materials.Length == 0) return;
        if (frameColor.HasValue) AssignColor(materials[0], frameColor.Value);
        int faceIndex = materials.Length >= 2 ? 1 : 0;
        if (portrait != null)
        {
            AssignTexture(materials[faceIndex], portrait);
            AssignColor(materials[faceIndex], Color.white);
        }

        renderer.enabled = true;
    }

    private static Material TakeRuntimeMaterial(Material[] materials, int index)
    {
        if (materials == null || index >= materials.Length) return null;
        Material material = materials[index];
        return material != null && material.name.StartsWith(RuntimePrefix) ? material : null;
    }

    private static Material CreateUnlit(string suffix)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Universal Render Pipeline/Lit") ??
                        Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogError("找不到 URP Unlit/Lit 着色器，无法生成棋子材质。");
            return null;
        }

        return new Material(shader) { name = RuntimePrefix + suffix };
    }

    private static void AssignTexture(Material material, Texture2D texture)
    {
        if (material == null || texture == null) return;
        material.SetTexture(BaseMapId, texture);
        material.SetTextureScale(BaseMapId, Vector2.one);
        material.SetTextureOffset(BaseMapId, Vector2.zero);
        material.SetTexture(MainTexId, texture);
        material.SetTextureScale(MainTexId, Vector2.one);
        material.SetTextureOffset(MainTexId, Vector2.zero);
    }

    private static void AssignColor(Material material, Color color)
    {
        if (material == null) return;
        if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, color);
        if (material.HasProperty(ColorId)) material.SetColor(ColorId, color);
    }
}
}
