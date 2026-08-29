using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AbilitySystem.Tests
{
    /// <summary>子弹落点桥接的契约测试：事件映射、枚举末尾追加序号稳定、
    /// SpawnEntity 的事件位置提取 seam。Bullet/EffectManager 装配与飞行
    /// 由 PlayMode 验证（与 BeforeTargetSelect 桥接同口径）。</summary>
    public class BulletLandedBridgeTests
    {
        [Test]
        public void BulletLandedEvent_MapsToOnBulletLanded()
        {
            var evt = new BulletLandedEvent { position = new Vector2(3, 4) };
            Assert.That(evt.TriggerEvent, Is.EqualTo(TriggerEvent.OnBulletLanded));
            Assert.That(evt.position, Is.EqualTo(new Vector2(3, 4)));
        }

        [Test]
        public void TriggerEvent_OnBulletLanded_AppendedAfterOnBeforeTargetSelect()
        {
            // 既有 asset 按枚举序号序列化；OnBulletLanded 必须排在末尾且不改变既有序号。
            Assert.That((int)TriggerEvent.OnSummonDeath, Is.EqualTo(17));
            Assert.That((int)TriggerEvent.OnBeforeTargetSelect, Is.EqualTo(18));
            Assert.That((int)TriggerEvent.OnBulletLanded, Is.EqualTo(19));
        }

        [Test]
        public void ResolveEventPosition_ReadsCarryingEvents_NullOtherwise()
        {
            Assert.That(
                AbilitySystem.Components.SpawnEntity.ResolveEventPosition(
                    new BulletLandedEvent { position = new Vector2(1, 2) }),
                Is.EqualTo(new Vector2(1, 2)));
            Assert.That(
                AbilitySystem.Components.SpawnEntity.ResolveEventPosition(
                    new SummonDeathEvent { position = new Vector2(5, 6) }),
                Is.EqualTo(new Vector2(5, 6)));
            Assert.That(
                AbilitySystem.Components.SpawnEntity.ResolveEventPosition(new AttackSuccessfullyEvent()),
                Is.Null, "不携带位置的事件返回 null，由 positionMode=event 的调用方报配线错误");
            Assert.That(AbilitySystem.Components.SpawnEntity.ResolveEventPosition(null), Is.Null);
        }
    }
}
