using UnityEngine;

/// <summary>
/// One grid tile. Replaces the former per-context split (BlockDataEntry for
/// editor storage, BlockState for runtime) — one struct now serves both.
/// Carries explicit <c>i</c>/<c>j</c> so the same struct works in both the
/// sparse <c>LevelData.MapData</c> list and the dense runtime
/// <c>Tiles[i, j]</c> array. Rendering-layer <c>Material</c> references live
/// in a parallel <c>TileMaterials[,]</c> — kept out of this struct so the
/// data layer stays serialization-clean and free of UnityEngine.Object refs.
/// See spec §3.
/// </summary>
[System.Serializable]
public struct Tile
{
    public int   i;             // sparse-list 用;dense 数组时下标即坐标,这里冗余但无害
    public int   j;
    public bool  highland;
    public bool  canSet;
    public int   passableType;  // 0 = ground walk, 1 = +low-air, 2 = +high-air, 3 = impassable
    public bool  deadly;
    public int   portalOutI;    // -1 = no portal
    public int   portalOutJ;    // -1 = no portal
    public Color portalColor;

    /// <summary>
    /// 未画刷格子的默认状态。
    ///   passableType = 2 → 对 moveMethod(地面/近地)不可走
    ///   portalOutI/J = -1 → sentinel "no portal",避免 A* 把它当 portal 入口
    ///   其余默认 (false / 0 / transparent)
    /// 注意:C# 9 不支持 struct 字段初始化器(要 C# 10),所以这里用静态工厂。
    /// 矩阵初始化处必须显式用 Tile.Default(),new Tile[iSize, jSize] 仍是 0/false。
    /// </summary>
    public static Tile Default() => new Tile
    {
        i = 0,
        j = 0,
        highland = false,
        canSet = false,
        passableType = 2,
        deadly = false,
        portalOutI = -1,
        portalOutJ = -1,
        portalColor = default,
    };
}