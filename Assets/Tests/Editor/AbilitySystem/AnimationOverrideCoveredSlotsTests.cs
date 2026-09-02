using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Spine.Unity;

namespace AbilitySystem.Tests
{
    /// <summary>AnimationOverride.GetCoveredSlots（一次性覆盖的消费槽位集合）的 EditMode 契约测试。
    /// 覆盖槽 = 非空资源槽 + ClearedSlots；空值不算。一次性覆盖条目靠它初始化待消费集合，
    /// 漏一个槽就永远不消费（条目滞留），所以逐槽位断言。</summary>
    public class AnimationOverrideCoveredSlotsTests
    {
        [Test]
        public void CoveredSlots_MixesSetResourcesAndClearedSlots_SkipsEmpty()
        {
            var ov = new AnimationOverride { [AnimationSlot.Idle] = "anim_idle", [AnimationSlot.AttackRemote] = "anim_atk" };
            ov.Clear(AnimationSlot.Die);

            HashSet<AnimationSlot> covered = ov.GetCoveredSlots();

            Assert.That(covered, Is.EquivalentTo(new[]
            {
                AnimationSlot.Idle, AnimationSlot.AttackRemote, AnimationSlot.Die,
            }));
        }

        [Test]
        public void CoveredSlots_AllSixteenSlots_WhenFullyPopulated()
        {
            var ov = new AnimationOverride
            {
                [AnimationSlot.Default] = "a", [AnimationSlot.Idle] = "a", [AnimationSlot.Move] = "a",
                [AnimationSlot.JumpBegin] = "a", [AnimationSlot.JumpLoop] = "a", [AnimationSlot.JumpEnd] = "a",
                [AnimationSlot.Start] = "a", [AnimationSlot.Cast] = "a", [AnimationSlot.Die] = "a",
                [AnimationSlot.AttackBegin] = "a", [AnimationSlot.AttackEnd] = "a",
                [AnimationSlot.AttackRemote] = "a", [AnimationSlot.AttackClose] = "a",
                [AnimationSlot.ChargeBegin] = "a", [AnimationSlot.Charge] = "a", [AnimationSlot.ChargeEnd] = "a",
            };

            Assert.That(ov.GetCoveredSlots().Count, Is.EqualTo(16));
        }

        [Test]
        public void CoveredSlots_EmptyOverride_IsEmpty()
        {
            Assert.That(new AnimationOverride().GetCoveredSlots(), Is.Empty);
        }

        [Test]
        public void Indexer_NullOrEmptyValue_RemovesSlot()
        {
            var ov = new AnimationOverride { [AnimationSlot.Idle] = "x" };
            ov[AnimationSlot.Idle] = null;
            Assert.That(ov.GetCoveredSlots(), Is.Empty);
            Assert.That(ov[AnimationSlot.Idle], Is.Null);
        }

        [Test]
        public void Indexer_GetUnsetSlot_ReturnsNull()
        {
            Assert.That(new AnimationOverride()[AnimationSlot.Die], Is.Null);
        }
    }

    /// <summary>AnimationSet（槽位字典）解析契约：覆盖应用、清槽、组/单槽路由、拷贝独立性。</summary>
    public class AnimationSetTests
    {
        private static AnimationReferenceAsset Anim(string name)
        {
            var asset = ScriptableObject.CreateInstance<AnimationReferenceAsset>();
            asset.name = name;
            return asset;
        }

        [Test]
        public void Apply_RoutesSingleAndGroupSlots_BySlotKind()
        {
            AnimationReferenceAsset idle = Anim("idle"), cast = Anim("cast");
            AnimationReferenceAsset[] remote = { Anim("r1"), Anim("r2") };
            var resolved = new AnimationSet();
            var ov = new AnimationOverride
            {
                [AnimationSlot.Idle] = "idle",
                [AnimationSlot.Cast] = "cast",
                [AnimationSlot.AttackRemote] = "remote",
            };

            resolved.Apply(ov,
                name => name == "idle" ? idle : cast,
                name => remote);

            Assert.That(resolved.GetSingle(AnimationSlot.Idle), Is.SameAs(idle));
            Assert.That(resolved.GetSingle(AnimationSlot.Cast), Is.SameAs(cast));
            Assert.That(resolved.GetGroup(AnimationSlot.AttackRemote), Is.SameAs(remote));
        }

        [Test]
        public void Apply_ClearSlots_RemovesResolvedEntries()
        {
            var resolved = new AnimationSet();
            resolved.SetSingle(AnimationSlot.Idle, Anim("idle"));
            resolved.SetGroup(AnimationSlot.Charge, new[] { Anim("c") });

            var ov = new AnimationOverride();
            ov.Clear(AnimationSlot.Idle, AnimationSlot.Charge);
            resolved.Apply(ov, name => Anim(name), name => new[] { Anim(name) });

            Assert.That(resolved.GetSingle(AnimationSlot.Idle), Is.Null);
            Assert.That(resolved.GetGroup(AnimationSlot.Charge), Is.Null);
        }

        [Test]
        public void Copy_IsIndependentOfSource()
        {
            var source = new AnimationSet();
            source.SetSingle(AnimationSlot.Idle, Anim("idle"));
            AnimationSet copy = source.Copy();
            copy.SetSingle(AnimationSlot.Idle, Anim("other"));

            Assert.That(source.GetSingle(AnimationSlot.Idle).name, Is.EqualTo("idle"));
        }

        [Test]
        public void IsGroup_DistinguishesGroupSlots()
        {
            Assert.That(AnimationSet.IsGroup(AnimationSlot.AttackRemote), Is.True);
            Assert.That(AnimationSet.IsGroup(AnimationSlot.AttackClose), Is.True);
            Assert.That(AnimationSet.IsGroup(AnimationSlot.Charge), Is.True);
            Assert.That(AnimationSet.IsGroup(AnimationSlot.Idle), Is.False);
            Assert.That(AnimationSet.IsGroup(AnimationSlot.Cast), Is.False);
        }

        [Test]
        public void Apply_UnknownResourceName_ThrowsKeyNotFound()
        {
            var resolved = new AnimationSet();
            var ov = new AnimationOverride { [AnimationSlot.Idle] = "nope" };
            // 解析器抛 KeyNotFound（等价 AnimationResources.GetAnimation 的行为）→ 配置错误必须暴露
            Assert.Throws<KeyNotFoundException>(
                () => resolved.Apply(ov, name => throw new KeyNotFoundException(name), name => null));
        }
    }
}
