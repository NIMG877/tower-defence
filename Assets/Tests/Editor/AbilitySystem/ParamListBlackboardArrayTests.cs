using NUnit.Framework;

namespace AbilitySystem.Tests
{
    /// <summary>ParamList GetXxxArrayLazy 的 fromBlackboard 路径类型契约：
    /// 数值标量无损方向(int 值读 float[])收进读取器；有损方向(double 值读 float[]、
    /// float 值读 int[])不做静默转换，保留一次性类型告警并回退 defaultValue。</summary>
    public class ParamListBlackboardArrayTests
    {
        [Test]
        public void IntScalar_InBB_ReadsAsFloatArray()
        {
            var bb = new Blackboard();
            bb.Set("k", 3); // write_blackboard 字面量 type:Int / int 属性装箱的产物

            float[] result = new ParamList().GetFloatArrayLazy("k", null, bb)();

            Assert.That(result, Is.EqualTo(new[] { 3f }));
        }

        [Test]
        public void FloatAndStringScalars_StillRead()
        {
            var bb = new Blackboard();
            bb.Set("f", 2.5f);
            bb.Set("s", "1.5,2.5");

            Assert.That(new ParamList().GetFloatArrayLazy("f", null, bb)(), Is.EqualTo(new[] { 2.5f }));
            Assert.That(new ParamList().GetFloatArrayLazy("s", null, bb)(), Is.EqualTo(new[] { 1.5f, 2.5f }));
        }

        [Test]
        public void DoubleScalar_LossyDirection_FallsBackToDefault()
        {
            var bb = new Blackboard();
            bb.Set("k", 2.5d);
            var fallback = new[] { 9f };

            Assert.That(new ParamList().GetFloatArrayLazy("k", fallback, bb)(), Is.EqualTo(fallback));
        }

        [Test]
        public void FloatScalar_LossyDirection_FallsBackToDefault()
        {
            var bb = new Blackboard();
            bb.Set("k", 2.5f);
            var fallback = new[] { 9 };

            Assert.That(new ParamList().GetIntArrayLazy("k", fallback, bb)(), Is.EqualTo(fallback));
        }

        [Test]
        public void IntScalar_InBB_StillReadsAsIntArray()
        {
            var bb = new Blackboard();
            bb.Set("k", 3);

            Assert.That(new ParamList().GetIntArrayLazy("k", null, bb)(), Is.EqualTo(new[] { 3 }));
        }
    }
}
