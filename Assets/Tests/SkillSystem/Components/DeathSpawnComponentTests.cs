using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class DeathSpawnComponentTests
{
    [Test]
    public void OnTrigger_NullEntity_DoesNotThrow()
    {
        var comp = new DeathSpawnComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "entityId", type = ParamValueType.String, value = "c,1" },
            new ParamEntry { key = "num", type = ParamValueType.Int, value = "3" },
            new ParamEntry { key = "gap", type = ParamValueType.Float, value = "0.2" }
        }};
        comp.OnInit(ctx, pl);
        ctx.currentEvent = new BeforeDieAnimationEvent();
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }

    [Test]
    public void OnTrigger_WrongEvent_IsNoOp()
    {
        var comp = new DeathSpawnComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        comp.OnInit(ctx, new ParamList());
        ctx.currentEvent = new BeforeTakeDamageEvent();
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }
}
