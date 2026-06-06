
using System.Threading;
using Codice.CM.Client.Differences.Merge;
using UnityEngine;

public interface IManagerStartEnd
{
    /// <summary>
    /// Managers初始化调用函数
    /// </summary>
    void Initialize();
    /// <summary>
    /// Managers开始调用函数
    /// </summary>
    void ToStart();
    /// <summary>
    /// Managers结束调用函数
    /// </summary>
    void ToEnd();
}
public class LevelResourceSharing
{
    public static GameObject EnvironmentalControlDevice;
    public static CancellationToken LevelCtk;
    public static Transform LM;
    public static LevelData LD;
    public static Camera MainCamera;
    private static CancellationTokenSource _levelCts;

    public static void LevelInitialize()
    {
        LM = GameObject.Find("LM").transform;
        MainCamera = GameObject.Find("MCam").GetComponent<Camera>();
        MainCamera.transform.position = new Vector3(LD.CameraPos.x, LD.CameraPos.y, -1);
        MainCamera.orthographicSize = LD.CameraSize;
        EntityManager.Manager.Initialize();
        EntityPoolManager.Manager.Initialize();
        EffectManager.Manager.Initialize();
        MapDataManager.Manager.CreateMap(LD.MapPrefab);
        MapDataManager.Manager.Initialize();
        PathDataManager.Manager.CreatePaths(LD.CheckPoints);
        PathDataManager.Manager.Initialize();
        SlidersManager.Manager.Initialize();
        LevelActionManager.Manager.SetEntityPrefabTypesAndWaves(LD.Waves, LD.WaveEntityPrefabIDs);
        LevelActionManager.Manager.Initialize();
        LevelRescurceManager.Manager.CanSetNumLeft = LD.CanSetNum;
        LevelRescurceManager.Manager.LevelHpLeft = LD.LevelHp;
        LevelRescurceManager.Manager.NeedOperateCount = LevelActionManager.Manager.CountTotalNeedOperateNum();
        LevelRescurceManager.Manager.CurrentOperateCount = 0;
        LevelRescurceManager.Manager.SetCostMessage(LD.Cost0, LD.MaxCost, LD.CostRecoverSpeed);
        LevelRescurceManager.Manager.Initialize();
        if (LD.EnvironmentalControlDevice != null)
        {
            EnvironmentalControlDevice = Object.Instantiate(LD.EnvironmentalControlDevice, LM);
            if (EnvironmentalControlDevice.TryGetComponent(out IManagerStartEnd iManagerStartEnd))
            {
                iManagerStartEnd.Initialize();
            }
        }
    }
    public static void LevelStart()
    {
        _levelCts = new CancellationTokenSource();
        LevelCtk = _levelCts.Token;
        EntityManager.Manager.ToStart();
        EntityPoolManager.Manager.ToStart();
        EffectManager.Manager.ToStart();
        MapDataManager.Manager.ToStart();
        PathDataManager.Manager.ToStart();
        SlidersManager.Manager.ToStart();
        LevelActionManager.Manager.ToStart();
        LevelRescurceManager.Manager.ToStart();
        if (EnvironmentalControlDevice != null && EnvironmentalControlDevice.TryGetComponent(out IManagerStartEnd iManagerStartEnd))
        {
            iManagerStartEnd.ToStart();
        }
    }
    public static void LevelEnd()
    {
        LevelActionManager.Manager.ToEnd();
        EntityManager.Manager.ToEnd();
        EntityPoolManager.Manager.ToEnd();
        EffectManager.Manager.ToEnd();
        MapDataManager.Manager.ToEnd();
        PathDataManager.Manager.ToEnd();
        SlidersManager.Manager.ToEnd();
        LevelRescurceManager.Manager.ToEnd();
        if (EnvironmentalControlDevice != null && EnvironmentalControlDevice.TryGetComponent(out IManagerStartEnd iManagerStartEnd))
        {
            iManagerStartEnd.ToEnd();
            Object.Destroy(EnvironmentalControlDevice);
        }
        EnvironmentalControlDevice = null;
        Debug.Log(_levelCts);
        _levelCts.Cancel();
        _levelCts = null;
        LevelCtk = default;
    }
}
