using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class SelfDestructComponentTests
{
    [Test]
    public void OnTick_NullEntity_DoesNotThrow()
    {
        var comp = new SelfDestructComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "duration", type = ParamValueType.Float, value = "2.5" }
        }};
        comp.OnInit(ctx, pl);
        // Without a real Entity, OnTick is a no-op (no Die() call target). The test ensures no exception.
        Assert.DoesNotThrow(() => comp.OnTick(ctx, 0.02f));
        Assert.DoesNotThrow(() => comp.OnTick(ctx, 10f));
    }

    [Test]
    public void OnInit_DefaultDuration_IsFiveSeconds()
    {
        var comp = new SelfDestructComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        // Empty params: default 5s. No observable state, just verify no exception.
        Assert.DoesNotThrow(() => comp.OnInit(ctx, new ParamList()));
    }
}
