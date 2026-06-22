using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HeadSeterTalent1 : Talent
{
    private bool _haveHead;
    private bool _isDie;
    private List<Entity> _entities;
    private EntityManager _entityManager;
    [SerializeField] private string _default_1, _move_1, _idle_1, _default_d, _move_d, _idle_d, _die_d, _set_d, _attack;
    public override void PreWarm()
    {
        base.PreWarm();
        _entities = new List<Entity>();
        _entityManager = EntityManager.Manager;
    }
    public override void Initialize()
    {
        _haveHead = true;
        _isDie = false;
        _thisEntity.buffController.AddAbnormalState(-10, 2);
        _thisEntity.OnAfterHurt += new Entity.OperationsAfterHurt((Entity origin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly) =>
        {
            if (_haveHead && !_isDie && isDeadly)
            {
                _thisEntity.TakeDamage(_thisEntity, 1, 1, 0, 0, 0, 0, 3, 0);
                _isDie = true;
                _thisEntity.Stats.AddHurtable(1);
                _thisEntity.Stats.AddSelectable(1);
                AnimationMachine am = _thisEntity.entityAM;
                am.RemoveOverrides(this);
                am.AddOverride(this, new AnimationOverride
                {
                    Default = _default_d,
                    Move = _move_d,
                    Idle = _idle_d,
                    Die = _die_d,
                    AttackClose = _set_d,
                    AttackRemote = _set_d,
                });
                am.TrySetState(EntityState.Default, true);
                _thisEntity.buffController.CreateBuff(new BuffType[1] { BuffType.mspeed_delta_percent }, null, "slowSpeed", new float[1] { -0.35f }, -5, false);
                _thisEntity.GetComponent<HeadSeterSkill1>().IsDie = true;
            }
        });
    }
    private void FixedUpdate()
    {
        if (!_thisEntity.Stats.IsActive || !_haveHead)
            return;
        _entities = _entityManager.EntitySelector_Radius((_thisEntity.Movement.Position.x, _thisEntity.Movement.Position.y), _thisEntity.Movement.Camp, true, 1, true);
        for (int i = 0; i < _entities.Count; i++)
        {
            if (_entities[i].EntityData.ChineseName== "�����㡱����")
            {
                _thisEntity.buffController.TryRemoveAbnormalState(2);
                if (!_isDie)
                {
                    AnimationMachine am = _thisEntity.entityAM;
                    am.RemoveOverrides(this);
                    am.AddOverride(this, new AnimationOverride
                    {
                        Default = _default_1,
                        Move = _move_1,
                        Idle = _idle_1,
                    });
                }
                _thisEntity.AttackBase.TryToAttack(new Entity[1] { _entities[i] }, false, false);
                _thisEntity.AttackBase.OnBeforeTakeDamage += new AttackBase.OperationsBeforeTakeDamage((Entity target, ref float multiplyer, ref float defPenetrate, ref float mgrPenetrate, ref float defPenetrate_value, ref float mgrPenetrate_value, ref int damageType, int applyType) =>
                {
                    if (_haveHead)
                    {
                        multiplyer = 0;
                        _haveHead = false;
                        target.GetComponent<WitherPedestalTalent1>().AddHead();
                        if (_isDie)
                        {
                            _thisEntity.Die();
                        }
                        else
                        {
                            _thisEntity.entityAM.AddOverride(this, new AnimationOverride
                            {
                                AttackClose = _attack,
                                AttackRemote = _attack,
                            });
                        }
                    }
                }); ;
                return;
            }
        }
    }
}
