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
        if (!IsBlocked(graph, startPoint, endPoint, entityR))
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
                return CorrectTmpPositions(graph, path.ToArray(), entityR);
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

    private static MoveParameters[] CorrectTmpPositions(AStarProperty[,] graph, MoveParameters[] tmpParameters, float entityR)
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
                        if (FirstBlockLine(graph, tmpMoveParameters[i].targetPosition, tmpMoveParameters[i + 2].targetPosition, tmpMoveParameters[i + 1].targetPosition, entityR).x == -1000)
                        {
                            tmpMoveParameters.RemoveAt(i + 1);
                        }
                        else
                        {
                            Vector2 tmpDPos = tmpMoveParameters[i + 2].targetPosition - tmpMoveParameters[i + 1].targetPosition;
                            Vector2 tmpCPos = tmpMoveParameters[i + 1].targetPosition;
                            tmpMoveParameters.RemoveAt(i + 1);
                            for (float alpha = 0.001f; alpha <= 1; alpha += 0.001f)
                            {
                                Vector2 tmp = FirstBlockLine(graph, tmpMoveParameters[i].targetPosition, tmpCPos + alpha * tmpDPos, tmpCPos, entityR);
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

    private static Vector2 FirstBlockLine(AStarProperty[,] graph, Vector2 beginPos, Vector2 endPos, Vector2 originPosition, float entityR)
    {
        if (beginPos == endPos) return new Vector2(-1000, -1000);

        // perp = origin 在 begin→end 法向上的偏移分量,长度 = entityR
        Vector2 se = endPos - beginPos;
        Vector2 so = originPosition - beginPos;
        Vector2 projectOnSe = (so.x * se.x + so.y * se.y) / se.sqrMagnitude * se;
        Vector2 perp = (projectOnSe - so).normalized * entityR;
        var (blocked, hitPoint) = SampleCorridor(graph, beginPos + perp, endPos + perp);
        if (blocked) return BaseOnBlockNewPoint(hitPoint, endPos, beginPos, entityR);
        return new Vector2(-1000, -1000);
    }

    private static bool IsBlocked(AStarProperty[,] graph, Vector2 beginPos, Vector2 endPos, float entityR)
    {
        if (beginPos == endPos) return false;
        Vector2 dir = endPos - beginPos;
        Vector2 perp = new Vector2(-dir.y, dir.x).normalized * entityR;
        if (SampleCorridor(graph, beginPos + perp, endPos + perp).blocked) return true;
        return SampleCorridor(graph, beginPos - perp, endPos - perp).blocked;
    }

    /// <summary>
    /// 沿 begin→end 直线扫,返回 (撞墙?, 首个撞墙 cell 索引)。
    /// 撞墙 → (true, hitPoint);hitPoint = (j, i),整数 cell index(跟原 FirstBlockLine 一致)。
    /// 畅通 → (false, Vector2.zero)。
    /// hitPoint 给 FirstBlockLine 做 BaseOnBlockNewPoint 输入。
    /// 实现:Amanatides-Woo 2D DDA + super-cover(tie 双轴同进,任一 cell 不可走即报)。
    /// </summary>
    private static (bool blocked, Vector2 hitPoint) SampleCorridor(AStarProperty[,] graph,
        Vector2 beginPos, Vector2 endPos)
    {
        if (beginPos == endPos) return (false, Vector2.zero);
        float dx = endPos.x - beginPos.x;
        float dy = endPos.y - beginPos.y;
        int stepJ = dx == 0 ? 0 : (dx > 0 ? 1 : -1);
        int stepI = dy == 0 ? 0 : (dy > 0 ? 1 : -1);
        // tDelta = 走一个 cell 所需的 t;用 (1/|dx|, 1/|dy|) 避免 dx==0 时除零。
        float tDeltaJ = dx == 0 ? float.PositiveInfinity : 1f / Mathf.Abs(dx);
        float tDeltaI = dy == 0 ? float.PositiveInfinity : 1f / Mathf.Abs(dy);
        int i = Mathf.FloorToInt(beginPos.y+0.5f);
        int j = Mathf.FloorToInt(beginPos.x+0.5f);
        // tMax = 沿射线到达下一个 cell 边界的参数 t(0~1)
        float tMaxJ = stepJ > 0 ? (j + 0.5f - beginPos.x) * tDeltaJ
                     : stepJ < 0 ? (beginPos.x - j + 0.5f) * tDeltaJ : float.PositiveInfinity;
        float tMaxI = stepI > 0 ? (i + 0.5f - beginPos.y) * tDeltaI
                     : stepI < 0 ? (beginPos.y - i + 0.5f) * tDeltaI : float.PositiveInfinity;
        if (IsBlockedCell(graph, i, j)) return (true, new Vector2(j, i));
        while (i != Mathf.FloorToInt(endPos.y+0.5f) || j != Mathf.FloorToInt(endPos.x+0.5f))
        {
            if (tMaxI < tMaxJ)
            {
                tMaxI += tDeltaI;
                i += stepI;
                if (IsBlockedCell(graph, i, j)) return (true, new Vector2(j, i));
            }
            else if (tMaxJ < tMaxI)
            {
                tMaxJ += tDeltaJ;
                j += stepJ;
                if (IsBlockedCell(graph, i, j)) return (true, new Vector2(j, i));
            }
            else // tie:super-cover 双轴同进,任一 cell 不可走即报撞墙
            {
                tMaxI += tDeltaI; i += stepI;
                tMaxJ += tDeltaJ; j += stepJ;
                if (IsBlockedCell(graph, i, j)) return (true, new Vector2(j, i));
            }
        }
        return (false, Vector2.zero);
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
