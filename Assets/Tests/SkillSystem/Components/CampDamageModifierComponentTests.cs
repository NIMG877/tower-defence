using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class CampDamageModifierComponentTests
{
    [Test]
    public void OnBeforeTakeDamage_SetsMultiplier_WhenAttackerCampMatches()
    {
        var comp = new CampDamageModifierComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "requiredCamp", type = ParamValueType.Int, value = "2" },
            new ParamEntry { key = "multiplier", type = ParamValueType.Float, value = "10" }
        }};
        comp.OnInit(ctx, pl);

        var evt = new BeforeTakeDamageEvent { multiplyer = 1f };
        ctx.currentEvent = evt;
        ctx.blackboard.Set("attackerCamp", 2);
        comp.OnTrigger(ctx);
        Assert.AreEqual(10f, evt.multiplyer, 0.001f);
    }

    [Test]
    public void OnTrigger_IgnoresUnrelatedEvents()
    {
        var comp = new CampDamageModifierComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        comp.OnInit(ctx, new ParamList());
        var evt = new BeforeAttackEvent();
        ctx.currentEvent = evt;
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }
}
