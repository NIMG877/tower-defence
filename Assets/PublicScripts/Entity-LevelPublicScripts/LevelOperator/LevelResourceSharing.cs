
using System.Threading;
using AbilitySystem;
using UnityEngine;

public interface IManagerStartEnd
{
    /// <summary>
    /// 关卡装载时由 <see cref="LevelResourceSharing.LevelInitialize"/> 对各实现方依次调用。
    /// 调用前该实现方的关卡数据已注入（地图/路径/波次/费用等经 CreateMap、CreatePaths、
    /// SetEntityPrefabTypesAndWaves、SetCostMessage 先行写入）。
    /// 实现方在此做一次性构建（索引、缓存、对象池、实例注册），不依赖运行期进度，
    /// 也不在此启动随帧推进的逻辑。
    /// </summary>
    void Initialize();
    /// <summary>
    /// 本局开始时由 <see cref="LevelResourceSharing.LevelStart"/> 调用；届时取消令牌
    /// <see cref="LevelResourceSharing.LevelCtk"/> 已就绪，各实现方的 Initialize 均已完成。
    /// 实现方在此激活本局运行：复位运行时状态、启动波次/调度/费用恢复等异步循环
    /// （应绑定 LevelCtk）、订阅运行期事件。
    /// </summary>
    void ToStart();
    /// <summary>
    /// 关卡结束时由 <see cref="LevelResourceSharing.LevelEnd"/> 按固定顺序依次调用
    /// （动态弹体已先行归还；全部调用结束后才取消 LevelCtk，异步循环此时仍在运行，
    /// 实现方须在此自行停摆）。实现方在此清理本局运行时状态：归还/销毁池对象与关卡产物、
    /// 退订事件、复位标志。本方法可能在局中经 MissionEnd/败北路径触发，且可能沿回池
    /// 通知链重入，清理逻辑须可容忍重入。
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
