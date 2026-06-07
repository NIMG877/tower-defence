using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class SetAttackEffectDataComponentTests
{
    [Test]
    public void OnTrigger_NullEntity_DoesNotThrow()
    {
        var comp = new SetAttackEffectDataComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        comp.OnInit(ctx, new ParamList());
        ctx.currentEvent = new BeforeTakeDamageEvent();
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }

    [Test]
    public void OnInit_EmptyParams_DoesNotThrow()
    {
        var comp = new SetAttackEffectDataComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        Assert.DoesNotThrow(() => comp.OnInit(ctx, new ParamList()));
    }
}
