using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ZombieTalent : Talent
{
    [SerializeField] private string _idle2, _move2, _1to2, _attack2;
    [SerializeField] private float _angryTime;
    [SerializeField] private float _buffRadius;
    [SerializeField] private GameObject _buffEffect;
    [SerializeField] private BuffType[] _buffTypes;
    [SerializeField] private float[] _buffValues;
    [SerializeField] private GameObject _additionalBuffEffect;
    [SerializeField] private BuffType[] _additionalBuffTypes;
    [SerializeField] private float[] _additionalBuffValues;
    private float _angryTimer;
    private List<Entity> _entityBuffs;
    private List<Buff> _buffs;
    private bool _canEnterAngry;
    public override void PreWarm()
    {
        base.PreWarm();
        _entityBuffs = new List<Entity>();
        _buffs = new List<Buff>();
    }
    public override void Initialize()
    {
        _canEnterAngry = true;
        _angryTimer = 0;
        _additionalBuffEffect.SetActive(false);
        _thisEntity.OnAfterHurt += new Entity.OperationsAfterHurt((Entity origin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly) =>
        {
            if (_canEnterAngry && isDeadly)
            {
                _thisEntity.TakeDamage(_thisEntity, 1, 1, 0, 0, 0, 0, 3, 0);
                EnterAngry();
            }
        });
    }
    public override void Dormancy()
    {
        for (int i = 0; i < _entityBuffs.Count; i++)
        {
            _entityBuffs[i].buffController.DestroyBuff(_buffs[i]);
        }
        _entityBuffs.Clear();
        _buffs.Clear();
    }
    private void EnterAngry()
    {
        _canEnterAngry = false;
        _angryTimer = _angryTime;
        _thisEntity.entityAM.AddOverride(this, new AnimationOverride
        {
            Idle = _idle2,
            Move = _move2,
            Start = _1to2,
            AttackClose = _attack2,
            AttackRemote = _attack2,
        });
        _thisEntity.entityAM.TrySetState(EntityState.Start, true);
        _thisEntity.buffController.CreateBuff(_additionalBuffTypes, null, "selfStrength", _additionalBuffValues, -10, true);
        _additionalBuffEffect.SetActive(true);
        _thisEntity.buffController.AddAbnormalState(-10, 3);
    }
    private void FixedUpdate()
    {
        if (!_canEnterAngry && _thisEntity.Stats.IsActive)
        {
            if (_angryTimer > 0)
            {
                _angryTimer -= Time.fixedDeltaTime;
                List<Entity> entitiesInRange = EntityManager.Manager.EntitySelector_Radius((_thisEntity.Movement.Position.x, _thisEntity.Movement.Position.y), _thisEntity.Movement.Camp, true, _buffRadius, false);
                for (int i = _entityBuffs.Count - 1; i >= 0; i--)
                {
                    if (!entitiesInRange.Contains(_entityBuffs[i]))
                    {
                        _entityBuffs[i].buffController.DestroyBuff(_buffs[i]);
                        _entityBuffs.RemoveAt(i);
                        _buffs.RemoveAt(i);
                    }
                }
                for (int i = 0; i < entitiesInRange.Count; i++)
                {
                    if (!_entityBuffs.Contains(entitiesInRange[i]))
                    {
                        _entityBuffs.Add(entitiesInRange[i]);
                        _buffs.Add(entitiesInRange[i].buffController.CreateBuff(_buffTypes, _buffEffect, "zombieStrength", _buffValues, -10, true));
                    }
                }
            }
            else
            {
                _thisEntity.Die();
            }
        }
    }
}
