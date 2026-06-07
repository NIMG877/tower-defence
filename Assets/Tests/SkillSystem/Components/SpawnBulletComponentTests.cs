using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class SpawnBulletComponentTests
{
    [Test]
    public void OnTrigger_NullEntity_DoesNotThrow()
    {
        var comp = new SpawnBulletComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "speed", type = ParamValueType.Float, value = "10" }
        }};
        comp.OnInit(ctx, pl);
        ctx.currentEvent = new BeforeAttackEvent();
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }

    [Test]
    public void OnInit_ParsesSpeed()
    {
        var comp = new SpawnBulletComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "speed", type = ParamValueType.Float, value = "12.5" }
        }};
        Assert.DoesNotThrow(() => comp.OnInit(ctx, pl));
    }
}
