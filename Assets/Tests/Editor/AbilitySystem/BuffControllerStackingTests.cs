using NUnit.Framework;
using UnityEngine;

namespace AbilitySystem.Tests
{
    /// <summary>
    /// 同名 buff 重复施加的叠加契约（再部署费用增幅的承载机制）：
    /// 同名 buff 各持独立 modifier 组并存，AddPercent 按 store 累加和
    /// base×(1+ΣM) 结算——加法累积，非乘法复合。
    /// </summary>
    public class BuffControllerStackingTests
    {
        private GameObject _go;
        private AttributeStore _store;
        private BuffController _buffs;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("buff-stacking-subject");
            _go.AddComponent<Entity>();                 // BuffController.PreWarm 里 GetComponent<Entity>
            var temp = new GameObject("TempContainer").transform;
            temp.parent = _go.transform;
            _go.GetComponent<Entity>().TempContainer = temp;
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
        public void SameNameLevelBuffs_StackAdditively()
        {
            _buffs.CreateBuff(new[] { new Modifier("Cost", ModifierOp.AddPercent, 0.5f) },
                null, "redeploy_cost_up", -5f, BuffScope.Level);
            _buffs.CreateBuff(new[] { new Modifier("Cost", ModifierOp.AddPercent, 0.5f) },
                null, "redeploy_cost_up", -5f, BuffScope.Level);

            // 10 × (1 + 0.5 + 0.5) = 20；两层独立 buff 而非单层刷新
            Assert.That(_store.GetFinal("Cost"), Is.EqualTo(20f).Within(1e-4f));
            Assert.That(_buffs.Buffs.Count, Is.EqualTo(2));
        }

        [Test]
        public void StackedLevelBuffs_SurviveDormancyTogether()
        {
            _buffs.CreateBuff(new[] { new Modifier("Cost", ModifierOp.AddPercent, 0.5f) },
                null, "redeploy_cost_up", -5f, BuffScope.Level);
            _buffs.CreateBuff(new[] { new Modifier("Cost", ModifierOp.AddPercent, 0.5f) },
                null, "redeploy_cost_up", -5f, BuffScope.Level);

            _buffs.Dormancy();

            // 回收（换手/再部署间隙）不清局内 buff：两层均保留
            Assert.That(_store.GetFinal("Cost"), Is.EqualTo(20f).Within(1e-4f));
            Assert.That(_buffs.Buffs.Count, Is.EqualTo(2));
        }

        [Test]
        public void PercentStack_ComposesWithFlatModifier()
        {
            // 同属性上 Flat 与 Percent 共存时按四段公式结算：
            // (base + F) × (1 + M) —— 例：天赋 -1 与增幅 +50% → (10-1)×1.5 = 13.5
            _buffs.CreateBuff(new[] { new Modifier("Cost", ModifierOp.AddFlat, -1f) },
                null, "talent_cost", -5f, BuffScope.Level);
            _buffs.CreateBuff(new[] { new Modifier("Cost", ModifierOp.AddPercent, 0.5f) },
                null, "redeploy_cost_up", -5f, BuffScope.Level);

            Assert.That(_store.GetFinal("Cost"), Is.EqualTo(13.5f).Within(1e-4f));
        }

        [Test]
        public void SetBuffValues_ReplacesMagnitude_InPlace()
        {
            // 再部署费用增幅回池#2 的换值路径：同名 buff 量 1×→2×，原地换快照——
            // 终值按新组结算、无旧组残留（10×1.5=15 → 10×2=20），条目数不变
            Buff buff = _buffs.CreateBuff(new[] { new Modifier("Cost", ModifierOp.AddPercent, 0.5f) },
                null, "redeploy_cost_up", -5f, BuffScope.Level);
            Assert.That(_store.GetFinal("Cost"), Is.EqualTo(15f).Within(1e-4f));

            _buffs.SetBuffValues(new[] { new Modifier("Cost", ModifierOp.AddPercent, 1f) }, buff);

            Assert.That(_store.GetFinal("Cost"), Is.EqualTo(20f).Within(1e-4f));
            Assert.That(_buffs.Buffs.Count, Is.EqualTo(1));
            Assert.That(_buffs.Buffs[0].modifiers[0].magnitude, Is.EqualTo(1f));
        }
    }
}
