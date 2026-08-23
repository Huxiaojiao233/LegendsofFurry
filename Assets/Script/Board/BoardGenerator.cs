using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 负责生成棋盘、记录所有真实存在的格子与占格，并提供安全的查询接口。
/// boardMap 中数值为 0 的位置代表空洞，不会生成格子。
/// </summary>
[DefaultExecutionOrder(-100)]
public class BoardGenerator : MonoBehaviour
{
    [SerializeField] private float RandomMapProbability = 0.9f;

    [Header("棋盘设置")]
    [SerializeField] private GameObject cellPrefab;

    [SerializeField] private int width = 10;
    [SerializeField] private int height = 8;

    [Header("格子设置")]
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private float gap = 0f;

    [Header("节点")]
    [SerializeField] private Transform gridRoot;

    private static readonly Vector2Int[] FourDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left
    };

    private BoardCell[,] cells;
    private int[,] boardMap;
    private readonly Dictionary<Vector2Int, Unit> occupants =
        new Dictionary<Vector2Int, Unit>();

    public int Width => cells == null ? width : cells.GetLength(0);
    public int Height => cells == null ? height : cells.GetLength(1);

    private int[,] GenerateRandomBoardMap()
    {
        int[,] map = new int[height, width];

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                map[z, x] = Random.value < RandomMapProbability ? 1 : 0;
            }
        }

        return map;
    }

    private void Awake()
    {
        boardMap = GenerateRandomBoardMap();
        GenerateBoard();
    }

    /// <summary>根据 boardMap 实例化所有存在的棋盘格。</summary>
    public void GenerateBoard()
    {
        int mapHeight = boardMap.GetLength(0);
        int mapWidth = boardMap.GetLength(1);

        cells = new BoardCell[mapWidth, mapHeight];
        occupants.Clear();

        float spacing = cellSize + gap;

        for (int z = 0; z < mapHeight; z++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                if (boardMap[z, x] == 0)
                {
                    continue;
                }

                GameObject cellObject = Instantiate(cellPrefab, gridRoot);
                float posX = (x - (mapWidth - 1) / 2f) * spacing;
                float posZ = (z - (mapHeight - 1) / 2f) * spacing;
                cellObject.transform.localPosition = new Vector3(posX, 0f, posZ);
                cellObject.transform.localRotation = Quaternion.identity;

                BoardCell cell = cellObject.GetComponent<BoardCell>();
                cell.Initialize(x, z);
                cells[x, z] = cell;
            }
        }
    }

    public BoardCell GetCell(int x, int z)
    {
        if (cells == null)
        {
            Debug.LogError("棋盘还没有初始化！");
            return null;
        }

        int mapWidth = cells.GetLength(0);
        int mapHeight = cells.GetLength(1);

        if (x < 0 || x >= mapWidth || z < 0 || z >= mapHeight)
        {
            Debug.LogWarning($"位置 ({x}, {z}) 超出棋盘范围");
            return null;
        }

        BoardCell cell = cells[x, z];
        if (cell == null)
        {
            Debug.LogWarning($"位置 ({x}, {z}) 不存在格子");
        }

        return cell;
    }

    /// <summary>安静地尝试获取格子。范围计算和寻路应优先使用此接口。</summary>
    public bool TryGetCell(int x, int z, out BoardCell cell)
    {
        cell = null;

        if (cells == null ||
            x < 0 || x >= cells.GetLength(0) ||
            z < 0 || z >= cells.GetLength(1))
        {
            return false;
        }

        cell = cells[x, z];
        return cell != null;
    }

    public bool IsOccupied(int x, int z, Unit ignore = null)
    {
        if (!occupants.TryGetValue(new Vector2Int(x, z), out Unit occupant) ||
            occupant == null)
        {
            return false;
        }

        return occupant != ignore;
    }

    public bool TryGetOccupant(int x, int z, out Unit occupant)
    {
        return occupants.TryGetValue(new Vector2Int(x, z), out occupant) &&
            occupant != null;
    }

    public void SetOccupant(Unit unit, Vector2Int position, Vector2Int? previous)
    {
        if (unit == null)
        {
            return;
        }

        if (previous.HasValue)
        {
            Vector2Int oldPosition = previous.Value;
            if (occupants.TryGetValue(oldPosition, out Unit current) && current == unit)
            {
                occupants.Remove(oldPosition);
            }
        }
        else
        {
            RemoveOccupant(unit);
        }

        occupants[position] = unit;
    }

    public void RemoveOccupant(Unit unit)
    {
        if (unit == null)
        {
            return;
        }

        List<Vector2Int> keysToRemove = null;
        foreach (KeyValuePair<Vector2Int, Unit> pair in occupants)
        {
            if (pair.Value == unit)
            {
                keysToRemove ??= new List<Vector2Int>();
                keysToRemove.Add(pair.Key);
            }
        }

        if (keysToRemove == null)
        {
            return;
        }

        for (int i = 0; i < keysToRemove.Count; i++)
        {
            occupants.Remove(keysToRemove[i]);
        }
    }

    /// <summary>从偏好坐标开始，找最近的空格子。随机地图导致出生点是空洞时使用。</summary>
    public bool TryFindNearestFreeCell(
        Vector2Int preferred,
        Unit ignore,
        out Vector2Int result)
    {
        result = preferred;

        if (cells == null)
        {
            return false;
        }

        int mapWidth = cells.GetLength(0);
        int mapHeight = cells.GetLength(1);
        if (IsFreeCell(preferred.x, preferred.y, ignore))
        {
            return true;
        }

        Queue<Vector2Int> frontier = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        frontier.Enqueue(preferred);
        visited.Add(preferred);

        while (frontier.Count > 0)
        {
            Vector2Int current = frontier.Dequeue();
            foreach (Vector2Int direction in FourDirections)
            {
                Vector2Int next = current + direction;
                if (next.x < 0 || next.x >= mapWidth ||
                    next.y < 0 || next.y >= mapHeight ||
                    !visited.Add(next))
                {
                    continue;
                }

                if (IsFreeCell(next.x, next.y, ignore))
                {
                    result = next;
                    return true;
                }

                frontier.Enqueue(next);
            }
        }

        return false;
    }

    /// <summary>四方向最短路径。目标格可被指定单位占用（用于走向敌人但不踩上去）。</summary>
    public bool TryFindPath(
        Vector2Int start,
        Vector2Int goal,
        Unit mover,
        List<Vector2Int> path)
    {
        path.Clear();

        if (start == goal)
        {
            path.Add(start);
            return true;
        }

        Queue<Vector2Int> frontier = new Queue<Vector2Int>();
        Dictionary<Vector2Int, Vector2Int> cameFrom =
            new Dictionary<Vector2Int, Vector2Int>();

        frontier.Enqueue(start);
        cameFrom[start] = start;

        while (frontier.Count > 0)
        {
            Vector2Int current = frontier.Dequeue();
            if (current == goal)
            {
                ReconstructPath(cameFrom, start, goal, path);
                return true;
            }

            foreach (Vector2Int direction in FourDirections)
            {
                Vector2Int next = current + direction;
                if (cameFrom.ContainsKey(next) || !IsWalkable(next, mover, goal))
                {
                    continue;
                }

                cameFrom[next] = current;
                frontier.Enqueue(next);
            }
        }

        return false;
    }

    private bool IsWalkable(Vector2Int coordinate, Unit mover, Vector2Int goal)
    {
        if (!TryGetCell(coordinate.x, coordinate.y, out _))
        {
            return false;
        }

        if (coordinate == goal)
        {
            return true;
        }

        return !IsOccupied(coordinate.x, coordinate.y, mover);
    }

    private bool IsFreeCell(int x, int z, Unit ignore)
    {
        return TryGetCell(x, z, out _) && !IsOccupied(x, z, ignore);
    }

    private static void ReconstructPath(
        Dictionary<Vector2Int, Vector2Int> cameFrom,
        Vector2Int start,
        Vector2Int goal,
        List<Vector2Int> path)
    {
        Vector2Int current = goal;
        path.Add(current);

        while (current != start)
        {
            current = cameFrom[current];
            path.Add(current);
        }

        path.Reverse();
    }
}
