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
}