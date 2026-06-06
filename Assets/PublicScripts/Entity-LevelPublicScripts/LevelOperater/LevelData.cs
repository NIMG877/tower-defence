using UnityEngine;
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
    public GameObject[] CheckPoints;
    public EntityID[] WaveEntityPrefabIDs;
    public int LevelHp;
    public int Cost0;
    public int MaxCost;
    public int CanSetNum;
    public float CostRecoverSpeed;
}
