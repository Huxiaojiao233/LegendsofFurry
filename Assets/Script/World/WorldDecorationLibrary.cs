using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>打包进 Resources，让正式包也能实例化 Forest Essentials 装饰预制件。</summary>
[CreateAssetMenu(menuName = "Legends Of Furry/装饰预制件库", fileName = "DecorationLibrary")]
public sealed class WorldDecorationLibrary : ScriptableObject
{
    public const string ResourcePath = "World/DecorationLibrary";

    public List<WorldDecorationLibraryEntry> entries = new List<WorldDecorationLibraryEntry>();
}

[Serializable]
public sealed class WorldDecorationLibraryEntry
{
    public string id;
    public string category;
    public string label;
    public string glyph;
    public GameObject prefab;
}
