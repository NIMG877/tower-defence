using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure-function A* pathfinding — the single source of truth for the
/// non-Jump variant. Behaviour is byte-identical to the previous
/// runtime implementation in <see cref="MapDataManager.AStarWayFinding"/>
/// and the editor-side copy in
/// <c>Assets/Editor/LevelEditor/PathEditing/EditorPathFinder.cs</c>
/// (now deleted). All state is local to the call — no singletons, no
/// instance fields. Reads <see cref="Tile.passableType"/> / portal coords
/// from a <c>Tile[,]</c> matrix.
/// </summary>
public static class MapPathFinder
{
    public static MoveParameters[] AStar(Tile[,] tiles, int iSize, int jSize,Vector2 startPoint, Vector2 endPoint, float entityR, int moveMethod)
    {
        var graph = new AStarProperty[iSize, jSize];
        var heap = new HeapEntry[iSize * jSize];
        int heapCount = 0;
        for (int i = 0; i < iSize; i++)
        {
            for (int j = 0; j < jSize; j++)
            {
                graph[i, j].plotPos = new Vector2(j, i);
                graph[i, j].portalEnter = tiles[i, j].portalOutI != -1;
                graph[i, j].Reset(tiles[i, j].passableType <= moveMethod);
            }
        }
        if (!IsBlocked(graph, iSize, jSize, startPoint, endPoint, entityR))
        {
            return new MoveParameters[2] { new MoveParameters(startPoint, false), new MoveParameters(endPoint, false) };
        }
        foreach (var ij in JudgePointInUnWalkableBlock(graph, startPoint, entityR)) graph[ij.i, ij.j].Passable = true;
        foreach (var ij in JudgePointInUnWalkableBlock(graph, endPoint,   entityR)) graph[ij.i, ij.j].Passable = true;
        int startI = (int)(startPoint.y + 0.5f), startJ = (int)(startPoint.x + 0.5f);
        int endI   = (int)(endPoint.y + 0.5f),   endJ   = (int)(endPoint.x + 0.5f);
        graph[startI, startJ] = Change(graph[startI, startJ], true, true, 0, Distance(startPoint, endPoint));
        graph[endI, endJ]     = Change(graph[endI, endJ],     true, false, 0, 0);
        HeapPush(heap, ref heapCount, startI, startJ, graph[startI, startJ].priority);
        while (heapCount > 0)
        {
            AStarProperty peeked = graph[heap[0].i, heap[0].j];
            HeapPop(heap, ref heapCount);
            if (peeked.plotPos.x == endJ && peeked.plotPos.y == endI)
            {
                var path = new List<MoveParameters>();
                var current = peeked;
                while (current.plotPosCameFrom.x != current.plotPos.x || current.plotPosCameFrom.y != current.plotPos.y)
                {
                    bool wasPortalExit = current.portalOut;
                    path.Insert(0, new MoveParameters(current.plotPos, false));
                    current = graph[current.plotPosCameFrom.y, current.plotPosCameFrom.x];
                    if (wasPortalExit)
                    {
                        path.Insert(0, new MoveParameters(current.plotPos, true));
                        current = graph[current.plotPosCameFrom.y, current.plotPosCameFrom.x];
                    }
                }
                path.Insert(0, new MoveParameters(startPoint, false));
                path[path.Count - 1].targetPosition = endPoint;
                return CorrectTmpPositions(tiles, graph, path.ToArray(), entityR);
                //return path.ToArray();
            }
            FindNewFrontier(tiles, graph, iSize, jSize, peeked, endPoint, heap, ref heapCount);
        }
        return null;
    }

    // ===== 私有静态辅助方法(运行时实例方法 → 纯函数) =====

    private static AStarProperty Change(AStarProperty aStarProperty, bool passable, bool marked, float cost, float priority)
    {
        aStarProperty.Passable = passable;
        aStarProperty.marked = marked;
        aStarProperty.cost = cost;
        aStarProperty.priority = priority;
        return aStarProperty;
    }

    /// <summary>
    /// 返回 point 半径 entityR 内所有不可走的 cell(cp 偏离格心时被强制 mark 为 Passable 用)。
    /// </summary>
    private static (int i, int j)[] JudgePointInUnWalkableBlock(AStarProperty[,] graph, Vector2 point, float entityR)
    {
        List<(int i, int j)> ijs = new List<(int i, int j)>(4);
        (int i, int j) ij0 = ((int)(point.y + 0.5), (int)(point.x + 0.5));
        if (!graph[ij0.i, ij0.j].Passable)
        {
            ijs.Add(ij0);
        }
        float k = 0.5f - entityR;
        if (Math.Abs(point.x - ij0.j) < k && Math.Abs(point.y - ij0.i) < k)
        {
            return ijs.ToArray();
        }
        int dx = (point.x - ij0.j) > 0 ? 1 : -1;
        int dy = (point.y - ij0.i) > 0 ? 1 : -1;
        Vector2 pV = new Vector2(ij0.j + 0.5f * dx, ij0.i + 0.5f * dy);
        if (Vector2.Distance(pV, point) <= entityR)
        {
            if (!graph[ij0.i + dy, ij0.j].Passable) ijs.Add((ij0.i + dy, ij0.j));
            if (!graph[ij0.i, ij0.j + dx].Passable) ijs.Add((ij0.i, ij0.j + dx));
            if (!graph[ij0.i + dy, ij0.j + dx].Passable) ijs.Add((ij0.i + dy, ij0.j + dx));
            return ijs.ToArray();
        }
        if ((pV.y - point.y) * dy <= entityR && !graph[ij0.i + dy, ij0.j].Passable) ijs.Add((ij0.i + dy, ij0.j));
        if ((pV.x - point.x) * dx <= entityR && !graph[ij0.i, ij0.j + dx].Passable) ijs.Add((ij0.i, ij0.j + dx));
        return ijs.ToArray();
    }

    private static float Distance(Vector2 pos1, Vector2 pos2)
    {
        return Math.Abs(pos1.x - pos2.x) + Math.Abs(pos1.y - pos2.y);
    }

    private static void HeapPush(HeapEntry[] heap, ref int heapCount, int i, int j, float priority)
    {
        heap[heapCount].i = i;
        heap[heapCount].j = j;
        heap[heapCount].priority = priority;
        int pos = heapCount;
        while (pos > 0)
        {
            int parent = (pos - 1) >> 1;
            if (heap[parent].priority <= heap[pos].priority) break;
            (heap[parent], heap[pos]) = (heap[pos], heap[parent]);
            pos = parent;
        }
        heapCount++;
    }

    private static void HeapPop(HeapEntry[] heap, ref int heapCount)
    {
        heapCount--;
        if (heapCount > 0)
        {
            heap[0] = heap[heapCount];
            int pos = 0;
            while (true)
            {
                int left = (pos << 1) + 1;
                int right = left + 1;
                int smallest = pos;
                if (left < heapCount && heap[left].priority < heap[smallest].priority) smallest = left;
                if (right < heapCount && heap[right].priority < heap[smallest].priority) smallest = right;
                if (smallest == pos) break;
                (heap[smallest], heap[pos]) = (heap[pos], heap[smallest]);
                pos = smallest;
            }
        }
    }

    private static void FindNewFrontier(Tile[,] tiles, AStarProperty[,] graph, int iSize, int jSize,
        AStarProperty aStarProperty, Vector2 endPoint, HeapEntry[] heap, ref int heapCount)
    {
        int i = (int)aStarProperty.plotPos.y;
        int j = (int)aStarProperty.plotPos.x;
        if (graph[i, j].portalEnter)
        {
            var bd = tiles[i, j];
            if (bd.portalOutI == -1 || bd.portalOutJ == -1) return; // 防御:portalEnter 标志位但坐标未配置
            int ti = bd.portalOutI;
            int tj = bd.portalOutJ;
            if (graph[ti, tj].marked == false)
            {
                graph[ti, tj].marked = true;
                graph[ti, tj].portalOut = true;
                graph[ti, tj].plotPosCameFrom = (j, i);
                graph[ti, tj].cost = aStarProperty.cost;
                graph[ti, tj].priority = graph[ti, tj].cost + Distance(graph[ti, tj].plotPos, endPoint);
                HeapPush(heap, ref heapCount, ti, tj, graph[ti, tj].priority);
            }
        }
        if (i < iSize - 1 && graph[i + 1, j].Passable && !graph[i + 1, j].marked)
        {
            graph[i + 1, j].marked = true;
            graph[i + 1, j].plotPosCameFrom = (j, i);
            graph[i + 1, j].cost = aStarProperty.cost + 1;
            graph[i + 1, j].priority = graph[i + 1, j].cost + Distance(graph[i + 1, j].plotPos, endPoint);
            HeapPush(heap, ref heapCount, i + 1, j, graph[i + 1, j].priority);
        }
        if (i > 0 && graph[i - 1, j].Passable && !graph[i - 1, j].marked)
        {
            graph[i - 1, j].marked = true;
            graph[i - 1, j].plotPosCameFrom = (j, i);
            graph[i - 1, j].cost = aStarProperty.cost + 1;
            graph[i - 1, j].priority = graph[i - 1, j].cost + Distance(graph[i - 1, j].plotPos, endPoint);
            HeapPush(heap, ref heapCount, i - 1, j, graph[i - 1, j].priority);
        }
        if (j < jSize - 1 && graph[i, j + 1].Passable && !graph[i, j + 1].marked)
        {
            graph[i, j + 1].marked = true;
            graph[i, j + 1].plotPosCameFrom = (j, i);
            graph[i, j + 1].cost = aStarProperty.cost + 1;
            graph[i, j + 1].priority = graph[i, j + 1].cost + Distance(graph[i, j + 1].plotPos, endPoint);
            HeapPush(heap, ref heapCount, i, j + 1, graph[i, j + 1].priority);
        }
        if (j > 0 && graph[i, j - 1].Passable && !graph[i, j - 1].marked)
        {
            graph[i, j - 1].marked = true;
            graph[i, j - 1].plotPosCameFrom = (j, i);
            graph[i, j - 1].cost = aStarProperty.cost + 1;
            graph[i, j - 1].priority = graph[i, j - 1].cost + Distance(graph[i, j - 1].plotPos, endPoint);
            HeapPush(heap, ref heapCount, i, j - 1, graph[i, j - 1].priority);
        }
    }

    private static MoveParameters[] CorrectTmpPositions(Tile[,] tiles, AStarProperty[,] graph, MoveParameters[] tmpParameters, float entityR)
    {
        if (tmpParameters.Length > 1)
        {
            List<MoveParameters> tmpMoveParameters = new List<MoveParameters>(tmpParameters);
            List<MoveParameters> moveParameters = new List<MoveParameters>();
            int num = 0;
            while (moveParameters != tmpMoveParameters && num < 5)
            {
                if (moveParameters.Count == tmpMoveParameters.Count)
                {
                    bool same = true;
                    for (int i = 0; i < tmpMoveParameters.Count; i++)
                    {
                        if (moveParameters[i].targetPosition != tmpMoveParameters[i].targetPosition)
                        {
                            same = false;
                            break;
                        }
                    }
                    if (same)
                    {
                        break;
                    }
                }
                moveParameters = new List<MoveParameters>(tmpMoveParameters);
                num++;
                for (int i = 0; i < tmpMoveParameters.Count - 2; i++)
                {
                    if (tmpMoveParameters[i + 1].whetherToEnterPortal == true)
                    {
                        i += 1;
                    }
                    else if (tmpMoveParameters[i].whetherToEnterPortal == false)
                    {
                        if (FirstBlockLine(tiles, graph, tmpMoveParameters[i].targetPosition, tmpMoveParameters[i + 2].targetPosition, tmpMoveParameters[i + 1].targetPosition, entityR).x == -1000)
                        {
                            tmpMoveParameters.RemoveAt(i + 1);
                        }
                        else
                        {
                            Vector2 tmpDPos = tmpMoveParameters[i + 2].targetPosition - tmpMoveParameters[i + 1].targetPosition;
                            Vector2 tmpCPos = tmpMoveParameters[i + 1].targetPosition;
                            tmpMoveParameters.RemoveAt(i + 1);
                            for (float alpha = 0.1f; alpha <= 1; alpha += 0.1f)
                            {
                                Vector2 tmp = FirstBlockLine(tiles, graph, tmpMoveParameters[i].targetPosition, tmpCPos + alpha * tmpDPos, tmpCPos, entityR);
                                if (tmp.x != -1000)
                                {
                                    i++;
                                    tmpMoveParameters.Insert(i, new MoveParameters(tmp, false));
                                }
                            }
                        }
                        i--;
                    }
                }
            }
            return tmpMoveParameters.ToArray();
        }
        else
        {
            MoveParameters[] tmpVector2Parameters = { tmpParameters[0] };
            return tmpVector2Parameters;
        }
    }

    private static Vector2 FirstBlockLine(Tile[,] tiles, AStarProperty[,] graph, Vector2 beginPos, Vector2 endPos, Vector2 originPosition, float entityR)
    {
        if (beginPos == endPos)
        {
            return new Vector2(-1000, -1000);
        }
        else if (beginPos.x == endPos.x)
        {
            Vector2Int p1;
            if (originPosition.x > beginPos.x)
            {
                beginPos.x -= entityR;
                endPos.x -= entityR;
            }
            else
            {
                beginPos.x += entityR;
                endPos.x += entityR;
            }
            List<float> Y = new List<float>() { endPos.y };
            if (beginPos.y < endPos.y)
            {
                for (int i = (int)(endPos.y + 0.5f) - 1; i >= beginPos.y + 0.5f; i--)
                {
                    Y.Add(i + 0.5f);
                }
            }
            else
            {
                for (int i = (int)(endPos.y + 0.5f); i <= beginPos.y + 0.5f; i++)
                {
                    Y.Add(i + 0.5f);
                }
            }
            Y.Add(beginPos.y);
            for (int i = 0; i < Y.Count - 1; i++)
            {
                p1 = new Vector2Int((int)(beginPos.x + 0.5), (int)(0.5 + (Y[i] + Y[i + 1]) / 2));
                if (!graph[p1.y, p1.x].Passable)
                {
                    return BaseOnBlockNewPoint(new Vector2(p1.x, p1.y), endPos, beginPos, entityR);
                }
            }
            return new Vector2(-1000, -1000);
        }
        else
        {
            Vector2 p1, p2;
            p1 = endPos - beginPos;
            p2 = originPosition - beginPos;
            p1 = (((p1.x * p2.x + p1.y * p2.y) / p1.sqrMagnitude) * (endPos - beginPos) + beginPos - originPosition).normalized * entityR;
            beginPos += p1;
            endPos += p1;
            float k = (beginPos.y - endPos.y) / (beginPos.x - endPos.x);
            float b = beginPos.y - k * beginPos.x;
            List<float> X = new List<float>() { beginPos.x, endPos.x };
            if (beginPos.y < endPos.y)
            {
                for (int i = (int)(beginPos.y + 0.5f); i <= endPos.y - 0.5f; i++)
                {
                    X.Add((i + 0.5f - b) / k);
                }
            }
            else
            {
                for (int i = (int)(endPos.y + 0.5f); i <= beginPos.y - 0.5f; i++)
                {
                    X.Add((i + 0.5f - b) / k);
                }
            }
            if (beginPos.x < endPos.x)
            {
                for (int i = (int)(beginPos.x + 0.5f); i <= endPos.x - 0.5f; i++)
                {
                    X.Add(i + 0.5f);
                }
                X.Sort((x, y) => -x.CompareTo(y));
            }
            else
            {
                for (int i = (int)(endPos.x + 0.5f); i <= beginPos.x - 0.5f; i++)
                {
                    X.Add(i + 0.5f);
                }
                X.Sort();
            }
            for (int i = 0; i < X.Count - 1; i++)
            {
                p1 = new Vector2(0.5f + (X[i] + X[i + 1]) / 2, 0.5f + k * (X[i] + X[i + 1]) / 2 + b);
                if (!graph[(int)p1.y, (int)p1.x].Passable)
                {
                    return BaseOnBlockNewPoint(new Vector2((int)p1.x, (int)p1.y), endPos, beginPos, entityR);
                }
            }
            return new Vector2(-1000, -1000);
        }
    }

    private static bool IsBlocked(AStarProperty[,] graph, int iSize, int jSize,
        Vector2 beginPos, Vector2 endPos, float entityR)
    {
        if (beginPos == endPos)
        {
            return false;
        }
        Vector2 dir = endPos - beginPos;
        Vector2 perp = new Vector2(-dir.y, dir.x).normalized * entityR;
        if (SampleCorridor(graph, beginPos + perp, endPos + perp)) return true;
        return SampleCorridor(graph, beginPos - perp, endPos - perp);
    }

    /// <summary>
    /// 沿 begin→end 直线扫,检查所经过的 cell 是否全部 Passable。
    /// 任一不可走 → true(撞墙)。全部可走 → false(走廊畅通)。
    /// 实现:Amanatides-Woo 2D DDA,逐格步进,无中点采样,无除零。
    /// </summary>
    private static bool SampleCorridor(AStarProperty[,] graph,
        Vector2 beginPos, Vector2 endPos)
    {
        if (beginPos == endPos) return false;
        float dx = endPos.x - beginPos.x;
        float dy = endPos.y - beginPos.y;
        int stepJ = dx == 0 ? 0 : (dx > 0 ? 1 : -1);
        int stepI = dy == 0 ? 0 : (dy > 0 ? 1 : -1);
        // tMax = 沿射线到达下一个 cell 边界的参数 t(0~1);tDelta = 走一个 cell 所需的 t。
        // 用 (1/|dx|, 1/|dy|) 算 tDelta,避免 dx==0 时除零。
        float tDeltaJ = dx == 0 ? float.PositiveInfinity : 1f / Mathf.Abs(dx);
        float tDeltaI = dy == 0 ? float.PositiveInfinity : 1f / Mathf.Abs(dy);
        int i = Mathf.FloorToInt(beginPos.y);
        int j = Mathf.FloorToInt(beginPos.x);
        // 起 cell 边界上的 t(从 cell 内一点走到 cell 右边/下边界的距离占比)
        float tMaxJ = stepJ > 0 ? ((j + 1) - beginPos.x) * tDeltaJ
                     : stepJ < 0 ? (beginPos.x - j) * tDeltaJ : float.PositiveInfinity;
        float tMaxI = stepI > 0 ? ((i + 1) - beginPos.y) * tDeltaI
                     : stepI < 0 ? (beginPos.y - i) * tDeltaI : float.PositiveInfinity;
        if (CheckTouchedCells(graph, beginPos.x, beginPos.y)) return true;
        while (i != Mathf.FloorToInt(endPos.y) || j != Mathf.FloorToInt(endPos.x))
        {
            float tNext = Mathf.Min(tMaxI, tMaxJ);
            if (tMaxI < tMaxJ)
            {
                tMaxI += tDeltaI;
                i += stepI;
            }
            else if (tMaxJ < tMaxI)
            {
                tMaxJ += tDeltaJ;
                j += stepJ;
            }
            else
            {
                tMaxJ += tDeltaJ;
                j += stepJ;
                tMaxI += tDeltaI;
                i += stepI;
            }
            if (CheckTouchedCells(graph, beginPos.x + dx * Mathf.Clamp01(tNext),
                    beginPos.y + dy * Mathf.Clamp01(tNext))) return true;
        }
        if (CheckTouchedCells(graph, endPos.x, endPos.y)) return true;
        return false;
    }

    private static bool CheckTouchedCells(AStarProperty[,] graph, float x, float y)
    {
        bool onVerticalGridLine = IsOnGridLine(x);
        bool onHorizontalGridLine = IsOnGridLine(y);
        int baseJ = Mathf.FloorToInt(x);
        int baseI = Mathf.FloorToInt(y);
        int minJ = onVerticalGridLine ? baseJ - 1 : baseJ;
        int maxJ = onVerticalGridLine ? baseJ : baseJ;
        int minI = onHorizontalGridLine ? baseI - 1 : baseI;
        int maxI = onHorizontalGridLine ? baseI : baseI;

        for (int checkI = minI; checkI <= maxI; checkI++)
        {
            for (int checkJ = minJ; checkJ <= maxJ; checkJ++)
            {
                if (IsBlockedCell(graph, checkI, checkJ)) return true;
            }
        }
        return false;
    }

    private static bool IsOnGridLine(float value)
    {
        return value == Mathf.Round(value);
    }

    private static bool IsBlockedCell(AStarProperty[,] graph, int i, int j)
    {
        return i < 0 || j < 0 || i >= graph.GetLength(0) || j >= graph.GetLength(1) || !graph[i, j].Passable;
    }

    private static Vector2 BaseOnBlockNewPoint(Vector2 blockCenterPosition, Vector2 endPosition, Vector2 startPosition, float entityR)
    {
        float k = entityR * 1.00001f + 0.5f;
        Vector2 sc = blockCenterPosition - startPosition;
        Vector2 se = endPosition - startPosition;
        se = ((sc.x * se.x + sc.y * se.y) / (se.x * se.x + se.y * se.y)) * se - sc;
        return blockCenterPosition + new Vector2(k * (se.x > 0 ? 1 : -1), k * (se.y > 0 ? 1 : -1));
    }

    // ===== 私有嵌套结构体 =====

    struct AStarProperty
    {
        public Vector2 plotPos;
        public (int x, int y) plotPosCameFrom;
        public bool Passable;
        public bool marked;
        public bool portalEnter;
        public bool portalOut;
        public float cost;
        public float priority;
        public void Reset(bool passable)
        {
            plotPosCameFrom = ((int)plotPos.x, (int)plotPos.y);
            Passable = passable;
            marked = false;
            portalOut = false;
            cost = 0;
            priority = 0;
        }
    }

    struct HeapEntry
    {
        public int i;
        public int j;
        public float priority;
    }
}
