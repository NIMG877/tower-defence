using Cysharp.Threading.Tasks;
using MyUI;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

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
        _printerPoolWalk = new List<TrailRenderer>() { Object.Instantiate(Resources.Load<TrailRenderer>("Prefabs/PathPrinter/Printer_walk")) };
        _printerPoolFly = new List<TrailRenderer>() { Object.Instantiate(Resources.Load<TrailRenderer>("Prefabs/PathPrinter/Printer_fly")) };
        _printerWalk = new List<TrailRenderer>();
        _printerFly = new List<TrailRenderer>();
    }
    private EntityID[] _entityIDs;
    private LevelActions.Wave[] _waves;
    private List<Entity> _waveEntities;
    private int _currentIndex;
    private int _actionProcessNum;
    private bool _holdingWaveWhileExistWaveEntities;

    private List<TrailRenderer> _printerPoolWalk;
    private List<TrailRenderer> _printerWalk;
    private List<TrailRenderer> _printerPoolFly;
    private List<TrailRenderer> _printerFly;
    private float printerMoveSpeed = 8;
    private float printerLifeTime = 0.8f;



    private async void WaveProcess(LevelActions.Wave wave, CancellationToken cancellationToken)
    {
        _holdingWaveWhileExistWaveEntities = true;
        LevelActions.Action[] actions = wave.Actions;
        _actionProcessNum = actions.Length;
        for (int i = 0; i < actions.Length; i++)
        {
            if (actions[i].GapFromLastAction > 0)
            {
                await UniTask.WaitForSeconds(actions[i].GapFromLastAction, false, PlayerLoopTiming.Update, cancellationToken);
            }
            ActionProcess(actions[i], LevelResourceSharing.LevelCtk);
        }
        //_cancellationTokens.Remove(cancellationToken);
    }
    private async void ActionProcess(LevelActions.Action action, CancellationToken cancellationToken)
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
        if (!_holdingWaveWhileExistWaveEntities && _actionProcessNum == 0)
        {
            if (_currentIndex < _waves.Length - 1)
            {
                _currentIndex++;
                WaveProcess(_waves[_currentIndex], LevelResourceSharing.LevelCtk);
            }
            else
            {
                MissionEnd(true);
            }
        }
        //_cancellationTokenSources.Remove(cancellationToken);
    }
    private async void PathPrinterMove(TrailRenderer pathPrinter, int pathSerial, int sectionSerial, int pointSerial, int moveMethod, CancellationToken cancellationToken)
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
                            //pathPrinter.enabled = false;
                            //pathPrinter.transform.position = currentSection[pointSerial + 1].targetPosition;
                            //pathPrinter.enabled = true;
                            ////Debug.Log("arrive portal" +$"{currentSection[pointSerial + 1].targetPosition},{pathSerial},{sectionSerial},{pointSerial + 1}");
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
                Entity movableEntity = EntityManager.Manager.SetMovableEntity(_entityIDs[action.EntityPrefabSerial], PathDataManager.Manager.GetSectionBeginPos(action.PathSerial), action.Camp, action.PathSerial);
                action.OnActionRepeat?.Invoke(movableEntity);
                break;
            case 1:
                Entity staticEntity = EntityManager.Manager.SetStaticEntity(_entityIDs[action.EntityPrefabSerial], action.Destination, action.Camp, action.Orientation);
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
            for (int j = 0; j < _waves[i].Actions.Length; j++)
            {
                LevelActions.Action action = _waves[i].Actions[j];
                if (action.CommandType == 0)
                {
                    if (action.ModifyAttributes && action.ModifyCountOperate)
                    {
                        num += action.GapsFromLastRepeat.Length;
                    }
                    else
                    {
                        num += action.GapsFromLastRepeat.Length;
                    }
                    // ԭӦļм¼ǷΪĬϼ¼ؿǣ+1
                    // else if (_entityIDs[action.EntityPrefabSerial].TryGetComponent(out StaticEntityAttributes sea) && sea.CountOperated)
                    // {
                    //     num += action.GapsFromLastRepeat.Length;
                    // }
                    
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
            if (_waveEntities.Count == 0 && _actionProcessNum == 0)
            {
                if (_currentIndex < _waves.Length - 1)
                {
                    _currentIndex++;
                    WaveProcess(_waves[_currentIndex], LevelResourceSharing.LevelCtk);
                }
                else
                {
                    MissionEnd(true);
                }
            }
        }
    }
    public void ReleaseCurrentWave()
    {
        _holdingWaveWhileExistWaveEntities = false;
        if (_actionProcessNum == 0)
        {
            if (_currentIndex < _waves.Length - 1)
            {
                _currentIndex++;
                WaveProcess(_waves[_currentIndex], LevelResourceSharing.LevelCtk);
            }
            else
            {
                MissionEnd(true);
            }
        }
    }
    public void SetEntityPrefabTypesAndWaves(LevelActions.Wave[] waves, EntityID[] prefab_ids)
    {
        _waves = waves;
        _entityIDs = prefab_ids;
    }
    public void MissionEnd(bool win)
    {
        if (LevelResourceSharing.LevelCtk != default)
        {
            AudioManager.Manager.PlayAudio(win ? "win" : "lose", 1, false, false);
            SettlementPanel.Panel.SetDatas($"{LevelResourceSharing.LD.LevelCode}  {LevelResourceSharing.LD.LevelName}", win, 0, LevelMessagePanel.Panel.DamageStatisticDatas);
            PanelManager.Push(SettlementPanel.Panel);
            LevelResourceSharing.LevelEnd();
        }
    }
    public void SetPathPrinter(Vector2 destination, int pathSerial, int sectionSerial, int pointSerial, int moveMethod)
    {
        List<TrailRenderer> printerPool;
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

        TrailRenderer printer;
        if (printerPool.Count > 1)
        {
            printer = printerPool[1];
            printerPool.RemoveAt(1);
        }
        else
        {
            printer = Object.Instantiate(printerPool[0], LevelResourceSharing.LM);
        }
        printerReceive.Add(printer);
        printer.transform.position = destination;
        printer.gameObject.SetActive(true);
        PathPrinterMove(printer, pathSerial, sectionSerial, pointSerial, moveMethod, LevelResourceSharing.LevelCtk);
    }
    public void ReturnPathPrinter(TrailRenderer pathPrinter, int moveMethod)
    {
        List<TrailRenderer> printerPool;
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
        pathPrinter.gameObject.SetActive(false);
        printerReceive.Remove(pathPrinter);
        printerPool.Add(pathPrinter);
    }

    public void Initialize()
    {
        _currentIndex = 0;
        _waveEntities = new List<Entity>();
        int[] entityNum = new int[_entityIDs.Length];
        for (int i = 0; i < _waves.Length; i++)
        {
            LevelActions.Wave wave = _waves[i];
            for (int j = 0; j < wave.Actions.Length; j++)
            {
                LevelActions.Action action = wave.Actions[j];
                entityNum[action.EntityPrefabSerial] += action.CommandType switch
                {
                    0 => action.GapsFromLastRepeat.Length,
                    1 => 1,
                    _ => 0,
                };
                switch (_waves[i].Actions[j].CommandType)
                {
                    case 0: break;
                    case 1: break;
                    case 2: _waves[i].Actions[j].GapsFromLastRepeat = new float[2] { 0, printerLifeTime }; break;
                    case 3: _waves[i].Actions[j].GapsFromLastRepeat = new float[2] { 0, printerLifeTime }; break;
                    case 4: _waves[i].Actions[j].GapsFromLastRepeat = new float[2] { 0, printerLifeTime }; break;
                    case 5: break;
                    case 6: break;
                }
            }
        }
        EntityPoolManager.Manager.CreateOrExpandEntityPool(_entityIDs, entityNum);
    }
    public void ToStart()
    {
        WaveProcess(_waves[_currentIndex], LevelResourceSharing.LevelCtk);
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
