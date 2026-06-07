using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class CoroutineLoopComponentTests
{
    [Test]
    public void OnInit_EmptyParams_DoesNotThrow()
    {
        var comp = new CoroutineLoopComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        Assert.DoesNotThrow(() => comp.OnInit(ctx, new ParamList()));
    }

    [Test]
    public void OnTick_StartsLoopButStopsOnTeardown()
    {
        var comp = new CoroutineLoopComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        comp.OnInit(ctx, new ParamList());
        // First tick starts the loop; no real side effects in the empty-body stub.
        Assert.DoesNotThrow(() => comp.OnTick(ctx, 0.02f));
        // Teardown cancels the running flag and any pending await.
        Assert.DoesNotThrow(() => comp.OnTeardown(ctx));
    }
}
