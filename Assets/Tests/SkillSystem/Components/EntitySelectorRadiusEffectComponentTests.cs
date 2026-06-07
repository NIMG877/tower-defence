using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class EntitySelectorRadiusEffectComponentTests
{
    [Test]
    public void OnTrigger_NullEntity_DoesNotThrow()
    {
        var comp = new EntitySelectorRadiusEffectComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "radius", type = ParamValueType.Float, value = "3" },
            new ParamEntry { key = "subComponentType", type = ParamValueType.String, value = "ApplyBuff" }
        }};
        comp.OnInit(ctx, pl);
        ctx.currentEvent = new BeforeTakeDamageEvent();
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }

    [Test]
    public void OnInit_EmptySubType_LeavesFieldEmpty()
    {
        var comp = new EntitySelectorRadiusEffectComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        comp.OnInit(ctx, new ParamList());
        // Without subComponentType, OnTrigger short-circuits before touching the EntityManager.
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }
}
