using UnityEngine;

[System.Serializable]
public struct PathData
{
    public Vector2[] CheckPoints;
    public float[]   WaitTimes;
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
    public GameObject EnvironmentalControlDevice;
    public LevelActions.Wave[] Waves;
    [System.Obsolete("Use Paths[] instead. Kept temporarily for legacy asset migration.")]
    public GameObject[] CheckPoints;
    public PathData[] Paths = new PathData[0];
    public EntityID[] WaveEntityPrefabIDs;
    public int LevelHp;
    public int Cost0;
    public int MaxCost;
    public int CanSetNum;
    public float CostRecoverSpeed;
}
