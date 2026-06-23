using JetBrains.Annotations;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class PathDataManager:IManagerStartEnd
{
    private static PathDataManager _instance;
    public static PathDataManager Manager
    {
        get
        {
            if(_instance == null)
                _instance=new PathDataManager();
            return _instance;
        }
    }
    private PathDataManager()
    {
        _mapDataManager = MapDataManager.Manager;
    }
    private struct Path
    {
        public Vector2[] CheckPoints;
        public float[] WaitTimes;
        public List<MoveParameters[]> MoveParameters0;
        public List<MoveParameters[]> MoveParameters1;
        public List<MoveParameters[]> MoveParameters2;
        public bool NeedUpdate;
        public Path(GameObject path)
        {
            int checkPointNum = path.transform.childCount;
            CheckPoints = new Vector2[checkPointNum];
            WaitTimes = new float[checkPointNum];
            for (int i = 0; i < checkPointNum; i++)
            {
                Transform checkPointi = path.transform.GetChild(i);
                CheckPoints[i] = checkPointi.position;
                WaitTimes[i] = float.Parse(checkPointi.name);
            }
            MoveParameters0 = new List<MoveParameters[]>();
            MoveParameters1 = new List<MoveParameters[]>();
            MoveParameters2 = new List<MoveParameters[]>();
            NeedUpdate = true;
        }
        // 新格式:直接吃 LevelData.PathData(由 editor Path Editing 写入)
        public Path(PathData data)
        {
            CheckPoints = data.CheckPoints ?? new Vector2[0];
            WaitTimes = data.WaitTimes ?? new float[0];
            // WaitTimes 长度可能 != CheckPoints,统一补齐
            if (WaitTimes.Length < CheckPoints.Length)
            {
                var grown = new float[CheckPoints.Length];
                System.Array.Copy(WaitTimes, grown, WaitTimes.Length);
                WaitTimes = grown;
            }
            MoveParameters0 = new List<MoveParameters[]>();
            MoveParameters1 = new List<MoveParameters[]>();
            MoveParameters2 = new List<MoveParameters[]>();
            NeedUpdate = true;
        }
    }
    private MapDataManager _mapDataManager;
    private Path[] _paths;

    public PathDataManager(GameObject[] paths)
    {
        CreatePaths(paths);
    }
    public void CreatePaths(GameObject[] paths)
    {
        _paths = new Path[paths.Length];
        float entityR = EntityManager.EntityR;
        for (int i = 0; i < _paths.Length; i++)
        {
            _paths[i] = new Path(paths[i]);
            for (int j = 0; j < _paths[i].CheckPoints.Length - 1; j++)
            {
                _paths[i].MoveParameters0.Add(_mapDataManager.AStarWayFinding(_paths[i].CheckPoints[j], _paths[i].CheckPoints[j + 1], 0));
                _paths[i].MoveParameters1.Add(_mapDataManager.AStarWayFinding(_paths[i].CheckPoints[j], _paths[i].CheckPoints[j + 1], 1));
                _paths[i].MoveParameters2.Add(_mapDataManager.AStarWayFinding(_paths[i].CheckPoints[j], _paths[i].CheckPoints[j + 1], 2));
            }
        }
    }
    // 新格式入口:从 LevelData.Paths[] 直接构造,不再依赖 prefab 旧格式
    public void CreatePaths(PathData[] paths)
    {
        _paths = new Path[paths != null ? paths.Length : 0];
        for (int i = 0; i < _paths.Length; i++)
        {
            _paths[i] = new Path(paths[i]);
            for (int j = 0; j < _paths[i].CheckPoints.Length - 1; j++)
            {
                _paths[i].MoveParameters0.Add(_mapDataManager.AStarWayFinding(_paths[i].CheckPoints[j], _paths[i].CheckPoints[j + 1], 0));
                _paths[i].MoveParameters1.Add(_mapDataManager.AStarWayFinding(_paths[i].CheckPoints[j], _paths[i].CheckPoints[j + 1], 1));
                _paths[i].MoveParameters2.Add(_mapDataManager.AStarWayFinding(_paths[i].CheckPoints[j], _paths[i].CheckPoints[j + 1], 2));
            }
        }
    }
    public Vector2 GetSectionBeginPos(int pathSerial)
    {
        return _paths[pathSerial].CheckPoints[0];
    }
    public (float, MoveParameters[]) GetSection(int pathSerial, int sectionSerial, int moveMethod)
    {
        Path path = _paths[pathSerial];
        if (sectionSerial < path.CheckPoints.Length - 1)
        {
            switch (moveMethod)
            {
                case 0: return (path.WaitTimes[sectionSerial], path.MoveParameters0[sectionSerial]);
                case 1: return (path.WaitTimes[sectionSerial], path.MoveParameters1[sectionSerial]);
                case 2: return (path.WaitTimes[sectionSerial], path.MoveParameters2[sectionSerial]);
                default: return (0, null);
            }
        }
        else
        {
            return (path.WaitTimes[sectionSerial], null);
        }
    }
    public float GetLength(int pathSerial, int sectionSerial, int moveMethod)
    {
        float length = 0;
        List<MoveParameters[]> moveParameters = moveMethod switch
        {
            0 => _paths[pathSerial].MoveParameters0,
            1 => _paths[pathSerial].MoveParameters1,
            _ => _paths[pathSerial].MoveParameters2,
        };
        for (int i = sectionSerial; i < moveParameters.Count; i++)
        {
            for (int j = 0; j < moveParameters[i].Length - 1; j++)
            {
                length += Vector2.Distance(moveParameters[i][j].targetPosition, moveParameters[i][j + 1].targetPosition);
            }
        }
        return length;
    }

    public void Initialize()
    {
        
    }

    public void ToStart()
    {
        
    }

    public void ToEnd()
    {

    }
}
