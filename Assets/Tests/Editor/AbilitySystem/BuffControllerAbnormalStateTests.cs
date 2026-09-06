using NUnit.Framework;
using UnityEngine;

namespace AbilitySystem.Tests
{
    /// <summary>
    /// 异常状态计时契约：每类型单计时器，限时（>0）到期自动移除，≤-5 为无限持续。
    /// 已激活时的刷新规则 = "取更长"，其中永久为最高档——永久不被限时覆盖，
    /// 限时可升级为永久（击退滑行以 -10 施加失衡、与技能限时失衡并存的场景依赖此语义）。
    /// 全部用眩晕（type 0/1/2，仅触 StateMachine）验证；type 3 无敌依赖 Stats，不经此路径测。
    /// </summary>
    public class BuffControllerAbnormalStateTests
    {
        private GameObject _go;
        private BuffController _buffs;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("abnormal-state-subject");
            _go.AddComponent<Entity>();                 // BuffController.PreWarm 里 GetComponent<Entity>
            _buffs = _go.AddComponent<BuffController>();
            _buffs.PreWarm();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void Inactive_AddTimed_Activates()
        {
            _buffs.AddAbnormalState(2f, 0);

            Assert.That(_buffs.FetchAbnormalState(0), Is.True);
            Assert.That(_buffs.FetchAbnormalStateTime(0), Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void Timed_ThenLongerTimed_Refreshes()
        {
            _buffs.AddAbnormalState(2f, 0);
            _buffs.AddAbnormalState(5f, 0);

            Assert.That(_buffs.FetchAbnormalStateTime(0), Is.EqualTo(5f).Within(1e-4f));
        }

        [Test]
        public void Timed_ThenShorterTimed_KeepsCurrent()
        {
            _buffs.AddAbnormalState(5f, 0);
            _buffs.AddAbnormalState(2f, 0);

            Assert.That(_buffs.FetchAbnormalStateTime(0), Is.EqualTo(5f).Within(1e-4f));
        }

        [Test]
        public void Timed_ThenSameTimed_NoReset()
        {
            _buffs.AddAbnormalState(5f, 0);
            _buffs.AddAbnormalState(5f, 0);

            Assert.That(_buffs.FetchAbnormalStateTime(0), Is.EqualTo(5f).Within(1e-4f));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Timed_ThenPermanent_UpgradesToPermanent(int type)
        {
            _buffs.AddAbnormalState(2f, type);
            _buffs.AddAbnormalState(-10f, type);        // -10：MoveBase.TryToAddImpulse 击退滑行的施加方式

            Assert.That(_buffs.FetchAbnormalStateTime(type), Is.EqualTo(-10f).Within(1e-4f),
                "限时施加后追加永久施加，应升级为永久");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Permanent_ThenTimed_KeepsPermanent(int type)
        {
            _buffs.AddAbnormalState(-10f, type);
            _buffs.AddAbnormalState(2f, type);

            Assert.That(_buffs.FetchAbnormalStateTime(type), Is.EqualTo(-10f).Within(1e-4f),
                "永久状态不被限时施加覆盖");
        }

        [Test]
        public void PermanentBoundary_NegativeFive_IsPermanent()
        {
            _buffs.AddAbnormalState(-5f, 0);
            _buffs.AddAbnormalState(2f, 0);

            Assert.That(_buffs.FetchAbnormalStateTime(0), Is.EqualTo(-5f).Within(1e-4f),
                "-5 为永久阈值（小于等于 -5 即无限持续），不被限时覆盖");
        }

        [Test]
        public void Permanent_TryRemove_Deactivates()
        {
            _buffs.AddAbnormalState(-10f, 0);
            Assert.That(_buffs.FetchAbnormalState(0), Is.True);

            _buffs.TryRemoveAbnormalState(0);

            Assert.That(_buffs.FetchAbnormalState(0), Is.False);
            Assert.That(_buffs.FetchAbnormalStateTime(0), Is.EqualTo(0f).Within(1e-4f));
        }
    }
}
