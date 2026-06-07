using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class FlashMoveComponentTests
{
    [Test]
    public void OnTrigger_NullEntity_DoesNotThrow()
    {
        var comp = new FlashMoveComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "moveDis", type = ParamValueType.Float, value = "5" }
        }};
        comp.OnInit(ctx, pl);
        ctx.currentEvent = new BeforeTakeDamageEvent();
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }

    [Test]
    public void OnInit_ParsesMoveDis()
    {
        var comp = new FlashMoveComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "moveDis", type = ParamValueType.Float, value = "3.5" }
        }};
        Assert.DoesNotThrow(() => comp.OnInit(ctx, pl));
    }
}
