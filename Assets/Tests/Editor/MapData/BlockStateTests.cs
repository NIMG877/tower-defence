using NUnit.Framework;

namespace MapData.Tests
{
    public class BlockStateTests
    {
        [Test]
        public void Default_construct_is_ground_walkable_with_no_portal_and_null_material()
        {
            var s = default(BlockState);
            Assert.IsFalse(s.highland);
            Assert.IsFalse(s.canSet);
            Assert.IsFalse(s.deadly);
            Assert.AreEqual(0, s.passableType);
            Assert.AreEqual(-1, s.portalOutI);
            Assert.AreEqual(-1, s.portalOutJ);
            Assert.IsNull(s.material);
        }
    }
}