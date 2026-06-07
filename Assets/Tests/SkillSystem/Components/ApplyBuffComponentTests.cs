using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class ApplyBuffComponentTests
{
    [Test]
    public void OnTrigger_NullEntity_DoesNotThrow()
    {
        var comp = new ApplyBuffComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        comp.OnInit(ctx, new ParamList());
        ctx.currentEvent = new BeforeTakeDamageEvent();
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }

    [Test]
    public void OnTrigger_IgnoresUnrelatedEvents()
    {
        var comp = new ApplyBuffComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        comp.OnInit(ctx, new ParamList());
        ctx.currentEvent = new AfterHurtEvent();
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }
}
