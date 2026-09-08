using System;
using System.Collections.Generic;
using UnityEngine;

public class MoveParameters
{
    public Vector2 targetPosition;
    public bool whetherToEnterPortal;
    public MoveParameters(Vector2 targetPosition, bool whetherToEnterPortal)
    {
        this.targetPosition = targetPosition;
        this.whetherToEnterPortal = whetherToEnterPortal;
    }
}
public class MapDataManager : IManagerStartEnd
{
    private static MapDataManager _instance;
    public static MapDataManager Manager
    {
        get
        {
            if (_instance == null)
                _instance = new MapDataManager();
            return _instance;
        }
    }
    public MapDataManager() { }
    private struct AStarProperty
    {
        public Vector2 plotPos;
        public (int x, int y) plotPosCameFrom;
        public bool Passable;
        public bool marked;
        public bool portalEnter;
        public bool portalOut;
        public float cost;
        public float priority;
        /// <summary>
        /// ���ò���
        /// </summary>
        /// <param name="walkableType">��ͨ�����ͣ�0-�����ͨ����1-����������ͨ����2-���п�ͨ����3-����ͨ��</param>
        public void Reset(bool passable)
        {
            plotPosCameFrom = ((int)plotPos.x, (int)plotPos.y);
            Passable = passable;
            marked = false;
            portalOut = false;
            cost = 0;
            priority = 0;
        }
    }
    public Tile[,]    Tiles;          // dense runtime matrix, mirrors LevelData.MapData but indexed by (i, j)
    public Material[,] TileMaterials; // parallel array — per-cell renderer material from MapPrefab, or null
    private LevelData _levelData;
    public bool[,] HigherCanSetBlock
    {
        get
        {
            bool[,] high = new bool[iSize, jSize];
            for (int i = 0; i < iSize; i++)
            {
                for (int j = 0; j < jSize; j++)
                {
                    high[i, j] = Tiles[i, j].highland && Tiles[i, j].canSet;
                }
            }
            return high;
        }
    }
    public bool[,] LowerCanSetBlock
    {
        get
        {
            bool[,] low = new bool[iSize, jSize];
            for (int i = 0; i < iSize; i++)
            {
                for (int j = 0; j < jSize; j++)
                {
                    low[i, j] = !Tiles[i, j].highland && !Tiles[i, j].deadly && Tiles[i, j].canSet;
                }
            }
            return low;
        }
    }
    private struct HeapEntry
    {
        public int i;
        public int j;
        public float priority;
    }
    private HeapEntry[] heap;
    private int heapCount;
    private AStarProperty[,] graph;
    private List<MoveParameters> path = new List<MoveParameters>();
    public int iSize;
    public int jSize;
    public (int iSize, int jSize) MapSize { get { return (iSize, jSize); } }
    private GameObject _map;
    /// <summary>不可部署标记材质的 Resources 路径(不含扩展名)。</summary>
    private const string UndeployableMarkerMaterialPath = "Prefabs/Levels/Main/MainImgs/UndeployableMarker";
    /// <summary>
    /// 标记层 z:本项目相机在 -z 侧朝 +z 看,z 越小越靠上层。地块 quad 在 0.01(最底层),
    /// 实体/装饰在 0(上层),标记取 0.005 夹在中间——盖住地块、不被单位挡住。
    /// </summary>
    private const float UndeployableMarkerZ = 0.005f;
    /// <summary>运行时 new 的 Mesh 是独立于 GameObject 的原生对象,须在 ToEnd 显式销毁。</summary>
    private Mesh _undeployableOverlayMesh;

    public ref Tile GetPosBlockRef(int i, int j)
    {
        return ref Tiles[i, j];
    }

    /// <summary>
    /// 与 <see cref="TileMaterials"/> 平行的 ref 访问,供可放置高亮等"需要改材质色"的场景用。
    /// </summary>
    public ref Material GetMaterialRef(int i, int j)
    {
        return ref TileMaterials[i, j];
    }

    public Tile GetPosBlock(int ii, int jj)
    {
        if (ii >= 0 && jj >= 0 && ii < iSize && jj < jSize)
        {
            return Tiles[ii, jj];
        }
        else
        {
            return default;
        }
    }
    public bool IsValid(int i, int j)
    {
        return Tiles != null && i >= 0 && i < iSize && j >= 0 && j < jSize;
    }
    public Tile[] GetCricleCoverBlocks((float x, float y) posC, float r)
    {
        (int i, int j) ij0 = ((int)(posC.y + 0.5), (int)(posC.x + 0.5));
        Tile[] bDatas = new Tile[4];
        bDatas[0] = GetPosBlock(ij0.i, ij0.j);
        float k = 0.5f - r;
        if (Math.Abs(posC.x - ij0.j) < k && Math.Abs(posC.y - ij0.i) < k)
        {
            return bDatas;
        }
        else
        {
            int dx = (posC.x - ij0.j) > 0 ? 1 : -1;
            int dy = (posC.y - ij0.i) > 0 ? 1 : -1;
            Vector2 pV = new Vector2(ij0.j + 0.5f * dx, ij0.i + 0.5f * dy);
            if (Vector2.Distance(pV, new Vector2(posC.x, posC.y)) <= r)
            {
                bDatas[1] = GetPosBlock(ij0.i + dy, ij0.j);
                bDatas[2] = GetPosBlock(ij0.i, ij0.j + dx);
                bDatas[3] = GetPosBlock(ij0.i + dy, ij0.j + dx);
            }
            else if (Math.Abs(posC.x - ij0.j) >= k && Math.Abs(posC.y - ij0.i) >= k)
            {
                bDatas[1] = GetPosBlock(ij0.i + dy, ij0.j);
                bDatas[2] = GetPosBlock(ij0.i, ij0.j + dx);
            }
            else if (Math.Abs(posC.x - ij0.j) >= k && Math.Abs(posC.y - ij0.i) < k)
            {
                bDatas[1] = GetPosBlock(ij0.i, ij0.j + dx);
            }
            else if (Math.Abs(posC.x - ij0.j) < k && Math.Abs(posC.y - ij0.i) >= k)
            {
                bDatas[1] = GetPosBlock(ij0.i + dy, ij0.j);
            }
            return bDatas;
        }
    }
    public void MapInitialize()
    {
        iSize = _levelData != null ? _levelData.iSize : 0;
        jSize = _levelData != null ? _levelData.jSize : 0;
        // Tile 是 struct,new Tile[iSize, jSize] 给的是 default(Tile) (int=0, bool=false),
        // 不会跑 Tile.Default() 的字段赋值,所以这里手动填一遍。这样:
        //   - 未画刷格子的 portalOutI/J=-1 (避开 A* 的 portalEnter 误判)
        Tiles         = (iSize > 0 && jSize > 0) ? new Tile[iSize, jSize]    : null;
        TileMaterials = (iSize > 0 && jSize > 0) ? new Material[iSize, jSize] : null;
        if (Tiles != null)
        {
            var d = Tile.Default();
            for (int i = 0; i < iSize; i++)
            for (int j = 0; j < jSize; j++)
                Tiles[i, j] = d;
        }

        if (_levelData != null)
        {
            foreach (var entry in _levelData.MapData)
            {
                if (entry.i < 0 || entry.i >= iSize || entry.j < 0 || entry.j >= jSize) continue;
                Tiles[entry.i, entry.j] = entry;
            }
        }

        if (_map != null)
        {
            for (int k = 0; k < _map.transform.childCount; k++)
            {
                var child = _map.transform.GetChild(k);
                int ci = (int)child.position.y;
                int cj = (int)child.position.x;
                if (ci < 0 || ci >= iSize || cj < 0 || cj >= jSize) continue;
                var mr = child.GetComponent<MeshRenderer>();
                if (mr == null) continue;
                TileMaterials[ci, cj] = mr.material;
            }
        }

        BuildUndeployableOverlays();

        graph = (iSize > 0 && jSize > 0) ? new AStarProperty[iSize, jSize] : null;
        heap = (iSize > 0 && jSize > 0) ? new HeapEntry[iSize * jSize + 16] : null;
        if (graph != null)
        {
            for (int i = 0; i < iSize; i++)
            {
                for (int j = 0; j < jSize; j++)
                {
                    graph[i, j].plotPos = new Vector2(j, i);
                    graph[i, j].portalEnter = Tiles[i, j].portalOutI != -1;
                }
            }
        }
        EntityManager.Manager.BlockEntitysInitialize(iSize, jSize);
    }

    /// <summary>
    /// 不可部署覆盖层:对"有地块方块但 canSet=false"的格子,在 _map 下生成一张合批
    /// Mesh(一格两个三角形)贴上标记贴图。视觉从 Tiles 数据派生,重画地图无需改
    /// 美术资源;覆盖层只是 _map 的一个子物体,随 ToEnd 销毁 _map 一并带走。
    /// 未画刷格子没有地块方块(TileMaterials 为 null),本就不在关卡视觉范围内,不贴。
    /// 材质缺失只警告一次,不阻断地图初始化。
    /// </summary>
    private void BuildUndeployableOverlays()
    {
        if (_map == null) return;
        Material markerMaterial = Resources.Load<Material>(UndeployableMarkerMaterialPath);
        if (markerMaterial == null)
        {
            Debug.LogWarning(
                $"Undeployable marker material not found at Resources/{UndeployableMarkerMaterialPath}");
            return;
        }

        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int i = 0; i < iSize; i++)
        {
            for (int j = 0; j < jSize; j++)
            {
                if (Tiles[i, j].canSet || TileMaterials[i, j] == null) continue;

                // 顶点序 BL,BR,TL,TR;三角面按顺时针,正对相机(相机在 -z 侧看向 +z)。
                int v = vertices.Count;
                vertices.Add(new Vector3(j - 0.5f, i - 0.5f, UndeployableMarkerZ));
                vertices.Add(new Vector3(j + 0.5f, i - 0.5f, UndeployableMarkerZ));
                vertices.Add(new Vector3(j - 0.5f, i + 0.5f, UndeployableMarkerZ));
                vertices.Add(new Vector3(j + 0.5f, i + 0.5f, UndeployableMarkerZ));
                triangles.Add(v);     triangles.Add(v + 2); triangles.Add(v + 1);
                triangles.Add(v + 2); triangles.Add(v + 3); triangles.Add(v + 1);
            }
        }
        if (vertices.Count == 0) return;

        var mesh = new Mesh
        {
            vertices = vertices.ToArray(),
            triangles = triangles.ToArray(),
            uv = BuildMarkerUvs(vertices.Count),
        };
        mesh.RecalculateBounds();
        _undeployableOverlayMesh = mesh;

        var overlay = new GameObject("UndeployableOverlay");
        overlay.transform.SetParent(_map.transform, false);
        overlay.AddComponent<MeshFilter>().sharedMesh = mesh;
        overlay.AddComponent<MeshRenderer>().sharedMaterial = markerMaterial;
    }

    /// <summary>每格 4 个顶点共享同一张 0..1 贴图;裁切留白由贴图自身的透明边距控制。</summary>
    private static Vector2[] BuildMarkerUvs(int vertexCount)
    {
        var uvs = new Vector2[vertexCount];
        for (int k = 0; k < vertexCount; k += 4)
        {
            uvs[k]     = new Vector2(0f, 0f);
            uvs[k + 1] = new Vector2(1f, 0f);
            uvs[k + 2] = new Vector2(0f, 1f);
            uvs[k + 3] = new Vector2(1f, 1f);
        }
        return uvs;
    }
    public (int x, int y)[] RangeCaculator((int x, int y)[] originRange, (int x, int y) pos, int orientation)
    {
        if (originRange == null)
        {
            return null;
        }
        int x, y;
        List<(int x, int y)> rangeList = new List<(int x, int y)>();
        switch (orientation)
        {
            case 0:
                for (int i = 0; i < originRange.Length; i++)
                {
                    x = pos.x + originRange[i].x;
                    y = pos.y + originRange[i].y;
                    if (IsValid(y, x))
                    {
                        rangeList.Add((x, y));
                    }
                }
                break;
            case 1:
                for (int i = 0; i < originRange.Length; i++)
                {
                    x = pos.x + originRange[i].y;
                    y = pos.y - originRange[i].x;
                    if (IsValid(y, x))
                    {
                        rangeList.Add((x, y));
                    }
                }
                break;
            case 2:
                for (int i = 0; i < originRange.Length; i++)
                {
                    x = pos.x - originRange[i].x;
                    y = pos.y - originRange[i].y;
                    if (IsValid(y, x))
                    {
                        rangeList.Add((x, y));
                    }
                }
                break;
            case 3:
                for (int i = 0; i < originRange.Length; i++)
                {
                    x = pos.x - originRange[i].y;
                    y = pos.y + originRange[i].x;
                    if (IsValid(y, x))
                    {
                        rangeList.Add((x, y));
                    }
                }
                break;
        }
        return rangeList.ToArray();
    }
    public MoveParameters[] AStarWayFinding(Vector2 startPoint, Vector2 endPoint, int moveMethod)
    {
        return MapPathFinder.AStar(Tiles, iSize, jSize, startPoint, endPoint, EntityManager.EntityR, moveMethod);
    }
    public MoveParameters[] AStarWayFinding_Jump(Vector2 startPoint, Vector2 endPoint, Vector2Int[] jumpRange)
    {
        float entityR = EntityManager.EntityR;
        (int i, int j)[] JudgePointInUnWalkableBlock(Vector2 point, float entityR)
        {
            List<(int i, int j)> ijs = new List<(int i, int j)>(4);
            (int i, int j) ij0 = ((int)(point.y + 0.5), (int)(point.x + 0.5));
            if (!graph[ij0.i, ij0.j].Passable)
            {
                ijs.Add(ij0);
            }
            float k = 0.5f - entityR;
            if (Math.Abs(point.x - ij0.j) < k && Math.Abs(point.y - ij0.i) < k)
            {
                return ijs.ToArray();
            }
            else
            {
                int dx = (point.x - ij0.j) > 0 ? 1 : -1;
                int dy = (point.y - ij0.i) > 0 ? 1 : -1;
                Vector2 pV = new Vector2(ij0.j + 0.5f * dx, ij0.i + 0.5f * dy);
                if (Vector2.Distance(pV, point) <= entityR)
                {
                    if (!graph[ij0.i + dy, ij0.j].Passable) ijs.Add((ij0.i + dy, ij0.j));
                    if (!graph[ij0.i, ij0.j + dx].Passable) ijs.Add((ij0.i, ij0.j + dx));
                    if (!graph[ij0.i + dy, ij0.j + dx].Passable) ijs.Add((ij0.i + dy, ij0.j + dx));
                    return ijs.ToArray();
                }
                else
                {
                    if ((pV.y - point.y) * dy <= entityR && !graph[ij0.i + dy, ij0.j].Passable) ijs.Add((ij0.i + dy, ij0.j));
                    if ((pV.x - point.x) * dx <= entityR && !graph[ij0.i, ij0.j + dx].Passable) ijs.Add((ij0.i, ij0.j + dx));
                    return ijs.ToArray();
                }
            }
        }
        bool isReach = false;
        for (int i = 0; i < iSize; i++)
        {
            for (int j = 0; j < jSize; j++)
            {
                graph[i, j].Reset(Tiles[i, j].passableType <= 0);
            }
        }
        path.Clear();
        HeapClear();
        foreach ((int i, int j) ij in JudgePointInUnWalkableBlock(startPoint, entityR))
        {
            graph[ij.i, ij.j].Passable = true;
        }
        foreach ((int i, int j) ij in JudgePointInUnWalkableBlock(endPoint, entityR))
        {
            graph[ij.i, ij.j].Passable = true;
        }
        int startI = (int)(startPoint.y + 0.5f);
        int startJ = (int)(startPoint.x + 0.5f);
        int endI = (int)(endPoint.y + 0.5f);
        int endJ = (int)(endPoint.x + 0.5f);
        graph[startI, startJ] = Change(graph[startI, startJ], true, true, 0, Distance(startPoint, endPoint));
        graph[endI, endJ] = Change(graph[endI, endJ], true, false, 0, 0);
        HeapPush(startI, startJ, graph[startI, startJ].priority);
        AStarProperty current = default;
        while (heapCount > 0)
        {
            current = graph[heap[0].i, heap[0].j];
            if (current.plotPos.x == endJ && current.plotPos.y == endI)
            {
                isReach = true;
                break;
            }
            else
            {
                FindNewFrontier_Jump(current, endPoint, jumpRange);
                HeapPop();
            }
        }
        if (!isReach)
        {
            HeapClear();
            for (int i = 0; i < iSize; i++)
            {
                for (int j = 0; j < jSize; j++)
                {
                    graph[i, j].Reset(Tiles[i, j].passableType <= 0);
                }
            }
            foreach ((int i, int j) ij in JudgePointInUnWalkableBlock(startPoint, entityR))
            {
                graph[ij.i, ij.j].Passable = true;
            }
            foreach ((int i, int j) ij in JudgePointInUnWalkableBlock(endPoint, entityR))
            {
                graph[ij.i, ij.j].Passable = true;
            }
            graph[startI, startJ] = Change(graph[startI, startJ], true, true, 0, Distance(startPoint, endPoint));
            graph[endI, endJ] = Change(graph[endI, endJ], true, false, 0, 0);
            HeapPush(startI, startJ, graph[startI, startJ].priority);
            current = graph[heap[0].i, heap[0].j];
            while (heapCount > 0)
            {
                current = graph[heap[0].i, heap[0].j];
                if (current.plotPos.x == endJ && current.plotPos.y == endI)
                {
                    isReach = true;
                    break;
                }
                else
                {
                    FindNewFrontier_Jump(current, endPoint, jumpRange);
                    HeapPop();
                }
            }
        }
        if (isReach)
        {
            while (current.plotPosCameFrom.x != current.plotPos.x || current.plotPosCameFrom.y != current.plotPos.y)
            {
                if (current.portalOut == false)
                {
                    path.Insert(0, new MoveParameters(current.plotPos, false));
                    current = graph[current.plotPosCameFrom.y, current.plotPosCameFrom.x];
                }
                else
                {
                    path.Insert(0, new MoveParameters(current.plotPos, false));
                    current = graph[current.plotPosCameFrom.y, current.plotPosCameFrom.x];
                    path.Insert(0, new MoveParameters(current.plotPos, true));
                    current = graph[current.plotPosCameFrom.y, current.plotPosCameFrom.x];
                }
            }
            path[path.Count - 1].targetPosition = endPoint;
            return path.ToArray();
        }
        else
        {
            Debug.LogWarning("��·��");
            return null;
        }

    }
    private AStarProperty Change(AStarProperty aStarProperty, bool passable, bool marked, float cost, float priority)
    {
        aStarProperty.Passable = passable;
        aStarProperty.marked = marked;
        aStarProperty.cost = cost;
        aStarProperty.priority = priority;
        return aStarProperty;
    }

    private float Distance(Vector2 pos1, Vector2 pos2)
    {
        return Math.Abs(pos1.x - pos2.x) + Math.Abs(pos1.y - pos2.y);
    }
    private void HeapClear()
    {
        heapCount = 0;
    }
    private void HeapPush(int i, int j, float priority)
    {
        heap[heapCount].i = i;
        heap[heapCount].j = j;
        heap[heapCount].priority = priority;
        int pos = heapCount;
        while (pos > 0)
        {
            int parent = (pos - 1) >> 1;
            if (heap[parent].priority <= heap[pos].priority) break;
            (heap[parent], heap[pos]) = (heap[pos], heap[parent]);
            pos = parent;
        }
        heapCount++;
    }
    private void HeapPop()
    {
        heapCount--;
        if (heapCount > 0)
        {
            heap[0] = heap[heapCount];
            int pos = 0;
            while (true)
            {
                int left = (pos << 1) + 1;
                int right = left + 1;
                int smallest = pos;
                if (left < heapCount && heap[left].priority < heap[smallest].priority) smallest = left;
                if (right < heapCount && heap[right].priority < heap[smallest].priority) smallest = right;
                if (smallest == pos) break;
                (heap[smallest], heap[pos]) = (heap[pos], heap[smallest]);
                pos = smallest;
            }
        }
    }
    private void FindNewFrontier_Jump(AStarProperty aStarProperty, Vector2 endPoint, Vector2Int[] jumpRange)
    {
        int i = (int)aStarProperty.plotPos.y;
        int j = (int)aStarProperty.plotPos.x;
        if (graph[i, j].portalEnter)
        {
            int ti = Tiles[i, j].portalOutI;
            int tj = Tiles[i, j].portalOutJ;
            if (graph[ti, tj].marked == false)
            {
                graph[ti, tj].marked = true;
                graph[ti, tj].portalOut = true;
                graph[ti, tj].plotPosCameFrom = (j, i);
                graph[ti, tj].cost = aStarProperty.cost;
                graph[ti, tj].priority = graph[ti, tj].cost + Distance(graph[ti, tj].plotPos, endPoint);
                HeapPush(ti, tj, graph[ti, tj].priority);
            }
        }
        for (int index = 0; index < jumpRange.Length; index++)
        {
            int ii = i + jumpRange[index].y;
            int jj = j + jumpRange[index].x;
            if (ii >= 0 && ii < iSize && jj >= 0 && jj < jSize && graph[ii, jj].Passable && !graph[ii, jj].marked)
            {
                graph[ii, jj].marked = true;
                graph[ii, jj].plotPosCameFrom = (j, i);
                graph[ii, jj].cost = aStarProperty.cost + 1;
                graph[ii, jj].priority = graph[ii, jj].cost + Distance(graph[ii, jj].plotPos, endPoint);
                HeapPush(ii, jj, graph[ii, jj].priority);
            }
        }
    }

    public void CreateMap(GameObject map)
    {
        _map = UnityEngine.Object.Instantiate(map, LevelResourceSharing.LM);
    }
    public void AttachLevelData(LevelData levelData)
    {
        _levelData = levelData;
    }
    public void Initialize()
    {
        MapInitialize();
    }

    public void ToStart()
    {

    }

    public void ToEnd()
    {
        if (_undeployableOverlayMesh != null)
        {
            UnityEngine.Object.Destroy(_undeployableOverlayMesh);
            _undeployableOverlayMesh = null;
        }
        UnityEngine.Object.Destroy(_map);
    }
}
