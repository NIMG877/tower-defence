using UnityEngine;

/// <summary>
/// 世界 (grid) 坐标与画布 (screen) 像素坐标之间的双向转换。
/// Offset + Zoom 模型:world -> ((world - Offset) * Zoom * unit) -> screen。
/// 与 <see cref="BlockMapCache"/> 解耦 —— 只接受原始 iSize/jSize,以便单元测试可独立构造。
/// </summary>
public struct ViewTransform
{
    public Vector2 Offset;
    public float Zoom;

    public const float CanvasWidth = 600f;
    public const float CanvasHeight = 400f;

    /// <summary>
    /// 计算让地图最大维度恰好贴满画布的 Zoom。
    /// </summary>
    public static ViewTransform Fit(int iSize, int jSize)
    {
        float unitX = CanvasWidth / jSize;
        float unitY = CanvasHeight / iSize;
        float fitZoom = Mathf.Min(unitX, unitY);
        return new ViewTransform { Offset = Vector2.zero, Zoom = fitZoom };
    }

    /// <summary>
    /// 吸附到最近的格子中心。-0.4 应吸附到 (-0.5, -0.5) 而非 (0.5, 0.5),
    /// 故使用 <see cref="Mathf.Floor"/> (而非 Round) — Floor 给的是"更小整数",+0.5 即格子中心。
    /// </summary>
    public static Vector2 SnapToGrid(Vector2 world)
        => new Vector2(Mathf.Floor(world.x) + 0.5f, Mathf.Floor(world.y) + 0.5f);

    public Vector2 WorldToScreen(Vector2 world, int iSize, int jSize)
    {
        float unitX = CanvasWidth / jSize;
        float unitY = CanvasHeight / iSize;
        return new Vector2(
            (world.x - Offset.x) * Zoom * unitX,
            (world.y - Offset.y) * Zoom * unitY);
    }

    public Vector2 ScreenToWorld(Vector2 screen, int iSize, int jSize)
    {
        float unitX = CanvasWidth / jSize;
        float unitY = CanvasHeight / iSize;
        return new Vector2(
            screen.x / (Zoom * unitX) + Offset.x,
            screen.y / (Zoom * unitY) + Offset.y);
    }
}
