using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class DamageRadiusFalloffComponentTests
{
    [Test]
    public void OnInit_ParsesAllTiers()
    {
        var comp = new DamageRadiusFalloffComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "maxRadius", type = ParamValueType.Float, value = "2" },
            new ParamEntry { key = "baseDamage", type = ParamValueType.Float, value = "100" },
            new ParamEntry { key = "tier1Mul", type = ParamValueType.Float, value = "1" },
            new ParamEntry { key = "tier2Mul", type = ParamValueType.Float, value = "0.5" },
            new ParamEntry { key = "tier3Mul", type = ParamValueType.Float, value = "0.25" },
            new ParamEntry { key = "tier4Mul", type = ParamValueType.Float, value = "0.1" }
        }};
        Assert.DoesNotThrow(() => comp.OnInit(ctx, pl));
    }

    [Test]
    public void OnTrigger_NullEntity_DoesNotThrow()
    {
        var comp = new DamageRadiusFalloffComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        comp.OnInit(ctx, new ParamList());
        ctx.currentEvent = new BeforeTakeDamageEvent();
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }
}
