using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class PeriodicAuraBuffComponentTests
{
    [Test]
    public void OnInit_ParsesAllParams()
    {
        var comp = new PeriodicAuraBuffComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "radius", type = ParamValueType.Float, value = "3" },
            new ParamEntry { key = "buffTypes", type = ParamValueType.String, value = "atk" },
            new ParamEntry { key = "buffValues", type = ParamValueType.String, value = "1.5" },
            new ParamEntry { key = "buffId", type = ParamValueType.String, value = "aura" },
            new ParamEntry { key = "priority", type = ParamValueType.Float, value = "-5" },
            new ParamEntry { key = "toAllies", type = ParamValueType.Bool, value = "true" }
        }};
        Assert.DoesNotThrow(() => comp.OnInit(ctx, pl));
    }

    [Test]
    public void OnTick_NullEntity_DoesNotThrow()
    {
        var comp = new PeriodicAuraBuffComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        comp.OnInit(ctx, new ParamList());
        Assert.DoesNotThrow(() => comp.OnTick(ctx, 0.02f));
    }
}
