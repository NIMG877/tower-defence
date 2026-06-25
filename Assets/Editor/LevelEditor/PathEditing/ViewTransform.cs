using UnityEngine;

/// <summary>
/// 世界 (grid) 坐标与画布 (screen) 像素坐标之间的双向转换。
///
/// 坐标系约定(editor 视角):
///   - editor world 单位 = "格子中心",整数 (3, 5) = 第 4 列第 6 行格子中心。
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
    /// 计算让地图以正方形格子居中贴满画布的 Zoom + Offset。
    /// </summary>
    public static ViewTransform Fit(int iSize, int jSize)
    {
        float unitX = CanvasWidth / jSize;
        float unitY = CanvasHeight / iSize;
        float unit = Mathf.Min(unitX, unitY);

        // Map is square-cell sized at `unit` px per side. Center it in the canvas:
        //   - If unitX < unitY (j is the wide axis / limiting), map's X spans full CanvasWidth,
        //     map's Y is shorter than CanvasHeight → center vertically (offset.y).
        //   - If unitY < unitX, center horizontally (offset.x).
        // Offset is in WORLD units. WorldToScreen does (world - Offset) * unit, so:
        //   To get screen X = (CanvasWidth - jSize*unit) / 2 when world.x = 0:
        //     (0 - Offset.x) * unit = (CanvasWidth - jSize*unit) / 2
        //     Offset.x = -(CanvasWidth - jSize*unit) / (2*unit)
        //   To get screen Y = (CanvasHeight + iSize*unit) / 2 when world.y = 0 (Y is flipped):
        //     CanvasHeight - (0 - Offset.y) * unit = (CanvasHeight + iSize*unit) / 2
        //     Offset.y = (iSize*unit - CanvasHeight) / (2*unit)
        float mapWidthPx = jSize * unit;
        float mapHeightPx = iSize * unit;
        return new ViewTransform
        {
            Offset = new Vector2(
                -(CanvasWidth - mapWidthPx) / (2f * unit),
                (mapHeightPx - CanvasHeight) / (2f * unit)),
            Zoom = 1,
        };
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
        float unit = Mathf.Min(unitX, unitY);
        var d = world - Offset;
        return new Vector2(
            d.x * Zoom * unit,
            CanvasHeight - d.y * Zoom * unit); // Y 翻转
    }

    /// <summary>screen 像素 -> editor world (整数=格子中心, Y 翻转后还原)。</summary>
    public Vector2 ScreenToWorld(Vector2 screen, int iSize, int jSize)
    {
        float unitX = CanvasWidth / jSize;
        float unitY = CanvasHeight / iSize;
        float unit = Mathf.Min(unitX, unitY);
        return new Vector2(
            screen.x / (Zoom * unit) + Offset.x,
            (CanvasHeight - screen.y) / (Zoom * unit) + Offset.y);
    }

    /// <summary>
    /// 中键 pan / cp 拖动共用的"屏幕像素 → 世界 delta"换算,返回要 OFFSET -= 的量。
    /// 关键:必须用 min(unitX, unitY) — 与 WorldToScreen/ScreenToWorld 一致,否则 iSize≠jSize 时
    /// 某一轴会出现"鼠标走 N 像素,世界只走 N × (min/max)"的滞后。
    /// Y 已翻转(屏幕向下 → 世界向上)。
    /// </summary>
    public static Vector2 ScreenDeltaToWorldDelta(Vector2 mouseDelta, int iSize, int jSize, float zoom)
    {
        float unitX = CanvasWidth / jSize;
        float unitY = CanvasHeight / iSize;
        float unit = Mathf.Min(unitX, unitY);
        return new Vector2(
            mouseDelta.x / (zoom * unit),
            -mouseDelta.y / (zoom * unit));
    }
}
