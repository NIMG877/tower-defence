using UnityEngine;

/// <summary>
/// Per-tile data stored on <see cref="LevelData"/>. Sparse — only cells with
/// non-default state have an entry. Mirrors the old <c>BlockData</c> MB fields
/// minus runtime state. See spec §3.1.
/// </summary>
[System.Serializable]
public struct BlockDataEntry
{
    public int   i;
    public int   j;
    public bool  highland;
    public bool  canSet;
    public int   passableType;   // 0 = ground walk, 1 = ground+low-air, 2 = +high-air, 3 = impassable
    public bool  deadly;
    public int   portalOutI;     // -1 = no portal
    public int   portalOutJ;     // -1 = no portal
    public Color portalColor;

    public BlockState ToBlockState() => new BlockState
    {
        highland    = highland,
        canSet      = canSet,
        passableType = passableType,
        deadly      = deadly,
        portalOutI  = portalOutI,
        portalOutJ  = portalOutJ,
        portalColor = portalColor,
        material    = null,
    };
}