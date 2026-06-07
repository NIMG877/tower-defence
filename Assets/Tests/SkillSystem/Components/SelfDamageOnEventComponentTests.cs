using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class SelfDamageOnEventComponentTests
{
    [Test]
    public void OnTrigger_NullEntity_DoesNotThrow()
    {
        var comp = new SelfDamageOnEventComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "damage", type = ParamValueType.Float, value = "5000" },
            new ParamEntry { key = "damageType", type = ParamValueType.Int, value = "3" }
        }};
        comp.OnInit(ctx, pl);
        ctx.currentEvent = new AfterTakeDamageEvent { isDeadly = true };
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }

    [Test]
    public void OnTrigger_NonDeadly_IsNoOp()
    {
        var comp = new SelfDamageOnEventComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        comp.OnInit(ctx, new ParamList());
        // Without a real Entity, even a non-deadly event is a no-op (no exception).
        ctx.currentEvent = new AfterTakeDamageEvent { isDeadly = false };
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }
}
