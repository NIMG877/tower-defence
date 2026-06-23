using NUnit.Framework;
using UnityEngine;

namespace MapData.Tests
{
    public class BlockDataEntryTests
    {
        [Test]
        public void Default_construct_has_all_passable_zero_and_no_portal()
        {
            var e = new BlockDataEntry { i = 3, j = 7 };
            Assert.AreEqual(3, e.i);
            Assert.AreEqual(7, e.j);
            Assert.AreEqual(0, e.passableType);
            Assert.IsFalse(e.highland);
            Assert.IsFalse(e.canSet);
            Assert.IsFalse(e.deadly);
            Assert.AreEqual(-1, e.portalOutI);
            Assert.AreEqual(-1, e.portalOutJ);
            Assert.AreEqual(default(Color), e.portalColor);
        }

        [Test]
        public void ToBlockState_copies_all_fields_and_resets_material_to_null()
        {
            var e = new BlockDataEntry
            {
                i = 1, j = 2,
                highland = true,
                canSet = true,
                passableType = 2,
                deadly = true,
                portalOutI = 5,
                portalOutJ = 6,
                portalColor = new Color(0.1f, 0.2f, 0.3f, 0.4f),
            };
            var s = e.ToBlockState();
            Assert.IsTrue(s.highland);
            Assert.IsTrue(s.canSet);
            Assert.AreEqual(2, s.passableType);
            Assert.IsTrue(s.deadly);
            Assert.AreEqual(5, s.portalOutI);
            Assert.AreEqual(6, s.portalOutJ);
            Assert.AreEqual(new Color(0.1f, 0.2f, 0.3f, 0.4f), s.portalColor);
            Assert.IsNull(s.material);
        }

        [Test]
        public void Equality_matches_field_by_field()
        {
            var a = new BlockDataEntry { i = 1, j = 2, passableType = 3, portalOutI = 4, portalOutJ = 5 };
            var b = new BlockDataEntry { i = 1, j = 2, passableType = 3, portalOutI = 4, portalOutJ = 5 };
            var c = new BlockDataEntry { i = 1, j = 2, passableType = 3, portalOutI = 4, portalOutJ = 6 };
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
        }
    }
}