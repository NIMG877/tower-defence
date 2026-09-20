using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Portal 关系的可视化层(canvas 子元素,pickingMode=Ignore,不挡事件)。
///
/// 画两种东西:
///  1. 已生效的 portal:遍历 LevelData.MapData,entry.portalOutI/J >= 0 的画一条
///     从源格中心到 portal 出口坐标的虚线+箭头(永久显示,与编辑模式无关)。
///  2. 预览线:仅在 Brush 工具 + Portal=SetPortalOut + 已点了第一段 + 鼠标在 canvas 上
///     时,画一条从 pendingPortalSource 到当前鼠标世界坐标(state.MouseWorld)
///     的虚线+箭头。state.Snap 开启时应用 entityR 粒度吸附。
///
/// 重绘触发:state.Changed、Undo/Redo、canvas MouseMove(跟随鼠标)/MouseLeave(隐藏预览)。
/// </summary>
public static class PortalLayer
{
    // 与 CheckpointLayer 选中态同色(橙黄)
    static readonly Color LineColor = new Color(1f, 0.784f, 0.314f, 0.85f);

    // 虚线 / 箭头规格
    const float DashLen = 6f;
    const float GapLen = 4f;
    const float ArrowLen = 6f;
    const float ArrowWidth = 5f;
    const float LineWidth = 1f;

    public static VisualElement Build(MapEditTab.BrushState brush, PathEditingState state, VisualElement canvas)
    {
        var layer = new VisualElement();
        layer.style.position = Position.Absolute;
        layer.style.left = 0; layer.style.top = 0;
        layer.style.right = 0; layer.style.bottom = 0;
        layer.pickingMode = PickingMode.Ignore; // 让父 canvas 接收事件

        // 鼠标是否在 canvas 上(决定是否画预览线)
        bool mouseOver = false;

        layer.generateVisualContent = ctx =>
        {
            if (state.Cache == null) return;

            // 1) 已生效的 portal — 永久显示
            var mapData = state.Cache.LevelData != null ? state.Cache.LevelData.MapData : null;
            if (mapData != null)
            {
                foreach (var entry in mapData)
                {
                    if (entry.portalOutI < 0 || entry.portalOutJ < 0) continue;
                    var srcWorld = new Vector2(entry.j, entry.i);
                    var dstWorld = new Vector2(entry.portalOutJ, entry.portalOutI);
                    DrawArrow(ctx, state, srcWorld, dstWorld);
                }
            }

            // 2) 预览线 — brush=null 表示路径 tab(无预览,只画已生效的);
            //    map tab 仅在 Portal 工具激活 + 第一段已点 + 鼠标在 canvas 上时画
            if (brush != null
                && mouseOver
                && brush.tool == MapEditTab.BrushState.Tool.Portal
                && brush.pendingPortalSource.HasValue)
            {
                var (i, j) = brush.pendingPortalSource.Value;
                var srcWorld = new Vector2(j, i);
                var end = state.MouseWorld;
                if (state.Snap) end = ViewTransform.SnapToGrid(end, state.Cache.EntityR);
                DrawArrow(ctx, state, srcWorld, end);
            }
        };

        // 重绘触发
        state.Changed += () => layer.MarkDirtyRepaint();
        Undo.undoRedoPerformed += () => layer.MarkDirtyRepaint();
        canvas.RegisterCallback<MouseMoveEvent>(_ =>
        {
            if (!mouseOver) mouseOver = true;
            layer.MarkDirtyRepaint();
        });
        canvas.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            if (mouseOver) { mouseOver = false; layer.MarkDirtyRepaint(); }
        });

        layer.RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            state.Changed -= () => layer.MarkDirtyRepaint();
            Undo.undoRedoPerformed -= () => layer.MarkDirtyRepaint();
        });

        return layer;
    }

    static void DrawArrow(MeshGenerationContext ctx, PathEditingState state, Vector2 srcWorld, Vector2 dstWorld)
    {
        var cache = state.Cache;
        var srcScreen = state.View.WorldToScreen(srcWorld, cache.ISize, cache.JSize);
        var dstScreen = state.View.WorldToScreen(dstWorld, cache.ISize, cache.JSize);

        var p2d = ctx.painter2D;
        p2d.strokeColor = LineColor;
        p2d.lineWidth = LineWidth;
        p2d.lineCap = LineCap.Round;
        p2d.lineJoin = LineJoin.Round;

        DrawDashedLine(p2d, srcScreen, dstScreen, DashLen, GapLen);

        // 箭头三角形
        var delta = dstScreen - srcScreen;
        float len = delta.magnitude;
        if (len < 0.5f) return; // 太短就不画箭头
        var dir = delta / len;
        var perp = new Vector2(-dir.y, dir.x);
        var tip = dstScreen;
        var base1 = tip - dir * ArrowLen + perp * (ArrowWidth * 0.5f);
        var base2 = tip - dir * ArrowLen - perp * (ArrowWidth * 0.5f);
        p2d.fillColor = LineColor;
        p2d.BeginPath();
        p2d.MoveTo(tip);
        p2d.LineTo(base1);
        p2d.LineTo(base2);
        p2d.ClosePath();
        p2d.Fill();
    }

    /// <summary>
    /// Painter2D 没有原生 dash 数组,改用"每段 dash 画一次 BeginPath/Stroke"模拟。
    /// </summary>
    static void DrawDashedLine(Painter2D p2d, Vector2 a, Vector2 b, float dashLen, float gapLen)
    {
        var delta = b - a;
        float total = delta.magnitude;
        if (total < 0.5f) return;
        var dir = delta / total;
        float step = dashLen + gapLen;
        for (float s = 0; s < total; s += step)
        {
            float e = Mathf.Min(s + dashLen, total);
            p2d.BeginPath();
            p2d.MoveTo(a + dir * s);
            p2d.LineTo(a + dir * e);
            p2d.Stroke();
        }
    }
}
