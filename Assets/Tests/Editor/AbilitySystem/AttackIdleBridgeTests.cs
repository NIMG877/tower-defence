using AbilitySystem;
using NUnit.Framework;

namespace AbilitySystem.Tests
{
    public class AttackIdleBridgeTests
    {
        [Test]
        public void AttackIdleEvent_MapsToOnAttackIdle()
        {
            var evt = new AttackIdleEvent();
            Assert.That(evt.TriggerEvent, Is.EqualTo(TriggerEvent.OnAttackIdle));
        }

        [Test]
        public void TriggerEvent_OnAttackIdle_AppendsAfterOnBulletLanded()
        {
            Assert.That((int)TriggerEvent.OnBulletLanded, Is.EqualTo(19));
            Assert.That((int)TriggerEvent.OnAttackIdle, Is.EqualTo(20));
        }
    }
}
