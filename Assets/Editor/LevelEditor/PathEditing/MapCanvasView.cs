using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class MapCanvasView
{
    public static VisualElement Build(SerializedObject so, PathEditingState state)
    {
        var canvas = new VisualElement();
        canvas.style.width = ViewTransform.CanvasWidth;
        canvas.style.height = ViewTransform.CanvasHeight;
        canvas.style.backgroundColor = new Color(0.078f, 0.078f, 0.094f); // rgb(20,20,24)
        canvas.style.borderTopLeftRadius = 3;
        canvas.style.borderTopRightRadius = 3;
        canvas.style.borderBottomLeftRadius = 3;
        canvas.style.borderBottomRightRadius = 3;
        canvas.style.borderLeftWidth = 1;
        canvas.style.borderRightWidth = 1;
        canvas.style.borderTopWidth = 1;
        canvas.style.borderBottomWidth = 1;
        canvas.style.borderLeftColor = new Color(0.235f, 0.235f, 0.275f);
        canvas.style.borderRightColor = new Color(0.235f, 0.235f, 0.275f);
        canvas.style.borderTopColor = new Color(0.235f, 0.235f, 0.275f);
        canvas.style.borderBottomColor = new Color(0.235f, 0.235f, 0.275f);
        canvas.style.overflow = Overflow.Hidden;
        canvas.style.position = Position.Relative;

        // 网格 + A* 路径
        canvas.generateVisualContent += ctx =>
        {
            if (state.Cache == null) return;
            DrawBlocks(ctx, state);
            DrawPaths(ctx, so, state);
        };

        // Checkpoint 圆 (作为子 VisualElement 添加,UI Toolkit 自动绘于父 generateVisualContent 之上)
        var cpLayer = CheckpointLayer.Build(so, state, canvas);
        canvas.Add(cpLayer);
        canvas.AddManipulator(new EditorPathManipulator(so, state, canvas, cpLayer));

        // Hint + cursor readout
        var hint = new Label("右键拖拽缩放 · 中键拖拽平移 · 左键新建/选中 · 拖动改位置");
        hint.style.position = Position.Absolute;
        hint.style.bottom = 4; hint.style.right = 8;
        hint.style.fontSize = 10;
        hint.style.color = new Color(0.55f, 0.55f, 0.55f);
        canvas.Add(hint);

        var cursorReadout = new Label("(0.0, 0.0)");
        cursorReadout.name = "cursor-readout";
        cursorReadout.style.position = Position.Absolute;
        cursorReadout.style.bottom = 4; cursorReadout.style.left = 8;
        cursorReadout.style.fontSize = 10;
        cursorReadout.style.color = new Color(0.55f, 0.55f, 0.55f);
        cursorReadout.style.unityFontStyleAndWeight = FontStyle.Normal;
        canvas.Add(cursorReadout);

        // 重绘触发:state 变化、Undo/Redo
        state.Changed += () => canvas.MarkDirtyRepaint();
        so.Update();
        Undo.undoRedoPerformed += () => canvas.MarkDirtyRepaint();

        return canvas;
    }

    public static void DrawBlocks(MeshGenerationContext ctx, PathEditingState state)
    {
        var p2d = ctx.painter2D;
        var cache = state.Cache;
        for (int i = 0; i < cache.ISize; i++)
        {
            for (int j = 0; j < cache.JSize; j++)
            {
                // 没有 MapData entry 的格子不画(让 canvas 背景透出来)
                if (!cache.HasEntry[i, j]) continue;

                var bd = cache.Blocks[i, j];

                // cell [i,j] 的中心是 (j, i) 整数;UnityEngine.Rect.y 视作"顶",
                // 顶边对应 grid y = i+0.5(屏幕 y 较小),底边对应 y = i-0.5(屏幕 y 较大)。
                var tl = state.View.WorldToScreen(new Vector2(j - 0.5f, i + 0.5f), cache.ISize, cache.JSize);
                var br = state.View.WorldToScreen(new Vector2(j + 0.5f, i - 0.5f), cache.ISize, cache.JSize);
                var rect = new Rect(tl.x, tl.y, br.x - tl.x, br.y - tl.y);

                Color fill;
                if (bd.deadly)
                    fill = new Color(0.471f, 0.235f, 0.235f); // rgb(120,60,60)
                else
                    fill = BlockTypeColor(bd.passableType);

                p2d.fillColor = fill;
                BeginRectPath(p2d, rect);
                p2d.Fill();

                // Highland 黄框 / CanSet 青框
                if (bd.highland)
                {
                    p2d.strokeColor = new Color(0.706f, 0.549f, 0.235f);
                    p2d.lineWidth = 0.5f;
                    BeginRectPath(p2d, rect);
                    p2d.Stroke();
                }
                if (bd.canSet)
                {
                    p2d.strokeColor = new Color(0.549f, 0.784f, 0.706f);
                    p2d.lineWidth = 0.5f;
                    BeginRectPath(p2d, rect);
                    p2d.Stroke();
                }
                if (bd.portalOutI != -1)
                {
                    p2d.strokeColor = bd.portalColor;
                    p2d.lineWidth = 0.5f;
                    BeginRectPath(p2d, rect);
                    p2d.Stroke();
                }
            }
        }
    }

    static Color BlockTypeColor(int passableType) => passableType switch
    {
        0 => new Color(0.235f, 0.255f, 0.216f), // rgb(60,65,55)
        1 => new Color(0.176f, 0.235f, 0.353f), // rgb(45,60,90)
        2 => new Color(0.196f, 0.314f, 0.353f), // rgb(50,80,90)
        _ => new Color(0.157f, 0.157f, 0.157f)
    };

    // Painter2D 没有 Rect(...) 方法,改用 4 顶点 + ClosePath 拼矩形
    static void BeginRectPath(Painter2D p2d, Rect r)
    {
        p2d.BeginPath();
        p2d.MoveTo(new Vector2(r.x, r.y));
        p2d.LineTo(new Vector2(r.xMax, r.y));
        p2d.LineTo(new Vector2(r.xMax, r.yMax));
        p2d.LineTo(new Vector2(r.x, r.yMax));
        p2d.ClosePath();
    }

    static void DrawPaths(MeshGenerationContext ctx, SerializedObject so, PathEditingState state)
    {
        if (state.SelectedPathIdx < 0) return;
        var pathsProp = so.FindProperty("Paths");
        if (pathsProp == null || state.SelectedPathIdx >= pathsProp.arraySize) return;

        var pathProp = pathsProp.GetArrayElementAtIndex(state.SelectedPathIdx);
        var cpsProp = pathProp.FindPropertyRelative("CheckPoints");
        if (cpsProp == null || cpsProp.arraySize < 2) return;

        var p2d = ctx.painter2D;
        var cache = state.Cache;

        for (int k = 0; k < cpsProp.arraySize - 1; k++)
        {
            var start = cpsProp.GetArrayElementAtIndex(k).vector2Value;
            var end = cpsProp.GetArrayElementAtIndex(k + 1).vector2Value;

            var path = EditorPathFinder.AStar(
                cache.Blocks, cache.ISize, cache.JSize,
                start, end, cache.EntityR, state.MoveMethod);

            p2d.strokeColor = path == null
                ? new Color(0.95f, 0.4f, 0.4f)   // 红虚线表示不可达(简化:实线)
                : new Color(0.306f, 0.788f, 0.627f); // rgb(78,201,160)
            p2d.lineWidth = 1.5f;
            p2d.BeginPath();

            if (path != null)
            {
                var first = state.View.WorldToScreen(path[0].targetPosition, cache.ISize, cache.JSize);
                p2d.MoveTo(first);
                for (int m = 1; m < path.Length; m++)
                {
                    var pt = state.View.WorldToScreen(path[m].targetPosition, cache.ISize, cache.JSize);
                    p2d.LineTo(pt);
                }
            }
            else
            {
                var s = state.View.WorldToScreen(start, cache.ISize, cache.JSize);
                var e = state.View.WorldToScreen(end, cache.ISize, cache.JSize);
                p2d.MoveTo(s);
                p2d.LineTo(e);
            }
            p2d.Stroke();
        }
    }
}
