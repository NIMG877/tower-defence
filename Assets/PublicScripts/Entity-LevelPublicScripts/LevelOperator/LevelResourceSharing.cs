
using System.Threading;
using AbilitySystem;
using Codice.CM.Client.Differences.Merge;
using UnityEngine;

public interface IManagerStartEnd
{
    /// <summary>
    /// Managers��ʼ�����ú���
    /// </summary>
    void Initialize();
    /// <summary>
    /// Managers��ʼ���ú���
    /// </summary>
    void ToStart();
    /// <summary>
    /// Managers�������ú���
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
    public static Camera UICamera;
    private static CancellationTokenSource _levelCts;

    public static void LevelInitialize()
    {
        LM = GameObject.Find("LM").transform;
        MainCamera = GameObject.Find("MCam").GetComponent<Camera>();
        MainCamera.transform.position = new Vector3(LD.CameraPos.x, LD.CameraPos.y, -1);
        MainCamera.orthographicSize = LD.CameraSize;
        UICamera = GameObject.Find("UICam").GetComponent<Camera>();
        UICamera.transform.position = new Vector3(LD.CameraPos.x, LD.CameraPos.y, 0);
        UICamera.orthographicSize = LD.CameraSize;
        EntityManager.Manager.Initialize();
        EntityPoolManager.Manager.Initialize();
        EffectManager.Manager.Initialize();
        MapDataManager.Manager.CreateMap(LD.MapPrefab);
        MapDataManager.Manager.AttachLevelData(LD);
        MapDataManager.Manager.Initialize();
        PathDataManager.Manager.CreatePaths(LD.Paths);
        PathDataManager.Manager.Initialize();
        SlidersManager.Manager.Initialize();
        LevelActionManager.Manager.SetEntityPrefabTypesAndWaves(LD.Waves);
        LevelActionManager.Manager.Initialize();
        DetachedStepScheduler.Manager.Initialize();
        LevelResourceManager.Manager.CanSetNumLeft = LD.CanSetNum;
        LevelResourceManager.Manager.LevelHpLeft = LD.LevelHp;
        LevelResourceManager.Manager.NeedOperateCount = LevelActionManager.Manager.CountTotalNeedOperateNum();
        LevelResourceManager.Manager.CurrentOperateCount = 0;
        LevelResourceManager.Manager.SetCostMessage(LD.Cost0, LD.MaxCost, LD.CostRecoverSpeed);
        LevelResourceManager.Manager.Initialize();
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
        DetachedStepScheduler.Manager.ToStart();
        LevelResourceManager.Manager.ToStart();
        if (EnvironmentalControlDevice != null && EnvironmentalControlDevice.TryGetComponent(out IManagerStartEnd iManagerStartEnd))
        {
            iManagerStartEnd.ToStart();
        }
    }
    public static void LevelEnd()
    {
        Bullet.ReturnAllActive();
        LevelActionManager.Manager.ToEnd();
        DetachedStepScheduler.Manager.ToEnd();
        EntityManager.Manager.ToEnd();
        EntityPoolManager.Manager.ToEnd();
        EffectManager.Manager.ToEnd();
        MapDataManager.Manager.ToEnd();
        PathDataManager.Manager.ToEnd();
        SlidersManager.Manager.ToEnd();
        LevelResourceManager.Manager.ToEnd();
        if (EnvironmentalControlDevice != null && EnvironmentalControlDevice.TryGetComponent(out IManagerStartEnd iManagerStartEnd))
        {
            iManagerStartEnd.ToEnd();
            Object.Destroy(EnvironmentalControlDevice);
        }
        EnvironmentalControlDevice = null;
        _levelCts.Cancel();
        _levelCts = null;
        LevelCtk = default;
    }
}
