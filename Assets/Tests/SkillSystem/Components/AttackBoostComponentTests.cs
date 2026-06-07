using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class AttackBoostComponentTests
{
    [Test]
    public void OnBeforeAttack_MultipliesAndIncrementsCumbo()
    {
        var comp = new AttackBoostComponent();
        var ctx = new SkillContext();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "multiplier", type = ParamValueType.Float, value = "1.4" },
            new ParamEntry { key = "cumbo", type = ParamValueType.Int, value = "2" }
        }};
        comp.OnInit(ctx, pl);
        var evt = new BeforeAttackEvent { multiplyer = 1f, cumbo = 1 };
        ctx.currentEvent = evt;
        comp.OnTrigger(ctx);
        Assert.AreEqual(1.4f, evt.multiplyer, 0.001f);
        Assert.AreEqual(2, evt.cumbo);
    }

    [Test]
    public void OnTrigger_IgnoresUnrelatedEvents()
    {
        var comp = new AttackBoostComponent();
        var ctx = new SkillContext();
        comp.OnInit(ctx, new ParamList());
        var evt = new AfterHurtEvent();
        ctx.currentEvent = evt;
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }
}
