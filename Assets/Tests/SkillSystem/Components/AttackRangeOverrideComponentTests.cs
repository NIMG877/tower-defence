using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class AttackRangeOverrideComponentTests
{
    [Test]
    public void OnTrigger_NullEntity_DoesNotThrow()
    {
        var comp = new AttackRangeOverrideComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "range", type = ParamValueType.String, value = "0,0;1,1;-1,0" }
        }};
        comp.OnInit(ctx, pl);
        ctx.currentEvent = new BeforeTakeDamageEvent();
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }

    [Test]
    public void OnInit_EmptyRange_LeavesEmpty()
    {
        var comp = new AttackRangeOverrideComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        Assert.DoesNotThrow(() => comp.OnInit(ctx, new ParamList()));
    }
}
