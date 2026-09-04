using NUnit.Framework;
using UnityEngine;

namespace AbilitySystem.Tests
{
    /// <summary>SplashRadius 数值接入契约：EntityData → store base → EntityStats.SplashRadiusS，
    /// 且可被 Modifier 修饰（ApplyBuff/BuffController 的写入路径即 AttributeStore.AddModifiers）。</summary>
    public class SplashRadiusAttributeTests
    {
        [Test]
        public void SplashRadius_BaseFlowsThroughStore_AndModifiersApply()
        {
            var go = new GameObject("splash-radius-subject");
            try
            {
                var store = new AttributeStore();
                var stats = new EntityStats(go.AddComponent<Entity>());
                stats.Bind(store);
                stats.AttributesCaculateFirst(new EntityData { SplashRadius = 1.5f });

                Assert.That(stats.SplashRadiusS, Is.EqualTo(1.5f).Within(1e-4f));

                // AddFlat：+2 → 3.5
                int flatGroup = store.AddModifiers(new[] { new Modifier("SplashRadius", ModifierOp.AddFlat, 2f) });
                Assert.That(stats.SplashRadiusS, Is.EqualTo(3.5f).Within(1e-4f));

                // AddPercent：+100% → 7
                int percentGroup = store.AddModifiers(new[] { new Modifier("SplashRadius", ModifierOp.AddPercent, 1f) });
                Assert.That(stats.SplashRadiusS, Is.EqualTo(7f).Within(1e-4f));

                // buff 销毁按 group 精确移除：先移 AddFlat → (1.5+0)×2=3，再移 AddPercent → 回到 base
                store.RemoveModifiers(flatGroup);
                Assert.That(stats.SplashRadiusS, Is.EqualTo(3f).Within(1e-4f));
                store.RemoveModifiers(percentGroup);
                Assert.That(stats.SplashRadiusS, Is.EqualTo(1.5f).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
