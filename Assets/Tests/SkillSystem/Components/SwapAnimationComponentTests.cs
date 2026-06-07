using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class SwapAnimationComponentTests
{
    [Test]
    public void OnTrigger_NullEntity_DoesNotThrow()
    {
        var comp = new SwapAnimationComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        comp.OnInit(ctx, new ParamList());
        ctx.currentEvent = new BeforeTakeDamageEvent();
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }

    [Test]
    public void OnInit_EmptyParams_DoesNotThrow()
    {
        var comp = new SwapAnimationComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        // Concrete AnimationReferenceAsset fields are populated by the migration tool (Phase 5/6).
        Assert.DoesNotThrow(() => comp.OnInit(ctx, new ParamList()));
    }
}
