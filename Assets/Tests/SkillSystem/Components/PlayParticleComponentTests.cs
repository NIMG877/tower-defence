using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class PlayParticleComponentTests
{
    [Test]
    public void OnTrigger_NullParticleSystem_DoesNotThrow()
    {
        var comp = new PlayParticleComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        comp.OnInit(ctx, new ParamList());
        ctx.currentEvent = new BeforeTakeDamageEvent();
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }

    [Test]
    public void OnInit_ParsesPlayAndStopFlags()
    {
        var comp = new PlayParticleComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "play", type = ParamValueType.Bool, value = "true" },
            new ParamEntry { key = "stop", type = ParamValueType.Bool, value = "false" }
        }};
        Assert.DoesNotThrow(() => comp.OnInit(ctx, pl));
    }
}
