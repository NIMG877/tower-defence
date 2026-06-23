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
///
/// 像素换算:
///   - Fit(默认)按 600×400 画布算 Zoom。
///   - MapCanvasView 启动时按 actualCanvasSize 重新算一次 Fit,使 canvas
///     不管被父容器裁剪成多宽多高,首屏都自动 fit 整张地图。
/// </summary>
public struct ViewTransform
{
    public Vector2 Offset;
    public float Zoom;

    public const float CanvasWidth = 600f;
    public const float CanvasHeight = 400f;

    /// <summary>
    /// 默认 Fit:按 600×400 画布让地图最大维度贴满。
    /// </summary>
    public static ViewTransform Fit(int iSize, int jSize)
    {
        return Fit(iSize, jSize, CanvasWidth, CanvasHeight);
    }

    /// <summary>
    /// 按实际画布尺寸 Fit:不管 canvas 被父容器裁剪成多宽多高,Zoom 总是
    /// 让地图最大维度贴满可见区域。
    /// </summary>
    public static ViewTransform Fit(int iSize, int jSize, float canvasW, float canvasH)
    {
        if (canvasW <= 0f || canvasH <= 0f || iSize <= 0 || jSize <= 0)
            return new ViewTransform { Offset = Vector2.zero, Zoom = 1f };
        float unitX = canvasW / jSize;
        float unitY = canvasH / iSize;
        float fitZoom = Mathf.Min(unitX, unitY);
        // Offset 居中:把地图中心 (jSize/2, iSize/2) 平移到画布中心 (canvasW/2, canvasH/2)
        // world -> screen:  screen = (world - Offset) * Zoom * unit
        //                  (canvasH - ... y 翻转) -> 设 iSize/2 -> 屏幕 y = canvasH/2
        // 解 Offset: Offset.x = jSize/2 - (canvasW/2) / (Zoom * unitX)
        //           由于 Zoom * unitX = canvasW/jSize(jSize 大方向),代入:
        //   (canvasW/2) / (Zoom * unitX) = (canvasW/2) / canvasW * jSize = jSize/2 ✓
        //   x 方向:Offset.x = jSize/2 - jSize/2 = 0
        //   y 方向:Offset.y = iSize/2 - iSize/2 = 0
        // 但 unitX != unitY 的话(地图非等比),y 翻转后居中需要单独算
        return new ViewTransform { Offset = Vector2.zero, Zoom = fitZoom };
    }

    /// <summary>
    /// 吸附到最近格子的中心 — editor world 整数即格子中心,直接四舍五入即可。
    /// </summary>
    public static Vector2 SnapToGrid(Vector2 world)
        => new Vector2(Mathf.Round(world.x), Mathf.Round(world.y));

    /// <summary>editor world (整数=格子中心) -> screen 像素(默认 600×400 画布, Y 翻转)。</summary>
    public Vector2 WorldToScreen(Vector2 world, int iSize, int jSize)
        => WorldToScreen(world, iSize, jSize, CanvasWidth, CanvasHeight);

    /// <summary>editor world -> screen,canvas 实际尺寸(由 MapCanvasView 在 generateVisualContent 时传)。</summary>
    public Vector2 WorldToScreen(Vector2 world, int iSize, int jSize, float canvasW, float canvasH)
    {
        float unitX = canvasW / jSize;
        float unitY = canvasH / iSize;
        var d = world - Offset;
        return new Vector2(
            d.x * Zoom * unitX,
            canvasH - d.y * Zoom * unitY); // Y 翻转
    }

    /// <summary>screen -> editor world (默认 600×400)。</summary>
    public Vector2 ScreenToWorld(Vector2 screen, int iSize, int jSize)
        => ScreenToWorld(screen, iSize, jSize, CanvasWidth, CanvasHeight);

    /// <summary>screen -> editor world,canvas 实际尺寸。</summary>
    public Vector2 ScreenToWorld(Vector2 screen, int iSize, int jSize, float canvasW, float canvasH)
    {
        float unitX = canvasW / jSize;
        float unitY = canvasH / iSize;
        return new Vector2(
            screen.x / (Zoom * unitX) + Offset.x,
            (canvasH - screen.y) / (Zoom * unitY) + Offset.y);
    }
}
