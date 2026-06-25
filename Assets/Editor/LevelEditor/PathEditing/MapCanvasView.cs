using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class MapCanvasView
{
    public static VisualElement Build(SerializedObject so, PathEditingState state)
    {
        // chrome / status / hint / repaint / cleanup 全部交给 EditorCanvasShell。
        // drawExtra 注入 DrawPaths(在 DrawBlocks 之后画 A* 折线)。
        var (canvas, cursorReadout) = EditorCanvasShell.Build(state,
            statusText: "(0.0, 0.0)",
            hintText: "右键拖拽缩放 · 中键拖拽平移 · 左键新建/选中 · 拖动改位置",
            drawExtra: ctx => DrawPaths(ctx, so, state));
        // EditorPathManipulator 通过 canvas.Q<Label>("cursor-readout") 拿这个 label
        cursorReadout.name = "cursor-readout";

        // Checkpoint 圆 (作为子 VisualElement 添加,UI Toolkit 自动绘于父 generateVisualContent 之上)
        var cpLayer = CheckpointLayer.Build(so, state, canvas);
        canvas.Add(cpLayer);
        canvas.AddManipulator(new EditorPathManipulator(so, state, canvas, cpLayer));

        // Portal 关系层(只渲染已生效的 portal 线;brush=null → 不画预览线)
        var portalLayer = PortalLayer.Build(null, state, canvas);
        canvas.Add(portalLayer);

        return canvas;
    }

    public static void DrawBlocks(MeshGenerationContext ctx, PathEditingState state)
    {
        var p2d = ctx.painter2D;
        var cache = state.Cache;

        // 1) 每个有 entry 的 cell:fill + canSet=false X
        for (int i = 0; i < cache.ISize; i++)
        {
            for (int j = 0; j < cache.JSize; j++)
            {
                if (!cache.HasEntry[i, j]) continue;
                var bd = cache.Blocks[i, j];
                var rect = CellRect(state, i, j, cache);

                Color fill = bd.deadly
                    ? new Color(0.471f, 0.235f, 0.235f) // rgb(120,60,60)
                    : BlockTypeColor(bd.passableType);
                p2d.fillColor = fill;
                BeginRectPath(p2d, rect);
                p2d.Fill();

                // canSet=false 在格中心画 × 提示不可放;highland 外轮廓由 DrawHighlandOutline 单独画
                if (!bd.canSet)
                {
                    p2d.strokeColor = new Color(0.5f, 0.5f, 0.5f); // 中等深灰
                    p2d.lineWidth = 0.5f;
                    DrawCellX(p2d, rect);
                }
            }
        }

        // 2) 整张 grid 一次性画完(单 path,JSize+1 + ISize+1 条线,共约
        //    (ISize + JSize + 2) × 4 顶点)。Fill 之后会被 cell 的填充覆盖,
        //    视觉上还是只在空 cell 上看到 grid。
        //    旧实现是每空 cell 一个 stroke rect,100x100 全空要 16k 顶点。
        DrawGridLines(p2d, state, cache);

        // 3) highland 外轮廓:全 grid 扫描,4 方向各一个 path + 一次 Stroke(批处理);
        //    每方向内连续 highland 段合并成一条长边(扫描线),避免逐 cell 短边浪费。
        //    画在 fill + X 之后,所以线在最上层。
        DrawHighlandOutline(p2d, state, cache);
    }

    static Rect CellRect(PathEditingState state, int i, int j, BlockMapCache cache)
    {
        // cell [i,j] 的中心是 (j, i) 整数;Rect.y 视作"顶",
        // 顶边对应 grid y = i+0.5(屏幕 y 较小),底边对应 y = i-0.5(屏幕 y 较大)。
        var tl = state.View.WorldToScreen(new Vector2(j - 0.5f, i + 0.5f), cache.ISize, cache.JSize);
        var br = state.View.WorldToScreen(new Vector2(j + 0.5f, i - 0.5f), cache.ISize, cache.JSize);
        return new Rect(tl.x, tl.y, br.x - tl.x, br.y - tl.y);
    }

    static void DrawGridLines(Painter2D p2d, PathEditingState state, BlockMapCache cache)
    {
        p2d.strokeColor = new Color(0.2f, 0.2f, 0.24f);
        p2d.lineWidth = 0.5f;
        p2d.BeginPath();

        // 全部 JSize+1 条竖直线(x = -0.5, 0.5, ..., JSize-0.5)
        for (int k = 0; k <= cache.JSize; k++)
        {
            float x = k - 0.5f;
            var top = state.View.WorldToScreen(new Vector2(x, -0.5f), cache.ISize, cache.JSize);
            var bot = state.View.WorldToScreen(new Vector2(x, cache.ISize - 0.5f), cache.ISize, cache.JSize);
            p2d.MoveTo(top);
            p2d.LineTo(bot);
        }
        // 全部 ISize+1 条水平线(y = -0.5, 0.5, ..., ISize-0.5)
        for (int k = 0; k <= cache.ISize; k++)
        {
            float y = k - 0.5f;
            var left = state.View.WorldToScreen(new Vector2(-0.5f, y), cache.ISize, cache.JSize);
            var right = state.View.WorldToScreen(new Vector2(cache.JSize - 0.5f, y), cache.ISize, cache.JSize);
            p2d.MoveTo(left);
            p2d.LineTo(right);
        }
        p2d.Stroke();
    }

    // highland 区块的外轮廓:每个 cell 检查 4 邻居,只在边界处画边。
    // 优化 A(扫描线):每个方向找连续段,合并成一条长边(50x50 块从 200 短边降到 4 长边)。
    // 优化 B(批处理):4 方向各一个 BeginPath/Stroke,全 grid 只 4 次 mesh 提交(原来 N 次)。
    // 空 cell 是 Tile.Default() { highland = false },所以对全 grid 扫描无需特判 HasEntry。
    static void DrawHighlandOutline(Painter2D p2d, PathEditingState state, BlockMapCache cache)
    {
        p2d.strokeColor = new Color(0.706f, 0.549f, 0.235f);
        p2d.lineWidth = 0.5f;

        // Pass 1: top edges — 高地方向(world y = i + 0.5,邻 cell 在 (i+1, j))
        p2d.BeginPath();
        for (int i = 0; i < cache.ISize; i++)
        {
            int runStart = -1;
            for (int j = 0; j < cache.JSize; j++)
            {
                bool needTop = cache.Blocks[i, j].highland
                    && (i + 1 >= cache.ISize || !cache.Blocks[i + 1, j].highland);
                if (needTop)
                {
                    if (runStart < 0) runStart = j;
                }
                else if (runStart >= 0)
                {
                    AddHorizontalEdge(p2d, state, cache, worldY: i + 0.5f, jStart: runStart, jEnd: j - 1);
                    runStart = -1;
                }
            }
            if (runStart >= 0)
                AddHorizontalEdge(p2d, state, cache, worldY: i + 0.5f, jStart: runStart, jEnd: cache.JSize - 1);
        }
        p2d.Stroke();

        // Pass 2: bottom edges — 世界 y = i - 0.5,邻 cell 在 (i-1, j)
        p2d.BeginPath();
        for (int i = 0; i < cache.ISize; i++)
        {
            int runStart = -1;
            for (int j = 0; j < cache.JSize; j++)
            {
                bool needBottom = cache.Blocks[i, j].highland
                    && (i - 1 < 0 || !cache.Blocks[i - 1, j].highland);
                if (needBottom)
                {
                    if (runStart < 0) runStart = j;
                }
                else if (runStart >= 0)
                {
                    AddHorizontalEdge(p2d, state, cache, worldY: i - 0.5f, jStart: runStart, jEnd: j - 1);
                    runStart = -1;
                }
            }
            if (runStart >= 0)
                AddHorizontalEdge(p2d, state, cache, worldY: i - 0.5f, jStart: runStart, jEnd: cache.JSize - 1);
        }
        p2d.Stroke();

        // Pass 3: left edges — 世界 x = j - 0.5,邻 cell 在 (i, j-1)
        p2d.BeginPath();
        for (int j = 0; j < cache.JSize; j++)
        {
            int runStart = -1;
            for (int i = 0; i < cache.ISize; i++)
            {
                bool needLeft = cache.Blocks[i, j].highland
                    && (j - 1 < 0 || !cache.Blocks[i, j - 1].highland);
                if (needLeft)
                {
                    if (runStart < 0) runStart = i;
                }
                else if (runStart >= 0)
                {
                    AddVerticalEdge(p2d, state, cache, worldX: j - 0.5f, iStart: runStart, iEnd: i - 1);
                    runStart = -1;
                }
            }
            if (runStart >= 0)
                AddVerticalEdge(p2d, state, cache, worldX: j - 0.5f, iStart: runStart, iEnd: cache.ISize - 1);
        }
        p2d.Stroke();

        // Pass 4: right edges — 世界 x = j + 0.5,邻 cell 在 (i, j+1)
        p2d.BeginPath();
        for (int j = 0; j < cache.JSize; j++)
        {
            int runStart = -1;
            for (int i = 0; i < cache.ISize; i++)
            {
                bool needRight = cache.Blocks[i, j].highland
                    && (j + 1 >= cache.JSize || !cache.Blocks[i, j + 1].highland);
                if (needRight)
                {
                    if (runStart < 0) runStart = i;
                }
                else if (runStart >= 0)
                {
                    AddVerticalEdge(p2d, state, cache, worldX: j + 0.5f, iStart: runStart, iEnd: i - 1);
                    runStart = -1;
                }
            }
            if (runStart >= 0)
                AddVerticalEdge(p2d, state, cache, worldX: j + 0.5f, iStart: runStart, iEnd: cache.ISize - 1);
        }
        p2d.Stroke();
    }

    // 单条水平边:world y 固定,从 (jStart-0.5, worldY) 到 (jEnd+0.5, worldY)
    static void AddHorizontalEdge(Painter2D p2d, PathEditingState state, BlockMapCache cache,
        float worldY, int jStart, int jEnd)
    {
        var a = state.View.WorldToScreen(new Vector2(jStart - 0.5f, worldY), cache.ISize, cache.JSize);
        var b = state.View.WorldToScreen(new Vector2(jEnd + 0.5f, worldY), cache.ISize, cache.JSize);
        p2d.MoveTo(a);
        p2d.LineTo(b);
    }

    // 单条垂直边:world x 固定,从 (worldX, iStart-0.5) 到 (worldX, iEnd+0.5)
    static void AddVerticalEdge(Painter2D p2d, PathEditingState state, BlockMapCache cache,
        float worldX, int iStart, int iEnd)
    {
        var a = state.View.WorldToScreen(new Vector2(worldX, iStart - 0.5f), cache.ISize, cache.JSize);
        var b = state.View.WorldToScreen(new Vector2(worldX, iEnd + 0.5f), cache.ISize, cache.JSize);
        p2d.MoveTo(a);
        p2d.LineTo(b);
    }

    static Color BlockTypeColor(int passableType) => passableType switch
    {
        0 => new Color(0.235f, 0.255f, 0.216f), // rgb(60,65,55)
        1 => new Color(0.176f, 0.235f, 0.353f), // rgb(45,60,90)
        2 => new Color(0.196f, 0.314f, 0.353f), // rgb(50,80,90)
        _ => new Color(0.157f, 0.157f, 0.157f)
    };

    // Painter2D 没有 Rect(...) 方法,用 4 个 LineTo 拼矩形(显式回到起点)。
    // mesh 顶点数与 ClosePath 等价 —— Painter2D 的 ClosePath 就是加一条
    // LineTo 回起点,不是"标记闭合"零顶点的语义。4 个 LineTo 让 4 条边一目了然,
    // 代码更清晰,顺手去掉对 ClosePath 隐式行为的依赖。
    static void BeginRectPath(Painter2D p2d, Rect r)
    {
        p2d.BeginPath();
        p2d.MoveTo(new Vector2(r.x, r.y));
        p2d.LineTo(new Vector2(r.xMax, r.y));
        p2d.LineTo(new Vector2(r.xMax, r.yMax));
        p2d.LineTo(new Vector2(r.x, r.yMax));
        p2d.LineTo(new Vector2(r.x, r.y));
    }

    // 两条对角线形成 ×。默认占格 50%(每边 inset 25%),比 edge-to-edge 看起来更像"标记"而不是
    // 把格切割开的痕迹。顶点账:2 quad = 8 vertices,无 join(对角线在中心交叉但不共享端点)。
    // 比 rect stroke(4 边 + 4 join ≈ 16-48 vertices)省一半以上。
    static void DrawCellX(Painter2D p2d, Rect r)
    {
        const float Inset = 0.25f;
        float x0 = r.x + r.width  * Inset;
        float y0 = r.y + r.height * Inset;
        float x1 = r.xMax - r.width  * Inset;
        float y1 = r.yMax - r.height * Inset;

        p2d.BeginPath();
        p2d.MoveTo(new Vector2(x0, y0));
        p2d.LineTo(new Vector2(x1, y1));
        p2d.MoveTo(new Vector2(x1, y0));
        p2d.LineTo(new Vector2(x0, y1));
        p2d.Stroke();
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

            var path = MapPathFinder.AStar(
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
