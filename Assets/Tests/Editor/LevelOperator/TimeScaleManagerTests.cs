using NUnit.Framework;
using UnityEngine;

namespace AbilitySystem.Tests
{
    /// <summary>
    /// TimeScaleManager 优先级合成与计数配平测试（纯 POCO 单例，EditMode 直测；
    /// Time.timeScale 在 EditMode 可读写）。单例跨测试持久，每例前后 Reset 归零。
    /// </summary>
    public class TimeScaleManagerTests
    {
        [SetUp]
        public void SetUp() => TimeScaleManager.Manager.Reset();

        [TearDown]
        public void TearDown() => TimeScaleManager.Manager.Reset();

        [Test]
        public void NoRequests_IsNormalSpeed()
        {
            Assert.AreEqual(TimeScaleManager.NormalScale, Time.timeScale);
        }

        [Test]
        public void Slow_IsCounted_MultipleSourcesDoNotClobberEachOther()
        {
            var mgr = TimeScaleManager.Manager;
            mgr.SetSlow(true);   // 来源A：查看干员
            mgr.SetSlow(true);   // 来源B：技能生成
            Assert.AreEqual(TimeScaleManager.SlowScale, Time.timeScale);

            mgr.SetSlow(false);  // A 先退出，B 仍持有
            Assert.AreEqual(TimeScaleManager.SlowScale, Time.timeScale);

            mgr.SetSlow(false);  // B 退出，恢复
            Assert.AreEqual(TimeScaleManager.NormalScale, Time.timeScale);
        }

        [Test]
        public void Slow_Overrides_Fast()
        {
            var mgr = TimeScaleManager.Manager;
            mgr.SetFast(true);
            Assert.AreEqual(TimeScaleManager.FastScale, Time.timeScale);

            mgr.SetSlow(true);
            Assert.AreEqual(TimeScaleManager.SlowScale, Time.timeScale);

            mgr.SetSlow(false);
            Assert.AreEqual(TimeScaleManager.FastScale, Time.timeScale);
        }

        [Test]
        public void Pause_Overrides_SlowAndFast()
        {
            var mgr = TimeScaleManager.Manager;
            mgr.SetFast(true);
            mgr.SetSlow(true);
            mgr.SetPause(true);
            Assert.AreEqual(TimeScaleManager.PauseScale, Time.timeScale);

            mgr.SetPause(false);
            Assert.AreEqual(TimeScaleManager.SlowScale, Time.timeScale);
        }

        [Test]
        public void Suspend_ForcesNormal_AndRestoresTiersOnResume()
        {
            var mgr = TimeScaleManager.Manager;
            mgr.SetPause(true);
            mgr.Suspend();
            Assert.AreEqual(TimeScaleManager.NormalScale, Time.timeScale, "面板栈压栈强制正常速度");

            mgr.Resume();
            Assert.AreEqual(TimeScaleManager.PauseScale, Time.timeScale, "恢复后按保留的档位申请重新合成");
        }

        [Test]
        public void Reset_ClearsAllRequests()
        {
            var mgr = TimeScaleManager.Manager;
            mgr.SetPause(true);
            mgr.SetSlow(true);
            mgr.SetFast(true);
            mgr.Suspend();

            mgr.Reset();
            Assert.AreEqual(TimeScaleManager.NormalScale, Time.timeScale);
        }
    }
}
