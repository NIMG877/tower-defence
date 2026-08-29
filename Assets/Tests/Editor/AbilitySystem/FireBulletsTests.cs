using System.Collections.Generic;
using AbilitySystem.Components;
using NUnit.Framework;
using UnityEngine;

namespace AbilitySystem.Tests
{
    /// <summary>FireBullets 配线错误路径的 EditMode 测试：点列表缺失 / 特效数据
    /// 越界时告警跳过、不抛异常。发弹本体（Bullet + EffectManager + 落点回调
    /// 派发）依赖场景装配与飞行时间，EditMode 不覆盖，由 PlayMode 验证。</summary>
    public class FireBulletsTests
    {
        private readonly List<GameObject> _scratch = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _scratch.Count; i++)
            {
                Object.DestroyImmediate(_scratch[i]);
            }
            _scratch.Clear();
        }

        [Test]
        public void MissingPointsKey_SkipsWithoutThrowing()
        {
            Entity entity = NewEntityWithAttackBase();
            var bb = new Blackboard();
            var ctx = new AbilityContext { entity = entity, sharedBlackboard = bb };

            var comp = new FireBullets();
            comp.OnInit(ctx, Params(("pointsKey", "no_such_points"), ("effectDataIndex", "0")));
            Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
        }

        [Test]
        public void EffectDataIndexOutOfRange_SkipsWithoutThrowing()
        {
            Entity entity = NewEntityWithAttackBase();
            var bb = new Blackboard();
            bb.Set("points", new List<Vector2> { new(1, 1), new(2, 2) });
            var ctx = new AbilityContext { entity = entity, sharedBlackboard = bb };

            var comp = new FireBullets();
            comp.OnInit(ctx, Params(("pointsKey", "points"), ("effectDataIndex", "3")));
            Assert.DoesNotThrow(() => comp.OnTrigger(ctx), "宿主 _extraEffectDatas 为空时越界告警跳过");
        }

        [Test]
        public void MissingSpawnTransform_SkipsWithoutThrowing()
        {
            Entity entity = NewEntityWithAttackBase();
            entity.AttackBase._extraEffectDatas.Add(new AttackBase.AttackEffectData());
            var bb = new Blackboard();
            bb.Set("points", new List<Vector2> { new(1, 1) });
            var ctx = new AbilityContext { entity = entity, sharedBlackboard = bb };

            var comp = new FireBullets();
            comp.OnInit(ctx, Params(("pointsKey", "points"), ("effectDataIndex", "0")));
            Assert.DoesNotThrow(() => comp.OnTrigger(ctx), "出生骨骼缺失是配线错误，告警跳过而非兜底到实体位置");
        }

        private Entity NewEntityWithAttackBase()
        {
            var go = new GameObject("firebullets_test_entity");
            _scratch.Add(go);
            Entity entity = go.AddComponent<Entity>();
            entity.AttackBase = go.AddComponent<AttackBase>();
            return entity;
        }

        private static ParamList Params(params (string key, string value)[] kv)
        {
            var entries = new ParamEntry[kv.Length];
            for (int i = 0; i < kv.Length; i++)
            {
                entries[i] = new ParamEntry
                {
                    key = kv[i].key,
                    value = kv[i].value,
                    fromBlackboard = false,
                    type = ParamValueType.String,
                };
            }
            return new ParamList { entries = entries };
        }
    }
}
