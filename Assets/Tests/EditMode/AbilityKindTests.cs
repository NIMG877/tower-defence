using NUnit.Framework;
using SkillSystem;

namespace Tests.EditMode
{
    public class AbilityKindTests
    {
        [Test]
        public void AbilityKind_HasThreeValues()
        {
            Assert.AreEqual(0, (int)AbilityKind.Skill);
            Assert.AreEqual(1, (int)AbilityKind.Talent);
            Assert.AreEqual(2, (int)AbilityKind.ExtraAbility);
        }

        [Test]
        public void TriggerEvent_HasAbilityEvents()
        {
            // Renamed from OnSkillBegin/OnSkillEnd
            Assert.AreNotEqual(TriggerEvent.OnSkillBegin, TriggerEvent.OnAbilityBegin);
            Assert.AreNotEqual(TriggerEvent.OnSkillEnd, TriggerEvent.OnAbilityEnd);
            // New
            Assert.AreNotEqual(default(TriggerEvent), TriggerEvent.OnAbilityAdded);
            Assert.AreNotEqual(default(TriggerEvent), TriggerEvent.OnAbilityRemoved);
        }
    }
}
