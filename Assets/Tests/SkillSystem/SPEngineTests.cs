using NUnit.Framework;
using SkillSystem;

public class SPEngineTests
{
    [Test]
    public void Natural_Recovery_IncrementsSp()
    {
        var cfg = new SPConfig { totalSp = 10, initialSp = 0, recoverMode = SpRecoverMode.Natural, openMode = SkillOpenMode.Manual };
        var eng = new SPEngine(cfg, () => { });
        eng.OnTick(5f, dtMultiplier: 1f);
        Assert.AreEqual(5f, eng.CurrentSp);
    }

    [Test]
    public void Natural_Recovery_CapsAtTotalSp()
    {
        var cfg = new SPConfig { totalSp = 10, initialSp = 0, recoverMode = SpRecoverMode.Natural, openMode = SkillOpenMode.Manual };
        var eng = new SPEngine(cfg, () => { });
        eng.OnTick(20f, dtMultiplier: 1f);
        Assert.AreEqual(10f, eng.CurrentSp);
    }

    [Test]
    public void OnAttackHit_Recover_AddsOneSp()
    {
        var cfg = new SPConfig { totalSp = 5, initialSp = 0, recoverMode = SpRecoverMode.OnAttackHit, openMode = SkillOpenMode.Manual };
        var eng = new SPEngine(cfg, () => { });
        eng.OnAttackSuccessfully();
        eng.OnAttackSuccessfully();
        Assert.AreEqual(2f, eng.CurrentSp);
    }

    [Test]
    public void OnAttackHit_Recover_TriggersFireAtFull()
    {
        var cfg = new SPConfig { totalSp = 3, initialSp = 0, recoverMode = SpRecoverMode.OnAttackHit, openMode = SkillOpenMode.OnAttackHit, consumeMode = SpConsumeMode.Instant };
        bool fired = false;
        var eng = new SPEngine(cfg, () => fired = true);
        eng.OnAttackSuccessfully(); eng.OnAttackSuccessfully(); eng.OnAttackSuccessfully();
        eng.OnAttackHit();
        Assert.IsTrue(fired);
        Assert.AreEqual(0f, eng.CurrentSp); // consumed by instant mode
    }

    [Test]
    public void InitialSp_SetsAtConstruction()
    {
        var cfg = new SPConfig { totalSp = 10, initialSp = 7, recoverMode = SpRecoverMode.Natural, openMode = SkillOpenMode.Natural };
        var eng = new SPEngine(cfg, () => { });
        Assert.AreEqual(7f, eng.CurrentSp);
    }
}
