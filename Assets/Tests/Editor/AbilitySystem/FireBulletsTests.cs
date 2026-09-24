using System.Collections.Generic;
using System.Text.RegularExpressions;
using AbilitySystem.Components;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AbilitySystem.Tests
{
    /// <summary>FireBullets 配线错误路径的 EditMode 测试：宿主攻击数据装配后，
    /// 点列表缺失 / 弹幕序号越界 / 出生骨骼缺失各走到位——LogError 跳过、
    /// 不抛异常。发弹本体（Bullet + EffectManager + 落点回调派发）依赖场景
    /// 装配与飞行时间，EditMode 不覆盖，由 PlayMode 验证。</summary>
    public class FireBulletsTests
    {
        private readonly List<Object> _scratch = new List<Object>();

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
            Entity entity = NewEntityWithAttack();
            var bb = new Blackboard();
            var ctx = new AbilityContext { entity = entity, sharedBlackboard = bb };

            var comp = new FireBullets();
            comp.OnInit(ctx, Params(("pointsKey", "no_such_points"), ("bulletDataIndex", "0")));
            LogAssert.Expect(LogType.Error, new Regex("pointsKey 'no_such_points' is missing"));
            Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
        }

        [Test]
        public void BulletDataIndexOutOfRange_SkipsWithoutThrowing()
        {
            Entity entity = NewEntityWithAttack();
            var bb = new Blackboard();
            bb.Set("points", new List<Vector2> { new(1, 1), new(2, 2) });
            var ctx = new AbilityContext { entity = entity, sharedBlackboard = bb };

            var comp = new FireBullets();
            comp.OnInit(ctx, Params(("pointsKey", "points"), ("bulletDataIndex", "3")));
            LogAssert.Expect(LogType.Error, new Regex("Bullet data index 3 out of range"));
            Assert.DoesNotThrow(() => comp.OnTrigger(ctx), "宿主 _bulletDatas 为空时越界报错跳过");
        }

        [Test]
        public void MissingSpawnTransform_SkipsWithoutThrowing()
        {
            Entity entity = NewEntityWithAttack();
            var bb = new Blackboard();
            bb.Set("points", new List<Vector2> { new(1, 1) });
            var ctx = new AbilityContext { entity = entity, sharedBlackboard = bb };

            // 空 _bulletDatas 会让索引 0 也越界、到不了出生骨骼分支。EntityAttack 没有
            // 直接灌入口，唯一 seam 是 PreWarm 从 EntityData.Bullets 装配：灌一条带
            // prefab（prefab 为空会停在更早的“无 BulletData 资产”分支）、宿主下无
            // Muzzle 骨骼（FindBulletMuzzle 静默返 null）的弹幕条目，让该分支可达。
            var bulletData = ScriptableObject.CreateInstance<BulletData>();
            bulletData.BulletPrefab = new GameObject("firebullets_test_bullet_prefab");
            _scratch.Add(bulletData.BulletPrefab);
            _scratch.Add(bulletData);
            entity.EntityData = new EntityData
            {
                VisionRange = new List<Vector2Int>(), // PreWarm 的 AttributesCaculateFirst 要求非空
                Bullets = new List<BulletData> { bulletData },
            };
            entity.Attack.PreWarm();

            var comp = new FireBullets();
            comp.OnInit(ctx, Params(("pointsKey", "points"), ("bulletDataIndex", "0")));
            LogAssert.Expect(LogType.Error, new Regex("has no BulletSpawnTransform"));
            Assert.DoesNotThrow(() => comp.OnTrigger(ctx), "出生骨骼缺失是配线错误，报错跳过而非兜底到实体位置");
        }

        private Entity NewEntityWithAttack()
        {
            var go = new GameObject("firebullets_test_entity");
            _scratch.Add(go);
            Entity entity = go.AddComponent<Entity>();
            // EntityAttack 是纯 C# 对象，运行时由 Entity.PreWarm 构造；EditMode 没有
            // 该装配链路，走 test-only 钩子补上，OnTrigger 才能越过宿主检查到达各配线错误分支。
            entity.CreateAttackForTests();
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
