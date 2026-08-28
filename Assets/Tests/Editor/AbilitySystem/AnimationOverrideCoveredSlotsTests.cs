using System.Collections.Generic;
using NUnit.Framework;

namespace AbilitySystem.Tests
{
    /// <summary>AnimationOverride.GetCoveredSlots（一次性覆盖的消费槽位集合）的 EditMode 契约测试。
    /// 覆盖槽 = 非空资源字段 + ClearedSlots；空字段不算。一次性覆盖条目靠它初始化待消费集合，
    /// 漏一个字段就永远不消费（条目滞留），所以逐槽位断言。</summary>
    public class AnimationOverrideCoveredSlotsTests
    {
        [Test]
        public void CoveredSlots_MixesSetResourcesAndClearedSlots_SkipsEmpty()
        {
            var ov = new AnimationOverride { Idle = "anim_idle", AttackRemote = "anim_atk" };
            ov.Clear(AnimationSlot.Die);

            HashSet<AnimationSlot> covered = ov.GetCoveredSlots();

            Assert.That(covered, Is.EquivalentTo(new[]
            {
                AnimationSlot.Idle, AnimationSlot.AttackRemote, AnimationSlot.Die,
            }));
        }

        [Test]
        public void CoveredSlots_AllFifteenSlots_WhenFullyPopulated()
        {
            var ov = new AnimationOverride
            {
                Default = "a", Idle = "a", Move = "a",
                JumpBegin = "a", JumpLoop = "a", JumpEnd = "a",
                Start = "a", Die = "a",
                AttackBegin = "a", AttackEnd = "a",
                AttackRemote = "a", AttackClose = "a",
                ChargeBegin = "a", Charge = "a", ChargeEnd = "a",
            };

            Assert.That(ov.GetCoveredSlots().Count, Is.EqualTo(15));
        }

        [Test]
        public void CoveredSlots_EmptyOverride_IsEmpty()
        {
            Assert.That(new AnimationOverride().GetCoveredSlots(), Is.Empty);
        }
    }
}
