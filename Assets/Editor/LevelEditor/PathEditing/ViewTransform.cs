using UnityEngine;

/// <summary>
/// 世界 (grid) 坐标与画布 (screen) 像素坐标之间的双向转换。
///
/// 坐标系约定(editor 视角):
///   - editor world 单位 = "格子中心",整数 (3, 5) = 第 4 列第 6 行格子中心。
///   - prefab 中 BlockData 实际 transform.position 在 (3.5, 5.5);editor 不关心
///     prefab 实际值,所有 cp / snap / 显示都用整数格子中心。
///   - Y 轴:UI Toolkit 的 localMousePosition 向下为正,但 grid i 索引向上为正,
///     所以 WorldToScreen 输出要翻转 Y。
///   - 地图渲染时,cell [i, j] 的范围是 [(j-0.5, i-0.5), (j+0.5, i+0.5)],
///     中心整数 (j, i) — 调用方自己负责传入边界坐标,这里只管单点转换。
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
    /// 吸附到 0.25 粒度的格点(每格 4 个可吸附点:0, .25, .5, .75)。
    /// </summary>
    public static Vector2 SnapToGrid(Vector2 world)
        => new Vector2(Mathf.Round(world.x * 4f) / 4f, Mathf.Round(world.y * 4f) / 4f);

    /// <summary>editor world (整数=格子中心) -> screen 像素(Y 翻转以匹配 grid 方向)。</summary>
    public Vector2 WorldToScreen(Vector2 world, int iSize, int jSize)
    {
        float unitX = CanvasWidth / jSize;
        float unitY = CanvasHeight / iSize;
        var d = world - Offset;
        return new Vector2(
            d.x * Zoom * unitX,
            CanvasHeight - d.y * Zoom * unitY); // Y 翻转
    }

    /// <summary>screen 像素 -> editor world (整数=格子中心, Y 翻转后还原)。</summary>
    public Vector2 ScreenToWorld(Vector2 screen, int iSize, int jSize)
    {
        float unitX = CanvasWidth / jSize;
        float unitY = CanvasHeight / iSize;
        return new Vector2(
            screen.x / (Zoom * unitX) + Offset.x,
            (CanvasHeight - screen.y) / (Zoom * unitY) + Offset.y);
    }
}
