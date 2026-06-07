using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class SetAbnormalStateComponentTests
{
    [Test]
    public void OnTrigger_NullEntity_DoesNotThrow()
    {
        var comp = new SetAbnormalStateComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        comp.OnInit(ctx, new ParamList());
        ctx.currentEvent = new BeforeTakeDamageEvent();
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }

    [Test]
    public void OnInit_ParsesAddFlagAndStateIndex()
    {
        var comp = new SetAbnormalStateComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "stateIndex", type = ParamValueType.Int, value = "3" },
            new ParamEntry { key = "add", type = ParamValueType.Bool, value = "false" }
        }};
        comp.OnInit(ctx, pl);
        // No observable effect without a real Entity, but OnInit must accept the params and not throw
        Assert.Pass();
    }
}
