using NUnit.Framework;
using UnityEngine;

namespace AbilitySystem.Tests
{
    /// <summary>
    /// 局内 buff（BuffScope.Level）生命周期契约：
    /// Dormancy（回收）保留局内 buff，且只按 group 移除 Normal/WhiteList 两列的 modifier；
    /// ClearLevelBuffs（实体离开实体池，如退出关卡）才移除局内 buff。
    /// </summary>
    public class BuffControllerLevelBuffTests
    {
        private GameObject _go;
        private AttributeStore _store;
        private BuffController _buffs;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("level-buff-subject");
            _go.AddComponent<Entity>();                 // BuffController.PreWarm 里 GetComponent<Entity>
            var temp = new GameObject("TempContainer").transform;
            temp.parent = _go.transform;
            _go.GetComponent<Entity>().TempContainer = temp;   // 带特效的 CreateBuff 要挂这里
            _store = new AttributeStore();
            _store.SetBase("Cost", 10f);
            _buffs = _go.AddComponent<BuffController>();
            _buffs.Bind(_store);
            _buffs.PreWarm();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void LevelBuff_SurvivesDormancy_NormalBuffDoesNot()
        {
            _buffs.CreateBuff(new[] { new Modifier("Cost", ModifierOp.AddFlat, 2f) }, null, "normal", -5f, BuffScope.Normal);
            _buffs.CreateBuff(new[] { new Modifier("Cost", ModifierOp.AddFlat, -1f) }, null, "level", -5f, BuffScope.Level);
            Assert.That(_store.GetFinal("Cost"), Is.EqualTo(11f).Within(1e-4f));

            _buffs.Dormancy();

            // 普通 buff 已按 group 移除，局内 buff 保留：10 - 1 = 9
            Assert.That(_store.GetFinal("Cost"), Is.EqualTo(9f).Within(1e-4f));
        }

        [Test]
        public void ClearLevelBuffs_RemovesLevelBuff_ModifiersAndList()
        {
            Buff levelBuff = _buffs.CreateBuff(new[] { new Modifier("Cost", ModifierOp.AddFlat, -1f) }, null, "level", -5f, BuffScope.Level);
            Assert.That(_store.GetFinal("Cost"), Is.EqualTo(9f).Within(1e-4f));

            _buffs.ClearLevelBuffs();

            Assert.That(_store.GetFinal("Cost"), Is.EqualTo(10f).Within(1e-4f));
            Assert.That(_buffs.ContainsBuff(levelBuff), Is.False);
            Assert.That(_buffs.Buffs, Is.Empty);
        }

        [Test]
        public void LevelBuff_VisibleThroughBuffsAndContainsBuff()
        {
            Buff levelBuff = _buffs.CreateBuff(new[] { new Modifier("Cost", ModifierOp.AddFlat, -1f) }, null, "level", -5f, BuffScope.Level);

            Assert.That(_buffs.ContainsBuff(levelBuff), Is.True);
            Assert.That(_buffs.Buffs, Has.Member(levelBuff));
        }

        [Test]
        public void DormancyTwice_LevelBuffStillIntact()
        {
            _buffs.CreateBuff(new[] { new Modifier("Cost", ModifierOp.AddFlat, -1f) }, null, "level", -5f, BuffScope.Level);

            _buffs.Dormancy();
            _buffs.Dormancy();

            Assert.That(_store.GetFinal("Cost"), Is.EqualTo(9f).Within(1e-4f));
        }

        [Test]
        public void Dormancy_NullsLevelBuffEffect_KeepsModifier()
        {
            var effect = new GameObject("level-buff-effect");
            try
            {
                Buff levelBuff = _buffs.CreateBuff(new[] { new Modifier("Cost", ModifierOp.AddFlat, -1f) },
                    effect, "leveled", -5f, BuffScope.Level);
                Assert.That(levelBuff.buff_effect, Is.Not.Null);

                _buffs.Dormancy();

                // 特效引用随回收清空（特效本体随 TempContainer 销毁），数值效果保留
                Assert.That(levelBuff.buff_effect, Is.Null);
                Assert.That(_store.GetFinal("Cost"), Is.EqualTo(9f).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(effect);
            }
        }
    }
}
