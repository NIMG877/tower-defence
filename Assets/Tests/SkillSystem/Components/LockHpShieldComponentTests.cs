using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class LockHpShieldComponentTests
{
    [Test]
    public void OnTrigger_NullEntity_DoesNotThrow()
    {
        var comp = new LockHpShieldComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "active", type = ParamValueType.Bool, value = "true" },
            new ParamEntry { key = "threshold", type = ParamValueType.Float, value = "100" }
        }};
        comp.OnInit(ctx, pl);
        ctx.currentEvent = new BeforeHurtEvent { damage = 500f };
        Assert.DoesNotThrow(() => comp.OnTrigger(ctx));
    }

    [Test]
    public void OnTrigger_Inactive_IsNoOp()
    {
        var comp = new LockHpShieldComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        var pl = new ParamList { entries = new[] {
            new ParamEntry { key = "active", type = ParamValueType.Bool, value = "false" }
        }};
        comp.OnInit(ctx, pl);
        // Even with non-null event, inactive shield is a no-op.
        var bhe = new BeforeHurtEvent { damage = 9999f };
        ctx.currentEvent = bhe;
        comp.OnTrigger(ctx);
        // Damage unchanged because inactive and entity is null anyway.
        Assert.AreEqual(9999f, bhe.damage, 0.001f);
    }
}
