using Cysharp.Threading.Tasks;
using MyUI;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

public class LevelActionManager : IManagerStartEnd
{
    private static LevelActionManager _instance;
    public static LevelActionManager Manager
    {
        get
        {
            if (_instance == null)
                _instance = new LevelActionManager();
            return _instance;
        }
    }
    private LevelActionManager()
    {
        _printerTemplateWalk = Resources.Load<TrailRenderer>("Prefabs/PathPrinter/Printer_walk");
        _printerTemplateFly = Resources.Load<TrailRenderer>("Prefabs/PathPrinter/Printer_fly");
        _printerWalk = new List<TrailRenderer>();
        _printerFly = new List<TrailRenderer>();
        _printerPoolWalk = new ObjectPool<TrailRenderer>(
            () => Object.Instantiate(_printerTemplateWalk, LevelResourceSharing.LM),
            actionOnRelease: printer => printer.gameObject.SetActive(false),
            collectionCheck: true);
        _printerPoolFly = new ObjectPool<TrailRenderer>(
            () => Object.Instantiate(_printerTemplateFly, LevelResourceSharing.LM),
            actionOnRelease: printer => printer.gameObject.SetActive(false),
            collectionCheck: true);
    }
    private LevelActions.Wave[] _waves;
    private List<Entity> _waveEntities;
    private int _currentIndex;
    private int _actionProcessNum;
    private bool _holdingWaveWhileExistWaveEntities;

    private TrailRenderer _printerTemplateWalk;
    private TrailRenderer _printerTemplateFly;
    // 走/飞两条通道各自的空闲栈；_printerWalk/_printerFly 为在用列表（关末全量归还）
    private ObjectPool<TrailRenderer> _printerPoolWalk;
    private List<TrailRenderer> _printerWalk;
    private ObjectPool<TrailRenderer> _printerPoolFly;
    private List<TrailRenderer> _printerFly;
    private float printerMoveSpeed = 8;
    private float printerLifeTime = 0.8f;



    private async UniTaskVoid WaveProcess(LevelActions.Wave wave, CancellationToken cancellationToken)
    {
        _holdingWaveWhileExistWaveEntities = true;
        var scheduled = LevelActionScheduler.CollectAndSortActions(wave);
        _actionProcessNum = scheduled.Count;
        float waveStartTime = Time.time;
        int total = scheduled.Count;

        for (int i = 0; i < total; i++)
        {
            var entry = scheduled[i];
            float dueTime = waveStartTime + Mathf.Max(0f, entry.Action.TriggerTime);
            float wait = dueTime - Time.time;
            if (wait > 0f)
                await UniTask.WaitForSeconds(wait, false, PlayerLoopTiming.Update, cancellationToken);
            ActionProcess(entry.Action, LevelResourceSharing.LevelCtk).Forget();
        }
    }
    private async UniTaskVoid ActionProcess(LevelActions.Action action, CancellationToken cancellationToken)
    {
        for (int i = 0; i < action.GapsFromLastRepeat.Length; i++)
        {
            if (action.GapsFromLastRepeat[i] > 0)
            {
                await UniTask.WaitForSeconds(action.GapsFromLastRepeat[i], false, PlayerLoopTiming.Update, cancellationToken);
            }
            ActionRepeat(action);
        }
        _actionProcessNum--;
        TryAdvanceWave();
    }
    private async UniTaskVoid PathPrinterMove(TrailRenderer pathPrinter, int pathSerial, int sectionSerial, int pointSerial, int moveMethod, CancellationToken cancellationToken)
    {
        await UniTask.WaitForFixedUpdate(cancellationToken);
        MoveParameters[] currentSection = PathDataManager.Manager.GetSection(pathSerial, sectionSerial, moveMethod).Item2;
        while (currentSection != null)
        {
            float deltas = printerMoveSpeed * Time.fixedDeltaTime;
            while (currentSection != null && deltas > 0)
            {
                for (; pointSerial < currentSection.Length; pointSerial++)
                {
                    float d = Vector2.Distance(currentSection[pointSerial].targetPosition, pathPrinter.transform.position);
                    if (deltas < d)
                    {
                        pathPrinter.transform.position += new Vector3(currentSection[pointSerial].targetPosition.x - pathPrinter.transform.position.x, currentSection[pointSerial].targetPosition.y - pathPrinter.transform.position.y, 0).normalized * deltas;
                        deltas = 0;
                        break;
                    }
                    else
                    {
                        deltas -= d;
                        pathPrinter.transform.position = currentSection[pointSerial].targetPosition;
                        if (currentSection[pointSerial].whetherToEnterPortal)
                        {
                            SetPathPrinter(currentSection[pointSerial + 1].targetPosition, pathSerial, sectionSerial, pointSerial + 1, moveMethod);
                            await UniTask.WaitForSeconds(printerLifeTime, false, PlayerLoopTiming.Update, cancellationToken);
                            ReturnPathPrinter(pathPrinter, moveMethod);
                            return;
                        }
                    }
                }
                if (pointSerial == currentSection.Length)
                {
                    sectionSerial++;
                    currentSection = PathDataManager.Manager.GetSection(pathSerial, sectionSerial, moveMethod).Item2;
                    pointSerial = 0;
                }
            }
            await UniTask.WaitForFixedUpdate(cancellationToken);
        }
        await UniTask.WaitForSeconds(printerLifeTime, false, PlayerLoopTiming.Update, cancellationToken);
        ReturnPathPrinter(pathPrinter, moveMethod);
    }
    private void ActionRepeat(LevelActions.Action action)
    {

        switch (action.CommandType)
        {
            case 0:
                Entity movableEntity = EntityManager.Manager.SetMovableEntity(action.EntityPrefabID, PathDataManager.Manager.GetSectionBeginPos(action.PathSerial), action.Camp, action.PathSerial);
                action.OnActionRepeat?.Invoke(movableEntity);
                break;
            case 1:
                Entity staticEntity = EntityManager.Manager.SetStaticEntity(action.EntityPrefabID, action.Destination, action.Camp, action.Orientation);
                action.OnActionRepeat?.Invoke(staticEntity);
                break;
            case 2:
                SetPathPrinter(PathDataManager.Manager.GetSectionBeginPos(action.PathSerial), action.PathSerial, 0, 0, 0);
                break;
            case 3:
                SetPathPrinter(PathDataManager.Manager.GetSectionBeginPos(action.PathSerial), action.PathSerial, 0, 0, 1);
                break;
            case 4:
                SetPathPrinter(PathDataManager.Manager.GetSectionBeginPos(action.PathSerial), action.PathSerial, 0, 0, 2);
                break;
            default: break;
        }
    }
    public int CountTotalNeedOperateNum()
    {
        int num = 0;
        for (int i = 0; i < _waves.Length; i++)
        {
            if (_waves[i].Tracks == null) continue;
            for (int t = 0; t < _waves[i].Tracks.Length; t++)
            {
                if (_waves[i].Tracks[t].Locked) continue;  // 未激活 Track:不贡献 entity 数
                var actions = _waves[i].Tracks[t].Actions;
                if (actions == null) continue;
                for (int j = 0; j < actions.Length; j++)
                {
                    LevelActions.Action action = actions[j];
                    if (action.CommandType == 0)
                    {
                        num += action.GapsFromLastRepeat.Length;
                    }
                }
            }
        }
        return num;
    }
    public void AddToWaveEntities(Entity entity)
    {
        if (entity.EntityData.MonsterIsPrimary)
            _waveEntities.Add(entity);
    }
    public void RemoveFromWaveEntities(Entity entity)
    {
        if (_waveEntities.Remove(entity))
        {
            TryAdvanceWave();
        }
    }
    public void ReleaseCurrentWave()
    {
        _holdingWaveWhileExistWaveEntities = false;
        TryAdvanceWave();
    }
    private void TryAdvanceWave()
    {
        if (_actionProcessNum != 0) return;
        bool okToAdvance = !_holdingWaveWhileExistWaveEntities || _waveEntities.Count == 0;
        if (!okToAdvance) return;

        if (_currentIndex < _waves.Length - 1)
        {
            _currentIndex++;
            WaveProcess(_waves[_currentIndex], LevelResourceSharing.LevelCtk).Forget();
        }
        else
        {
            MissionEnd(true);
        }
    }
    public void SetEntityPrefabTypesAndWaves(LevelActions.Wave[] waves)
    {
        _waves = waves;
    }
    public void MissionEnd(bool win)
    {
        if (LevelResourceSharing.LevelCtk != default)
        {
            AudioManager.Manager.PlayAudio(win ? "win" : "lose", 1, false, false);
            SettlementPanel.Panel.SetDatas($"{LevelResourceSharing.LD.LevelCode}  {LevelResourceSharing.LD.LevelName}", win, 0, LevelMessagePanel.Panel.DamageStatisticDatas);
            // 必须先 LevelEnd 再 Push：ToEnd 会把 _turrets 里的静态干员还池，触发
            // InteractableStatic.Dormancy → EntityBackToSelector 回填 _placeDataList。
            // 若先 Push，OnPause 会先把列表清空，EntityBackToSelector 在空列表上调用
            // AddStaticEntityPrefabToSelector 凭空塞一条记录，下一场 OnEnter 追加 placeData
            // 时会复制一份，部署/撤退后 bench 上出现重复图标。
            LevelResourceSharing.LevelEnd();
            PanelManager.Push(SettlementPanel.Panel);
        }
    }
    public void SetPathPrinter(Vector2 destination, int pathSerial, int sectionSerial, int pointSerial, int moveMethod)
    {
        ObjectPool<TrailRenderer> printerPool;
        List<TrailRenderer> printerReceive;
        if (moveMethod <= 1)
        {
            printerPool = _printerPoolWalk;
            printerReceive = _printerWalk;
        }
        else
        {
            printerPool = _printerPoolFly;
            printerReceive = _printerFly;
        }

        TrailRenderer printer = printerPool.Get();
        printerReceive.Add(printer);
        printer.transform.position = destination;
        printer.gameObject.SetActive(true);
        PathPrinterMove(printer, pathSerial, sectionSerial, pointSerial, moveMethod, LevelResourceSharing.LevelCtk).Forget();
    }
    public void ReturnPathPrinter(TrailRenderer pathPrinter, int moveMethod)
    {
        ObjectPool<TrailRenderer> printerPool;
        List<TrailRenderer> printerReceive;
        if (moveMethod <= 1)
        {
            printerPool = _printerPoolWalk;
            printerReceive = _printerWalk;
        }
        else
        {
            printerPool = _printerPoolFly;
            printerReceive = _printerFly;
        }
        // 归还与借出一一配对（PathPrinterMove 生命周期终点或关末 ToEnd）：
        // 协程被取消时以异常退出、不会走到归还，双归还即逻辑错误，由池的
        // collectionCheck 抛出
        printerReceive.Remove(pathPrinter);
        printerPool.Release(pathPrinter);
    }

    public void Initialize()
    {
        _currentIndex = 0;
        _waveEntities = new List<Entity>();
        // 从 actions 扫描派生 ID -> 召唤次数,直接喂给 EntityPoolManager(不再走 WaveEntityPrefabIDs 索引)
        Dictionary<EntityID, int> entityNum = new Dictionary<EntityID, int>();
        for (int i = 0; i < _waves.Length; i++)
        {
            LevelActions.Wave wave = _waves[i];
            if (wave.Tracks == null) continue;
            for (int t = 0; t < wave.Tracks.Length; t++)
            {
                if (wave.Tracks[t].Locked) continue;  // 未激活 Track:其 Action 不参与 entityNum 扫描
                LevelActions.Action[] actions = wave.Tracks[t].Actions;
                if (actions == null) continue;
                for (int j = 0; j < actions.Length; j++)
                {
                    LevelActions.Action action = actions[j];
                    int perAction = action.CommandType switch
                    {
                        0 => action.GapsFromLastRepeat.Length,
                        1 => 1,
                        _ => 0,
                    };
                    if (perAction > 0 && !action.EntityPrefabID.IsNull)
                    {
                        if (!entityNum.ContainsKey(action.EntityPrefabID))
                            entityNum[action.EntityPrefabID] = 0;
                        entityNum[action.EntityPrefabID] += perAction;
                    }
                    switch (action.CommandType)
                    {
                        case 0: break;
                        case 1: break;
                        case 2: actions[j] = WithGaps(action, new float[2] { 0, printerLifeTime }); break;
                        case 3: actions[j] = WithGaps(action, new float[2] { 0, printerLifeTime }); break;
                        case 4: actions[j] = WithGaps(action, new float[2] { 0, printerLifeTime }); break;
                        case 5: break;
                        case 6: break;
                    }
                }
            }
        }
        EntityPoolManager.Manager.CreateOrExpandEntityPool(entityNum);
    }

    // 辅助:struct Action 改字段后回写
    static LevelActions.Action WithGaps(LevelActions.Action a, float[] gaps)
    {
        a.GapsFromLastRepeat = gaps;
        return a;
    }
    public void ToStart()
    {
        WaveProcess(_waves[_currentIndex], LevelResourceSharing.LevelCtk).Forget();
    }
    public void ToEnd()
    {
        _waveEntities.Clear();
        for (int i = _printerWalk.Count - 1; i >= 0; i--)
        {
            ReturnPathPrinter(_printerWalk[i], 1);
        }
        for (int i = _printerFly.Count - 1; i >= 0; i--)
        {
            ReturnPathPrinter(_printerFly[i], 2);
        }
    }


}
