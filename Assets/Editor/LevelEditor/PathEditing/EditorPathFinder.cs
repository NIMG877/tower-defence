using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 编辑器端的纯函数 A* 寻路。行为与运行时 <see cref="MapDataManager.AStarWayFinding"/>
/// 保持 1:1 一致,但所有状态都是局部变量,不依赖场景/单例。
/// </summary>
public static class EditorPathFinder
{
    public static MoveParameters[] AStar(
        BlockDataEntry[,] blocks, int iSize, int jSize,
        Vector2 startPoint, Vector2 endPoint, float entityR, int moveMethod)
    {
        // 本地状态:对应运行时实例字段 graph/path/heap/heapCount
        var graph = new AStarProperty[iSize, jSize];
        var path = new List<MoveParameters>();
        var heap = new HeapEntry[iSize * jSize + 16];
        int heapCount = 0;

        // 初始化 plotPos / portalEnter(对应运行时 MapInitialize:182-184)
        for (int i = 0; i < iSize; i++)
        {
            for (int j = 0; j < jSize; j++)
            {
                // BlockDataEntry 是值类型,默认即"未配置",但仍然写入 plotPos 以便后续
                // graph[..].plotPos.x == endJ / .y == endI 终止条件成立
                graph[i, j].plotPos = new Vector2(j, i);
                graph[i, j].portalEnter = blocks[i, j].portalOutI != -1;
            }
        }

        // --- 局部函数:对应运行时 AStarWayFinding 内的同名闭包 ---
        (int i, int j)[] JudgePointInUnWalkableBlock(Vector2 point, float entityR)
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
            else
            {
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
                else
                {
                    if ((pV.y - point.y) * dy <= entityR && !graph[ij0.i + dy, ij0.j].Passable) ijs.Add((ij0.i + dy, ij0.j));
                    if ((pV.x - point.x) * dx <= entityR && !graph[ij0.i, ij0.j + dx].Passable) ijs.Add((ij0.i, ij0.j + dx));
                    return ijs.ToArray();
                }
            }
        }

        bool isReach = false;
        for (int i = 0; i < iSize; i++)
        {
            for (int j = 0; j < jSize; j++)
            {
                graph[i, j].Reset(blocks[i, j].passableType <= moveMethod);
            }
        }
        if (!IsBlocked(graph, iSize, jSize, startPoint, endPoint, entityR))
        {
            return new MoveParameters[2] { new MoveParameters(startPoint, false), new MoveParameters(endPoint, false) };
        }
        path.Clear();
        HeapClear(heap, ref heapCount);
        foreach ((int i, int j) ij in JudgePointInUnWalkableBlock(startPoint, entityR))
        {
            graph[ij.i, ij.j].Passable = true;
        }
        foreach ((int i, int j) ij in JudgePointInUnWalkableBlock(endPoint, entityR))
        {
            graph[ij.i, ij.j].Passable = true;
        }
        int startI = (int)(startPoint.y + 0.5f);
        int startJ = (int)(startPoint.x + 0.5f);
        int endI = (int)(endPoint.y + 0.5f);
        int endJ = (int)(endPoint.x + 0.5f);
        graph[startI, startJ] = Change(graph[startI, startJ], true, true, 0, Distance(startPoint, endPoint));
        graph[endI, endJ] = Change(graph[endI, endJ], true, false, 0, 0);
        HeapPush(heap, ref heapCount, startI, startJ, graph[startI, startJ].priority);
        AStarProperty current = default;
        while (heapCount > 0)
        {
            current = graph[heap[0].i, heap[0].j];
            if (current.plotPos.x == endJ && current.plotPos.y == endI)
            {
                isReach = true;
                break;
            }
            else
            {
                FindNewFrontier(blocks, graph, iSize, jSize, current, endPoint, heap, ref heapCount);
                HeapPop(heap, ref heapCount);
            }
        }
        if (!isReach)
        {
            HeapClear(heap, ref heapCount);
            for (int i = 0; i < iSize; i++)
            {
                for (int j = 0; j < jSize; j++)
                {
                    graph[i, j].Reset(blocks[i, j].passableType <= moveMethod);
                }
            }
            foreach ((int i, int j) ij in JudgePointInUnWalkableBlock(startPoint, entityR))
            {
                graph[ij.i, ij.j].Passable = true;
            }
            foreach ((int i, int j) ij in JudgePointInUnWalkableBlock(endPoint, entityR))
            {
                graph[ij.i, ij.j].Passable = true;
            }
            graph[startI, startJ] = Change(graph[startI, startJ], true, true, 0, Distance(startPoint, endPoint));
            graph[endI, endJ] = Change(graph[endI, endJ], true, false, 0, 0);
            HeapPush(heap, ref heapCount, startI, startJ, graph[startI, startJ].priority);
            current = graph[heap[0].i, heap[0].j];
            while (heapCount > 0)
            {
                current = graph[heap[0].i, heap[0].j];
                if (current.plotPos.x == endJ && current.plotPos.y == endI)
                {
                    isReach = true;
                    break;
                }
                else
                {
                    FindNewFrontier(blocks, graph, iSize, jSize, current, endPoint, heap, ref heapCount);
                    HeapPop(heap, ref heapCount);
                }
            }
        }
        if (isReach)
        {
            while (current.plotPosCameFrom.x != current.plotPos.x || current.plotPosCameFrom.y != current.plotPos.y)
            {
                if (current.portalOut == false)
                {
                    path.Insert(0, new MoveParameters(current.plotPos, false));
                    current = graph[current.plotPosCameFrom.y, current.plotPosCameFrom.x];
                }
                else
                {
                    path.Insert(0, new MoveParameters(current.plotPos, false));
                    current = graph[current.plotPosCameFrom.y, current.plotPosCameFrom.x];
                    path.Insert(0, new MoveParameters(current.plotPos, true));
                    current = graph[current.plotPosCameFrom.y, current.plotPosCameFrom.x];
                }
            }
            path.Insert(0, new MoveParameters(startPoint, false));
            path[path.Count - 1].targetPosition = endPoint;
            return CorrectTmpPositions(blocks, graph, path.ToArray(), entityR);
        }
        else
        {
            // 编辑器侧不输出 Debug.LogWarning("无路径"),调用方会用红色虚线绘制
            return null;
        }
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

    private static float Distance(Vector2 pos1, Vector2 pos2)
    {
        return Math.Abs(pos1.x - pos2.x) + Math.Abs(pos1.y - pos2.y);
    }

    private static void HeapClear(HeapEntry[] heap, ref int heapCount)
    {
        heapCount = 0;
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

    private static void FindNewFrontier(BlockDataEntry[,] blocks, AStarProperty[,] graph, int iSize, int jSize,
        AStarProperty aStarProperty, Vector2 endPoint, HeapEntry[] heap, ref int heapCount)
    {
        int i = (int)aStarProperty.plotPos.y;
        int j = (int)aStarProperty.plotPos.x;
        if (graph[i, j].portalEnter)
        {
            var bd = blocks[i, j];
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

    private static MoveParameters[] CorrectTmpPositions(BlockDataEntry[,] blocks, AStarProperty[,] graph, MoveParameters[] tmpParameters, float entityR)
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
                        if (FirstBlockLine(blocks, graph, tmpMoveParameters[i].targetPosition, tmpMoveParameters[i + 2].targetPosition, tmpMoveParameters[i + 1].targetPosition, entityR).x == -1000)
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
                                Vector2 tmp = FirstBlockLine(blocks, graph, tmpMoveParameters[i].targetPosition, tmpCPos + alpha * tmpDPos, tmpCPos, entityR);
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

    private static Vector2 FirstBlockLine(BlockDataEntry[,] blocks, AStarProperty[,] graph, Vector2 beginPos, Vector2 endPos, Vector2 originPosition, float entityR)
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
        else if (beginPos.x == endPos.x)
        {
            //1
            beginPos.x += entityR;
            endPos.x += entityR;
            Vector2 p1;
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
                p1 = new Vector2(beginPos.x + 0.5f, 0.5f + (Y[i] + Y[i + 1]) / 2);
                if (!graph[(int)p1.y, (int)p1.x].Passable)
                {
                    return true;
                }
            }
            //2
            beginPos.x -= 2 * entityR;
            endPos.x -= 2 * entityR;
            Y = new List<float>() { endPos.y };
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
                p1 = new Vector2(beginPos.x + 0.5f, 0.5f + (Y[i] + Y[i + 1]) / 2);
                if (!graph[(int)p1.y, (int)p1.x].Passable)
                {
                    return true;
                }
            }
            return false;
        }
        else
        {
            //1
            Vector2 p1, rVec2;
            p1 = endPos - beginPos;
            p1.Set(-p1.y, p1.x);
            p1 = p1.normalized * entityR;
            rVec2 = new Vector2(p1.x, p1.y);
            beginPos += rVec2;
            endPos += rVec2;
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
                    return true;
                }
            }
            //2
            beginPos -= 2 * rVec2;
            endPos -= 2 * rVec2;
            k = (beginPos.y - endPos.y) / (beginPos.x - endPos.x);
            b = beginPos.y - k * beginPos.x;
            X = new List<float>() { beginPos.x, endPos.x };
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
                    return true;
                }
            }
            return false;
        }
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