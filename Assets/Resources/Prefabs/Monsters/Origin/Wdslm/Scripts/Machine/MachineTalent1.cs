using Cysharp.Threading.Tasks;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.EventSystems.EventTrigger;

public class MachineTalent1 : Talent
{
    [SerializeField] private Transform _projectionRoot;
    private Skill[] _targetAbilities;
    private MoveBase _thisMove;
    private BuffController _thisBuff;
    private Buff _moveBuff;
    private int[,] _bestOrientation;
    private float _acc;
    private float _dcc;
    private float _v;
    private float _findGap;
    public override void Initialize()
    {
        base.Initialize();
        _acc = 1.2f;
        _dcc = 3;
        _v = 0.001f;
        _thisMove = _thisEntity.MoveBase;
        _thisBuff = _thisEntity.buffController;
        _moveBuff = _thisBuff.CreateBuff(new BuffType[1] { BuffType.mspeed_delta_percent }, null, "machine", new float[1] { _v - 1 }, -5, false);
        (int iSize, int jSize) = MapDataManager.Manager.MapSize;
        _bestOrientation = new int[iSize, jSize];
    }

    public void ProjectEntity(Entity entity, Skill wdlsmSkill3)
    {
        if (entity.MoveBase != null)
        {
            //ɾ���ƶ�ģ��
        }
        List<Skill> targetAbilities = new List<Skill>(entity.abilities);
        for (int i = targetAbilities.Count - 1; i >= 0; i--)
        {
            if (targetAbilities[i].SkillOpenMode < 3)
                targetAbilities.RemoveAt(i);
        }
        _targetAbilities = targetAbilities.ToArray();
        Destroy(entity.TempContainer.Find("shadow(Clone)").gameObject);
        EntityPassiveUpdate(entity, wdlsmSkill3);
        MoveSpeedAD();
    }
    private async void EntityPassiveUpdate(Entity entity, Skill skill)
    {
        while (_thisEntity.Stats.IsActive)
        {
            if (!entity.Stats.IsActive)
            {
                skill.SkillEnd();
                return;
            }
            for (int i = 0; i < _targetAbilities.Length; i++)
            {
                if (_targetAbilities[i].SkillCanBegin())
                {
                    _targetAbilities[i].SkillBegin();
                }
            }
            if (_findGap <= 0)
            {
                _findGap = 100000000;
                FindBestPoint(1, entity);
            }
            else
            {
                _findGap -= Time.fixedDeltaTime;
            }

            if (((int)(entity.Movement.Position.x + 0.5), (int)(entity.Movement.Position.y + 0.5)) != ((int)(_projectionRoot.transform.position.x + 0.5), (int)(_projectionRoot.transform.position.y + 0.5)))
            {
                entity.Movement.Position = _projectionRoot.transform.position;
                int bestO = _bestOrientation[(int)(entity.Movement.Position.y + 0.5), (int)(entity.Movement.Position.x + 0.5)];
                if (bestO >= 0)
                {
                    entity.SetOrientation(bestO);
                }
                else
                {
                    entity.Vision.Range = entity.Vision.BaseRange;
                }
                ShowVision();
            }
            else
            {
                entity.Movement.Position = _projectionRoot.transform.position;
            }
            await UniTask.WaitForFixedUpdate(LevelResourceSharing.LevelCtk);
        }
    }
    private async void MoveSpeedAD()
    {
        while (_thisEntity.Stats.IsActive)
        {
            SlidersManager.Manager.TakeOverSliderMove();
            float disToPoint = Vector2.Distance(_thisMove.CurrentSection[_thisMove.CurrentPointSerial].targetPosition, _thisEntity.Movement.Position);
            float maxV = Mathf.Sqrt(_dcc * disToPoint * 2);
            if (_v + Time.fixedDeltaTime * _acc < maxV)
            {
                _v += Time.fixedDeltaTime * _acc;
            }
            else
            {
                _v = maxV;
            }
            _thisBuff.SetBuffValues(new float[1] { _v - 1 }, _moveBuff);
            await UniTask.WaitForFixedUpdate(LevelResourceSharing.LevelCtk);
        }
    }
    private (int maxOrientation, int maxNum) FindBestOrientation((int x, int y) pos, (int x, int y)[] range)
    {
        int maxNum = 0;
        int maxO = -1;
        for (int o = 0; o < 4; o++)
        {
            (int x, int y)[] rangeO = MapDataManager.Manager.RangeCaculator(range, pos, o);
            List<Entity> entities = EntityManager.Manager.EntitySelector_Range(rangeO, _thisEntity.Movement.Camp, false, 0.5f, false);
            if (entities.Count > maxNum)
            {
                maxNum = entities.Count;
                maxO = o;
            }
        }
        return (maxO, maxNum);
    }
    private void FindBestPoint(int type, Entity entity)
    {
        BlockData[,] map = MapDataManager.Manager.BlockDataMatrix;
        (int iSize, int jSize) = MapDataManager.Manager.MapSize;
        (int x, int y)[] range = entity.Vision.BaseRange;
        int max = 0;
        (int x, int y) maxXY = (-10, -10);
        List<(int x, int y)> canmovepos = new List<(int x, int y)>();
        for (int i = 0; i < iSize; i++)
        {
            for (int j = 0; j < jSize; j++)
            {
                if (map[i, j].PassableType <= _thisMove.MoveMethod)
                {
                    (int o, int n) = FindBestOrientation((j, i), range);
                    if (n > max)
                    {
                        max = n;
                        maxXY = (j, i);
                    }
                    _bestOrientation[i, j] = o;
                    canmovepos.Add((j, i));
                }
                else
                {
                    _bestOrientation[i, j] = -1;
                }
            }
        }
        if (maxXY.x != -10)
        {
            _thisMove.AddTempTarget(() => { _findGap = 5; }, new Vector2(maxXY.x, maxXY.y), 5f);
        }
        else
        {
            maxXY = canmovepos[(int)(RandomHelper.Helper.RandomF() * canmovepos.Count)];
            _thisMove.AddTempTarget(null, new Vector2(maxXY.x, maxXY.y), 5);
            _findGap = 5;
        }
        print(_thisMove.CurrentSectionSerial);
    }
    private void ShowVision()
    {

    }
}
