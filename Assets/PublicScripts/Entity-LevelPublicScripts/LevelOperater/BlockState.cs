using UnityEngine;

/// <summary>
/// Runtime state for a single map cell. Stored in
/// <c>MapDataManager.BlockStateMatrix</c>. See spec §3.2.
/// </summary>
public struct BlockState : IPathable
{
    public bool     highland;
    public bool     canSet;
    public int      passableType;
    public bool     deadly;
    public int      portalOutI;
    public int      portalOutJ;
    public Color    portalColor;
    public Material material;     // null if no prefab instantiated

    int IPathable.PassableType => passableType;
    int IPathable.PortalOutI   => portalOutI;
    int IPathable.PortalOutJ   => portalOutJ;
}