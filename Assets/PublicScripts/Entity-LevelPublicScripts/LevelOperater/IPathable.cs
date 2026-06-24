/// <summary>
/// Minimal read interface for A* pathfinding. Both editor's
/// <see cref="BlockDataEntry"/> (struct) and runtime's
/// <see cref="BlockState"/> (struct) implement this so the algorithm
/// can be written once against this interface.
/// Only exposes the fields A* actually reads.
/// </summary>
public interface IPathable
{
    int PassableType { get; }
    int PortalOutI   { get; }
    int PortalOutJ   { get; }
}