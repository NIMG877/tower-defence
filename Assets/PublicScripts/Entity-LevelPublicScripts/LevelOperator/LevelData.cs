using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct PathData
{
    public string Name;            // 编辑器内标识,可空(为空时退化为 "Path {i}")
    public Vector2[] CheckPoints;
    public float[]   WaitTimes;
}

/// <summary>关卡环境设备类型：LevelInitialize 时由 <see cref="LevelResourceSharing"/> 按此枚举
/// 实例化对应普通类（实现 <see cref="IManagerStartEnd"/>），LevelStart/LevelEnd 驱动其生命周期。</summary>
public enum EnvironmentalDeviceKind
{
    None,
    MCHunger,
}

[CreateAssetMenu]
public class LevelData : ScriptableObject
{
    public string LevelName;
    public string LevelCode;
    public string LevelDescription;
    public float CameraSize;
    public Vector2 CameraPos;
    public Texture2D CutToLevelTexture;
    [Space(10)]
    public GameObject MapPrefab;
    [Header("Map data (new)")]
    public int iSize;
    public int jSize;
    public List<Tile> MapData = new List<Tile>();
    public EnvironmentalDeviceKind EnvironmentalControlDevice;
    public LevelActions.Wave[] Waves;
    public PathData[] Paths = new PathData[0];
    public int LevelHp;
    public int Cost0;
    public int MaxCost;
    public int CanSetNum;
    public float CostRecoverSpeed;
}
