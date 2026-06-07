using NUnit.Framework;
using SkillSystem;
using SkillSystem.Components;

public class StageStateMachineComponentTests
{
    [Test]
    public void OnTick_AdvancesAfterDuration()
    {
        var comp = new StageStateMachineComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        // Inject stages directly via the test helper, bypassing the string-encoded ParamList path.
        comp.OnInitForTest(new[] {
            new StageConfig { name = "S0", enterDuration = 1f },
            new StageConfig { name = "S1", enterDuration = 1f }
        });
        comp.OnInit(ctx, new ParamList());

        Assert.AreEqual("S0", comp.CurrentStageNameForTest);
        comp.OnTick(ctx, 0.5f);
        Assert.AreEqual("S0", comp.CurrentStageNameForTest);
        comp.OnTick(ctx, 0.6f);
        Assert.AreEqual("S1", comp.CurrentStageNameForTest);
    }

    [Test]
    public void OnTick_AfterLastStage_StaysAtEnd()
    {
        var comp = new StageStateMachineComponent();
        var ctx = new SkillContext();
        ctx.blackboard = new Blackboard();
        comp.OnInitForTest(new[] {
            new StageConfig { name = "Only", enterDuration = 0.5f }
        });
        comp.OnInit(ctx, new ParamList());
        comp.OnTick(ctx, 1f);
        // After exiting the only stage, CurrentStageNameForTest returns null (no active stage).
        Assert.IsNull(comp.CurrentStageNameForTest);
    }
}
