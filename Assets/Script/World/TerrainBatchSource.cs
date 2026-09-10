using UnityEngine;

/// <summary>
/// 标记格子上的基础地形 Renderer，供实例化合批使用。
/// 装饰、路径点和高亮覆盖层不要挂这个组件。
/// </summary>
[DisallowMultipleComponent]
public sealed class TerrainBatchSource : MonoBehaviour
{
    public bool Instanced { get; set; }

    public static bool IsInstancedRenderer(Renderer renderer)
    {
        return renderer != null &&
               renderer.TryGetComponent(out TerrainBatchSource source) &&
               source.Instanced;
    }
}
