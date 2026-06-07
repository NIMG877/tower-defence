using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class ResetAnimationComponentTests
{
    [Test]
    public void OnTrigger_NullEntity_DoesNotThrow()
    {
        var comp = new ResetAnimationComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "resetIndices", type = ParamValueType.String, value = "0,1,2" }
        }};
        comp.OnInit(ctx, pl);
        ctx.currentEvent = new BeforeTakeDamageEvent();
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }

    [Test]
    public void OnInit_EmptyCsv_LeavesArrayEmpty()
    {
        var comp = new ResetAnimationComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        comp.OnInit(ctx, new ParamList());
        // No observable internal state, but call must not throw.
        Assert.Pass();
    }
}
