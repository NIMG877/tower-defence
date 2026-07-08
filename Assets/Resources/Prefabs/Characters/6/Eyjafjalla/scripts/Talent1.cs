using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

public class Talent1 : Talent
{
    public EntityID LavaBubbleID;
    [SerializeField] private GameObject BubbleExplodeEffect;
    [SerializeField] private float p;
    [SerializeField] private string _attackBegin, _attackEnd, _attack;
    private bool _targetSetBubble;
    private AnimationMachine am;
    private AttackBase ab;
    private List<Entity> _bubbles;
    private Modifier[] _bubbleModifiers = new Modifier[2] { new Modifier(Attributes.PhysicalDamageRate, ModifierOp.MulFinal, 0.005f), new Modifier(Attributes.MagicDamageRate, ModifierOp.MulFinal, 0.005f) };
    private Buff _lavaBubbleATKBuff;
    private float deltaPerBubble = 0.2f;
    private bool _skill1Open;
    private AnimationOverrideHandle _bubbleTargetAnimation;
    private void OperationsOnBeforeTargetSelect(List<Entity> targets, ref int selectMaxNum, ref int selectMinNum, ref bool sameComp)
    {
        for (int i = targets.Count - 1; i >= 0; i--)
        {
            if (_bubbles.Contains(targets[i]))
            {
                targets.RemoveAt(i);
            }
        }
        targets.InsertRange(0, _bubbles);
    }
    private void OperationsAfterTakeDamage(Entity target, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly)
    {
        if (_bubbles.Contains(target))
        {
            target.Die();
        }
    }
    private void SetBubbleTarget()
    {
        if (!_targetSetBubble)
        {
            _targetSetBubble = true;
            _bubbleTargetAnimation = am.AddOverride(this, new AnimationOverride
            {
                AttackBegin = _attackBegin,
                AttackEnd = _attackEnd,
                AttackRemote = _attack,
                AttackClose = _attack,
            });
            ab.OnBeforeTargetSelect += OperationsOnBeforeTargetSelect;
            ab.OnAfterTakeDamage += OperationsAfterTakeDamage;
        }
    }
    private void SpawnBubble(Vector2 pos)
    {
        Entity bubble = EntityManager.Manager.SetMovableEntity(LavaBubbleID, pos, _thisEntity.Camp == 1 ? 2 : 1, 0);
        _bubbles.Add(bubble);
        _thisEntity.buffController.SetBuffValues(new Modifier[1] { new Modifier(Attributes.Attack, ModifierOp.AddPercent, _bubbles.Count * deltaPerBubble) }, _lavaBubbleATKBuff);
        bubble.buffController.CreateBuff(_bubbleModifiers, null, "lavaStrength", -5, false);
        bubble.OnBeforeDieAnimation += (() =>
        {
            _bubbles.Remove(bubble);
            Explode(pos);
        });
    }
    private void RemoveBubbleTargetAnimation()
    {
        am.RemoveOverride(_bubbleTargetAnimation);
        _bubbleTargetAnimation = null;
        am.OnAttackAnimationBegin -= RemoveBubbleTargetAnimation;
    }
    private void ReleaseBubbleTarget()
    {
        _targetSetBubble = false;
        ab.OnBeforeTargetSelect -= OperationsOnBeforeTargetSelect;
        ab.OnAfterTakeDamage -= OperationsAfterTakeDamage;
        am.OnAttackAnimationBegin += RemoveBubbleTargetAnimation;
    }
    private async void Explode(Vector2 pos)
    {
        await UniTask.WaitForSeconds(0.366667f);
        List<Entity> en = EntityManager.Manager.EntitySelector_Radius((pos.x, pos.y), _thisEntity.Camp, false, 1.5f, false);
        for (int i = 0; i < en.Count; i++)
        {
            en[i].TakeDamage(_thisEntity, _thisEntity.AttackBase.AttackDamageS, 3.7f, 0, 0, 0, 0, 1, 1);
        }
        _thisEntity.buffController.SetBuffValues(new Modifier[1] { new Modifier(Attributes.Attack, ModifierOp.AddPercent, _bubbles.Count * deltaPerBubble) }, _lavaBubbleATKBuff);
        if (_bubbles.Count == 0)
        {
            ReleaseBubbleTarget();
        }
        Destroy(Instantiate(BubbleExplodeEffect, pos, Quaternion.identity), 1);
    }
    public override void Initialize()
    {
        am = _thisEntity.entityAM;
        ab = _thisEntity.AttackBase;
        _bubbles = new List<Entity>();
        _lavaBubbleATKBuff = _thisEntity.buffController.CreateBuff(new Modifier[1] { new Modifier(Attributes.Attack, ModifierOp.AddPercent, 0f) }, null, "lavaBuff", -5, true);
        ab.OnAfterTakeDamage += ((Entity target, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly) =>
        {
            if (RandomHelper.Helper.RandomP(p))
            {
                SpawnBubble(target.transform.position);
                if (!_skill1Open)
                {
                    SetBubbleTarget();
                }
            }
        });
    }
    public void Skill1Open()
    {
        _skill1Open = true;
        p *= 3;
        if (_targetSetBubble)
        {
            ReleaseBubbleTarget();
        }
    }
    public void Skill1End()
    {
        _skill1Open = false;
        p /= 3;
        if (_bubbles.Count > 0 && !_targetSetBubble)
        {
            SetBubbleTarget();
        }
    }
    public void Skill2Open()
    {
        ReleaseBubbleTarget();
        am.OnAttackAnimationBegin -= RemoveBubbleTargetAnimation;
    }
    public void Skill2SetBubble(Vector2 pos)
    {
        SpawnBubble(pos);
    }
    public void Skill2End()
    {
        SetBubbleTarget();
    }
}
