using NUnit.Framework;
using SkillSystem;

public class ConditionEvaluatorTests
{
    [Test]
    public void None_AlwaysTrue()
    {
        var cond = new ConditionConfig { triggerEvent = TriggerEvent.OnAfterHurt, op = ConditionOp.None };
        var ctx = MakeCtx("hpRate", "0.5");
        Assert.IsTrue(ConditionEvaluator.Evaluate(cond, ctx));
    }

    [Test]
    public void Greater_FloatComparison()
    {
        var cond = new ConditionConfig { triggerEvent = TriggerEvent.OnAfterHurt, op = ConditionOp.Greater, leftKey = "hpRate", rightValue = "0.3" };
        Assert.IsTrue(ConditionEvaluator.Evaluate(cond, MakeCtx("hpRate", "0.5")));
        Assert.IsFalse(ConditionEvaluator.Evaluate(cond, MakeCtx("hpRate", "0.1")));
        Assert.IsFalse(ConditionEvaluator.Evaluate(cond, MakeCtx("hpRate", "0.3")));
    }

    [Test]
    public void Equal_StringCompare()
    {
        var cond = new ConditionConfig { triggerEvent = TriggerEvent.OnAfterHurt, op = ConditionOp.Equal, leftKey = "targetCamp", rightValue = "2" };
        Assert.IsTrue(ConditionEvaluator.Evaluate(cond, MakeCtx("targetCamp", "2")));
        Assert.IsFalse(ConditionEvaluator.Evaluate(cond, MakeCtx("targetCamp", "1")));
    }

    [Test]
    public void HasBlackboardKey_FromContext()
    {
        var cond = new ConditionConfig { triggerEvent = TriggerEvent.OnAfterHurt, op = ConditionOp.HasBlackboardKey, leftKey = "phase" };
        var ctx = MakeCtx("hpRate", "0.5");
        Assert.IsFalse(ConditionEvaluator.Evaluate(cond, ctx));
        ctx.Blackboard.Set("phase", "charging");
        Assert.IsTrue(ConditionEvaluator.Evaluate(cond, ctx));
    }

    private ConditionEvalContext MakeCtx(string leftKey, string leftValue)
    {
        var ctx = new ConditionEvalContext();
        ctx.Blackboard.Set(leftKey, leftValue);
        return ctx;
    }
}
