using Codice.Client.BaseCommands;
using System;
using System.Collections.Generic;
using UnityEngine;

public class MoveParameters
{
    public Vector2 targetPosition;
    public bool whetherToEnterPortal;
    public MoveParameters(Vector2 targetPosition, bool whetherToEnterPortal)
    {
        this.targetPosition = targetPosition;
        this.whetherToEnterPortal = whetherToEnterPortal;
    }
}
public class MapDataManager : IManagerStartEnd
{
    private static MapDataManager _instance;
    public static MapDataManager Manager
    {
        get
        {
            if (_instance == null)
                _instance = new MapDataManager();
            return _instance;
        }
    }
    private MapDataManager() { }
    private struct AStarProperty
    {
        public Vector2 plotPos;
        public (int x, int y) plotPosCameFrom;
        public bool Passable;
        public bool marked;
        public bool portalEnter;
        public bool portalOut;
        public float cost;
        public float priority;
        /// <summary>
        /// 重置参数
        /// </summary>
        /// <param name="walkableType">可通过类型：0-地面可通过，1-近地悬浮可通过，2-飞行可通过，3-不可通过</param>
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
    public BlockData[,] BlockDataMatrix;
    public bool[,] HigherCanSetBlock
    {
        get
        {
            bool[,] high = new bool[iSize, jSize];
            for (int i = 0; i < iSize; i++)
            {
                for (int j = 0; j < jSize; j++)
                {
                    high[i, j] = BlockDataMatrix[i, j].Highland && BlockDataMatrix[i, j].CanSet;
                }
            }
            return high;
        }
    }
    public bool[,] LowerCanSetBlock
    {
        get
        {
            bool[,] low = new bool[iSize, jSize];
            for (int i = 0; i < iSize; i++)
            {
                for (int j = 0; j < jSize; j++)
                {
                    low[i, j] = !BlockDataMatrix[i, j].Highland && !BlockDataMatrix[i, j].Deadly && BlockDataMatrix[i, j].CanSet;
                }
            }
            return low;
        }
    }
    private List<AStarProperty> frontier = new List<AStarProperty>();
    private AStarProperty[,] graph;
    private List<MoveParameters> path = new List<MoveParameters>();
    private int iSize;
    private int jSize;
    public (int iSize, int jSize) MapSize { get { return (iSize, jSize); } }
    private GameObject _map;

    public BlockData GetPosBlock(int ii, int jj)
    {
        if (ii >= 0 && jj >= 0 && ii < iSize && jj < jSize)
        {
            return BlockDataMatrix[ii, jj];
        }
        else
        {
            return null;
        }
    }
    public BlockData[] GetCricleCoverBlocks((float x, float y) posC, float r)
    {
        (int i, int j) ij0 = ((int)(posC.y + 0.5), (int)(posC.x + 0.5));
        BlockData[] bDatas = new BlockData[4];
        bDatas[0] = GetPosBlock(ij0.i, ij0.j);
        float k = 0.5f - r;
        if (Math.Abs(posC.x - ij0.j) < k && Math.Abs(posC.y - ij0.i) < k)
        {
            return bDatas;
        }
        else
        {
            int dx = (posC.x - ij0.j) > 0 ? 1 : -1;
            int dy = (posC.y - ij0.i) > 0 ? 1 : -1;
            Vector2 pV = new Vector2(ij0.j + 0.5f * dx, ij0.i + 0.5f * dy);
            if (Vector2.Distance(pV, new Vector2(posC.x, posC.y)) <= r)
            {
                bDatas[1] = GetPosBlock(ij0.i + dy, ij0.j);
                bDatas[2] = GetPosBlock(ij0.i, ij0.j + dx);
                bDatas[3] = GetPosBlock(ij0.i + dy, ij0.j + dx);
            }
            else if (Math.Abs(posC.x - ij0.j) >= k && Math.Abs(posC.y - ij0.i) >= k)
            {
                bDatas[1] = GetPosBlock(ij0.i + dy, ij0.j);
                bDatas[2] = GetPosBlock(ij0.i, ij0.j + dx);
            }
            else if (Math.Abs(posC.x - ij0.j) >= k && Math.Abs(posC.y - ij0.i) < k)
            {
                bDatas[1] = GetPosBlock(ij0.i, ij0.j + dx);
            }
            else if (Math.Abs(posC.x - ij0.j) < k && Math.Abs(posC.y - ij0.i) >= k)
            {
                bDatas[1] = GetPosBlock(ij0.i + dy, ij0.j);
            }
            return bDatas;
        }
    }
    public void MapInitialize()
    {
        int childCount = _map.transform.childCount;
        iSize = 0;
        jSize = 0;
        for (int i = 0; i < childCount; i++)
        {
            Vector2 pos = _map.transform.GetChild(i).position;
            if (iSize <= pos.y)
            {
                iSize = (int)pos.y;
            }
            if (jSize <= pos.x)
            {
                jSize = (int)pos.x;
            }
        }
        iSize++;
        jSize++;
        BlockDataMatrix = new BlockData[iSize, jSize];
        for (int i = 0; i < childCount; i++)
        {
            if (_map.transform.GetChild(i).TryGetComponent(out BlockData blockData))
            {
                BlockDataMatrix[(int)blockData.transform.position.y, (int)blockData.transform.position.x] = blockData;
                blockData.Material = blockData.GetComponent<MeshRenderer>().material;//获取材质
            }
        }

        graph = new AStarProperty[iSize, jSize];
        for (int i = 0; i < iSize; i++)
        {
            for (int j = 0; j < jSize; j++)
            {
                graph[i, j].plotPos = BlockDataMatrix[i, j].transform.position;
                graph[i, j].portalEnter = BlockDataMatrix[i, j].ProtalOutBlock != null;
            }
        }
        EntityManager.Manager.BlockEntitysInitialize(iSize, jSize);
    }
    public (int x, int y)[] RangeCaculator((int x, int y)[] originRange, (int x, int y) pos, int orientation)
    {
        if (originRange == null)
        {
            return null;
        }
        int x, y;
        List<(int x, int y)> rangeList = new List<(int x, int y)>();
        switch (orientation)
        {
            case 0:
                for (int i = 0; i < originRange.Length; i++)
                {
                    x = pos.x + originRange[i].x;
                    y = pos.y + originRange[i].y;
                    if (GetPosBlock(y, x) != null)
                    {
                        rangeList.Add((x, y));
                    }
                }
                break;
            case 1:
                for (int i = 0; i < originRange.Length; i++)
                {
                    x = pos.x + originRange[i].y;
                    y = pos.y - originRange[i].x;
                    if (GetPosBlock(y, x) != null)
                    {
                        rangeList.Add((x, y));
                    }
                }
                break;
            case 2:
                for (int i = 0; i < originRange.Length; i++)
                {
                    x = pos.x - originRange[i].x;
                    y = pos.y - originRange[i].y;
                    if (GetPosBlock(y, x) != null)
                    {
                        rangeList.Add((x, y));
                    }
                }
                break;
            case 3:
                for (int i = 0; i < originRange.Length; i++)
                {
                    x = pos.x - originRange[i].y;
                    y = pos.y + originRange[i].x;
                    if (GetPosBlock(y, x) != null)
                    {
                        rangeList.Add((x, y));
                    }
                }
                break;
        }
        return rangeList.ToArray();
    }
    public MoveParameters[] AStarWayFinding(Vector2 startPoint, Vector2 endPoint, int moveMethod)
    {
        float entityR = EntityManager.EntityR;
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
                graph[i, j].Reset(BlockDataMatrix[i, j].PassableType <= moveMethod && !BlockDataMatrix[i, j].TempOccupy);
            }
        }
        if (!IsBlocked(startPoint, endPoint, entityR))
        {
            return new MoveParameters[2] { new MoveParameters(startPoint, false), new MoveParameters(endPoint, false) };
        }
        path.Clear();
        frontier.Clear();
        foreach ((int i, int j) ij in JudgePointInUnWalkableBlock(startPoint, entityR))
        {
            graph[ij.i, ij.j].Passable = true;
        }
        foreach ((int i, int j) ij in JudgePointInUnWalkableBlock(endPoint, entityR))
        {
            graph[ij.i, ij.j].Passable = true;
        }
        graph[(int)(startPoint.y + 0.5f), (int)(startPoint.x + 0.5f)] = Change(graph[(int)(startPoint.y + 0.5f), (int)(startPoint.x + 0.5f)], true, true, 0, Distance(startPoint, endPoint));
        graph[(int)(endPoint.y + 0.5f), (int)(endPoint.x + 0.5f)] = Change(graph[(int)(endPoint.y + 0.5f), (int)(endPoint.x + 0.5f)], true, false, 0, 0);
        frontier.Add(graph[(int)(startPoint.y + 0.5f), (int)(startPoint.x + 0.5f)]);
        AStarProperty current = default;
        while (frontier.Count > 0)
        {
            current = MinPriorityFrontier();
            if (current.plotPos.x == (int)(endPoint.x + 0.5f) && current.plotPos.y == (int)(endPoint.y + 0.5f))
            {
                isReach = true;
                break;
            }
            else
            {
                FindNewFrontier(current, endPoint);
                frontier.Remove(current);
            }
        }
        if (!isReach)
        {
            frontier.Clear();
            for (int i = 0; i < iSize; i++)
            {
                for (int j = 0; j < jSize; j++)
                {
                    graph[i, j].Reset(BlockDataMatrix[i, j].PassableType <= moveMethod);
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
            graph[(int)(startPoint.y + 0.5f), (int)(startPoint.x + 0.5f)] = Change(graph[(int)(startPoint.y + 0.5), (int)(startPoint.x + 0.5)], true, true, 0, Distance(startPoint, endPoint));
            graph[(int)(endPoint.y + 0.5f), (int)(endPoint.x + 0.5f)] = Change(graph[(int)(endPoint.y + 0.5), (int)(endPoint.x + 0.5)], true, false, 0, 0);
            frontier.Add(graph[(int)(startPoint.y + 0.5f), (int)(startPoint.x + 0.5f)]);
            current = MinPriorityFrontier();
            while (frontier.Count > 0)
            {
                current = MinPriorityFrontier();
                if (current.plotPos.x == (int)(endPoint.x + 0.5f) && current.plotPos.y == (int)(endPoint.y + 0.5f))
                {
                    isReach = true;
                    break;
                }
                else
                {
                    FindNewFrontier(current, endPoint);
                    frontier.Remove(current);
                }
            }
        }
        //Debug.Log($"-----------当前点{(current.plotPos)}，cost{current.cost}，来自{current.plotPosCameFrom}---------------");
        //for (int i = 0; i < iSize; i++)
        //{
        //    for (int j = 0; j < jSize; j++)
        //    {
        //        Debug.Log($"点{(j,i)}，cost{graph[i, j].cost}，来自{graph[i, j].plotPosCameFrom}");
        //    }
        //}
        //return null;
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
            return CorrectTmpPositions(path.ToArray(), entityR);
            //return path.ToArray();
        }
        else
        {
            Debug.LogWarning("无路径");
            return null;
        }

    }
    public MoveParameters[] AStarWayFinding_Jump(Vector2 startPoint, Vector2 endPoint, Vector2Int[] jumpRange)
    {
        float entityR = EntityManager.EntityR;
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
                graph[i, j].Reset(BlockDataMatrix[i, j].PassableType <= 0 && !BlockDataMatrix[i, j].TempOccupy);
            }
        }
        path.Clear();
        frontier.Clear();
        foreach ((int i, int j) ij in JudgePointInUnWalkableBlock(startPoint, entityR))
        {
            graph[ij.i, ij.j].Passable = true;
        }
        foreach ((int i, int j) ij in JudgePointInUnWalkableBlock(endPoint, entityR))
        {
            graph[ij.i, ij.j].Passable = true;
        }
        graph[(int)(startPoint.y + 0.5f), (int)(startPoint.x + 0.5f)] = Change(graph[(int)(startPoint.y + 0.5f), (int)(startPoint.x + 0.5f)], true, true, 0, Distance(startPoint, endPoint));
        graph[(int)(endPoint.y + 0.5f), (int)(endPoint.x + 0.5f)] = Change(graph[(int)(endPoint.y + 0.5f), (int)(endPoint.x + 0.5f)], true, false, 0, 0);
        frontier.Add(graph[(int)(startPoint.y + 0.5f), (int)(startPoint.x + 0.5f)]);
        AStarProperty current = default;
        while (frontier.Count > 0)
        {
            current = MinPriorityFrontier();
            if (current.plotPos.x == (int)(endPoint.x + 0.5f) && current.plotPos.y == (int)(endPoint.y + 0.5f))
            {
                isReach = true;
                break;
            }
            else
            {
                FindNewFrontier_Jump(current, endPoint, jumpRange);
                frontier.Remove(current);
            }
        }
        if (!isReach)
        {
            frontier.Clear();
            for (int i = 0; i < iSize; i++)
            {
                for (int j = 0; j < jSize; j++)
                {
                    graph[i, j].Reset(BlockDataMatrix[i, j].PassableType <= 0);
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
            graph[(int)(startPoint.y + 0.5f), (int)(startPoint.x + 0.5f)] = Change(graph[(int)(startPoint.y + 0.5), (int)(startPoint.x + 0.5)], true, true, 0, Distance(startPoint, endPoint));
            graph[(int)(endPoint.y + 0.5f), (int)(endPoint.x + 0.5f)] = Change(graph[(int)(endPoint.y + 0.5), (int)(endPoint.x + 0.5)], true, false, 0, 0);
            frontier.Add(graph[(int)(startPoint.y + 0.5f), (int)(startPoint.x + 0.5f)]);
            current = MinPriorityFrontier();
            while (frontier.Count > 0)
            {
                current = MinPriorityFrontier();
                if (current.plotPos.x == (int)(endPoint.x + 0.5f) && current.plotPos.y == (int)(endPoint.y + 0.5f))
                {
                    isReach = true;
                    break;
                }
                else
                {
                    FindNewFrontier_Jump(current, endPoint, jumpRange);
                    frontier.Remove(current);
                }
            }
        }
        //Debug.Log($"-----------当前点{(current.plotPos)}，cost{current.cost}，来自{current.plotPosCameFrom}---------------");
        //for (int i = 0; i < iSize; i++)
        //{
        //    for (int j = 0; j < jSize; j++)
        //    {
        //        Debug.Log($"点{(j,i)}，cost{graph[i, j].cost}，来自{graph[i, j].plotPosCameFrom}");
        //    }
        //}
        //return null;
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
            path[path.Count - 1].targetPosition = endPoint;
            return path.ToArray();
        }
        else
        {
            Debug.LogWarning("无路径");
            return null;
        }

    }
    private AStarProperty Change(AStarProperty aStarProperty, bool passable, bool marked, float cost, float priority)
    {
        aStarProperty.Passable = passable;
        aStarProperty.marked = marked;
        aStarProperty.cost = cost;
        aStarProperty.priority = priority;
        return aStarProperty;
    }

    private float Distance(Vector2 pos1, Vector2 pos2)
    {
        return Math.Abs(pos1.x - pos2.x) + Math.Abs(pos1.y - pos2.y);
    }
    private AStarProperty MinPriorityFrontier()
    {
        int minIndex = 0;
        for (int i = minIndex; i < frontier.Count; i++)
        {
            if (frontier[minIndex].priority > frontier[i].priority)
            {
                minIndex = i;
            }
        }
        return frontier[minIndex];
    }
    private void FindNewFrontier(AStarProperty aStarProperty, Vector2 endPoint)
    {
        int i = (int)aStarProperty.plotPos.y;
        int j = (int)aStarProperty.plotPos.x;
        if (graph[i, j].portalEnter)
        {
            int ti = (int)BlockDataMatrix[i, j].ProtalOutBlock.transform.position.y;
            int tj = (int)BlockDataMatrix[i, j].ProtalOutBlock.transform.position.x;
            if (graph[ti, tj].marked == false)
            {
                graph[ti, tj].marked = true;
                graph[ti, tj].portalOut = true;
                graph[ti, tj].plotPosCameFrom = (j, i);
                graph[ti, tj].cost = aStarProperty.cost;
                graph[ti, tj].priority = graph[ti, tj].cost + Distance(graph[ti, tj].plotPos, endPoint);
                frontier.Add(graph[ti, tj]);
            }
        }
        if (i < iSize - 1 && graph[i + 1, j].Passable && !graph[i + 1, j].marked)
        {
            graph[i + 1, j].marked = true;
            graph[i + 1, j].plotPosCameFrom = (j, i);
            graph[i + 1, j].cost = aStarProperty.cost + 1;
            graph[i + 1, j].priority = graph[i + 1, j].cost + Distance(graph[i + 1, j].plotPos, endPoint);
            frontier.Add(graph[i + 1, j]);
        }
        if (i > 0 && graph[i - 1, j].Passable && !graph[i - 1, j].marked)
        {
            graph[i - 1, j].marked = true;
            graph[i - 1, j].plotPosCameFrom = (j, i);
            graph[i - 1, j].cost = aStarProperty.cost + 1;
            graph[i - 1, j].priority = graph[i - 1, j].cost + Distance(graph[i - 1, j].plotPos, endPoint);
            frontier.Add(graph[i - 1, j]);
        }
        if (j < jSize - 1 && graph[i, j + 1].Passable && !graph[i, j + 1].marked)
        {
            graph[i, j + 1].marked = true;
            graph[i, j + 1].plotPosCameFrom = (j, i);
            graph[i, j + 1].cost = aStarProperty.cost + 1;
            graph[i, j + 1].priority = graph[i, j + 1].cost + Distance(graph[i, j + 1].plotPos, endPoint);
            frontier.Add(graph[i, j + 1]);
        }
        if (j > 0 && graph[i, j - 1].Passable && !graph[i, j - 1].marked)
        {
            graph[i, j - 1].marked = true;
            graph[i, j - 1].plotPosCameFrom = (j, i);
            graph[i, j - 1].cost = aStarProperty.cost + 1;
            graph[i, j - 1].priority = graph[i, j - 1].cost + Distance(graph[i, j - 1].plotPos, endPoint);
            frontier.Add(graph[i, j - 1]);
        }
    }
    private void FindNewFrontier_Jump(AStarProperty aStarProperty, Vector2 endPoint, Vector2Int[] jumpRange)
    {
        int i = (int)aStarProperty.plotPos.y;
        int j = (int)aStarProperty.plotPos.x;
        if (graph[i, j].portalEnter)
        {
            int ti = (int)BlockDataMatrix[i, j].ProtalOutBlock.transform.position.y;
            int tj = (int)BlockDataMatrix[i, j].ProtalOutBlock.transform.position.x;
            if (graph[ti, tj].marked == false)
            {
                graph[ti, tj].marked = true;
                graph[ti, tj].portalOut = true;
                graph[ti, tj].plotPosCameFrom = (j, i);
                graph[ti, tj].cost = aStarProperty.cost;
                graph[ti, tj].priority = graph[ti, tj].cost + Distance(graph[ti, tj].plotPos, endPoint);
                frontier.Add(graph[ti, tj]);
            }
        }
        for (int index = 0; index < jumpRange.Length; index++)
        {
            int ii = i + jumpRange[index].y;
            int jj = j + jumpRange[index].x;
            if (ii >= 0 && ii < iSize && jj >= 0 && jj < jSize && graph[ii, jj].Passable && !graph[ii, jj].marked)
            {
                graph[ii, jj].marked = true;
                graph[ii, jj].plotPosCameFrom = (j, i);
                graph[ii, jj].cost = aStarProperty.cost + 1;
                graph[ii, jj].priority = graph[ii, jj].cost + Distance(graph[ii, jj].plotPos, endPoint);
                frontier.Add(graph[ii, jj]);
            }
        }
    }

    private MoveParameters[] CorrectTmpPositions(MoveParameters[] tmpParameters, float entityR)
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
                        if (FirstBlockLine(tmpMoveParameters[i].targetPosition, tmpMoveParameters[i + 2].targetPosition, tmpMoveParameters[i + 1].targetPosition, entityR).x == -1000)
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
                                Vector2 tmp = FirstBlockLine(tmpMoveParameters[i].targetPosition, tmpCPos + alpha * tmpDPos, tmpCPos, entityR);
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
            //tmpMoveParameters.RemoveAt(0);
            return tmpMoveParameters.ToArray();
        }
        else
        {
            MoveParameters[] tmpVector2Parameters = { tmpParameters[0] };
            return tmpVector2Parameters;
        }
    }
    private Vector2 FirstBlockLine(Vector2 beginPos, Vector2 endPos, Vector2 originPosition, float entityR)
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
                    return BaseOnBlockNewPoint(BlockDataMatrix[p1.y, p1.x].transform.position, endPos, beginPos, entityR);
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
                    return BaseOnBlockNewPoint(BlockDataMatrix[(int)p1.y, (int)p1.x].transform.position, endPos, beginPos, entityR);
                }
            }
            return new Vector2(-1000, -1000);
        }
    }
    private bool IsBlocked(Vector2 beginPos, Vector2 endPos, float entityR)
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
    private Vector2 BaseOnBlockNewPoint(Vector2 blockCenterPosition, Vector2 endPosition, Vector2 startPosition, float entityR)
    {
        float k = entityR * 1.00001f + 0.5f;
        Vector2 sc = blockCenterPosition - startPosition;
        Vector2 se = endPosition - startPosition;
        se = ((sc.x * se.x + sc.y * se.y) / (se.x * se.x + se.y * se.y)) * se - sc;
        return blockCenterPosition + new Vector2(k * (se.x > 0 ? 1 : -1), k * (se.y > 0 ? 1 : -1));
    }
    public void CreateMap(GameObject map)
    {
        _map = UnityEngine.Object.Instantiate(map, LevelResourceSharing.LM);
    }
    public void Initialize()
    {
        MapInitialize();
    }

    public void ToStart()
    {

    }

    public void ToEnd()
    {
        UnityEngine.Object.Destroy(_map);
    }
}
