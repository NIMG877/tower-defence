using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class PlayAnimationComponentTests
{
    [Test]
    public void OnTrigger_NullEntity_DoesNotThrow()
    {
        var comp = new PlayAnimationComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        comp.OnInit(ctx, new ParamList());
        ctx.currentEvent = new BeforeTakeDamageEvent();
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }

    [Test]
    public void OnInit_ParsesTargetStateAndForce()
    {
        var comp = new PlayAnimationComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "targetState", type = ParamValueType.Int, value = "6" }, // Die
            new ParamEntry { key = "force", type = ParamValueType.Bool, value = "false" }
        }};
        Assert.DoesNotThrow(() => comp.OnInit(ctx, pl));
    }
}
