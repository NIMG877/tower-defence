using NUnit.Framework;

namespace AbilitySystem.Tests
{
    /// <summary>索敌候选桥接的契约测试：事件映射、枚举末尾追加序号稳定。
    /// 桥接只派发事件与回写标量，不写黑板（候选列表改经
    /// write_blackboard/override_attack_targets 组件流水线，见
    /// OverrideAttackTargetsTests）。桥接本体依赖 Entity/AttackBase
    /// （MonoBehaviour + 场景装配），与既有组件同样不做 EditMode 单测，由
    /// PlayMode 验证。</summary>
    public class BeforeTargetSelectBridgeTests
    {
        [Test]
        public void BeforeTargetSelectEvent_MapsToOnBeforeTargetSelect()
        {
            var evt = new BeforeTargetSelectEvent();
            Assert.That(evt.TriggerEvent, Is.EqualTo(TriggerEvent.OnBeforeTargetSelect));
        }

        [Test]
        public void TriggerEvent_OnBeforeTargetSelect_AppendedAfterOnSummonDeath()
        {
            // 既有 asset 按枚举序号序列化；OnBeforeTargetSelect 必须排在末尾且不改变既有序号。
            Assert.That((int)TriggerEvent.OnSummonDeath, Is.EqualTo(17));
            Assert.That((int)TriggerEvent.OnBeforeTargetSelect, Is.EqualTo(18));
        }
    }
}
