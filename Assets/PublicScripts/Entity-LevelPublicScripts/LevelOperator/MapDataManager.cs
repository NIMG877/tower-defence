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
    public void MapInitialize()
    {
        iSize = _levelData != null ? _levelData.iSize : 0;
        jSize = _levelData != null ? _levelData.jSize : 0;
        // Tile 是 struct,new Tile[iSize, jSize] 给的是 default(Tile) (int=0, bool=false),
        // 不会跑 Tile.Default() 的字段赋值,所以这里手动填一遍。这样:
        //   - 未画刷格子的 portalOutI/J=-1
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
        return MapPathFinder.AStar(Tiles, iSize, jSize, startPoint, endPoint, EntityManager.MovableEntityR, moveMethod);
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
