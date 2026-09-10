using UnityEngine;

/// <summary>大地图上一块地形单位格子，边缘格可指向相邻关卡。</summary>
public sealed class WorldMapTile : MonoBehaviour
{
    public string StageId;
    public int LocalX;
    public int LocalY;
    public bool IsExit;
    public string NeighborStageId;
}
