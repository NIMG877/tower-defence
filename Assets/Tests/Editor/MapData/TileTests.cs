using NUnit.Framework;
using UnityEngine;

namespace MapData.Tests
{
    public class TileTests
    {
        [Test]
        public void Default_construct_has_all_passable_zero_and_no_portal()
        {
            var t = new Tile { i = 3, j = 7 };
            Assert.AreEqual(3, t.i);
            Assert.AreEqual(7, t.j);
            Assert.AreEqual(0, t.passableType);
            Assert.IsFalse(t.highland);
            Assert.IsFalse(t.canSet);
            Assert.IsFalse(t.deadly);
            Assert.AreEqual(-1, t.portalOutI);
            Assert.AreEqual(-1, t.portalOutJ);
        }

        [Test]
        public void Equality_matches_field_by_field()
        {
            var a = new Tile { i = 1, j = 2, passableType = 3, portalOutI = 4, portalOutJ = 5 };
            var b = new Tile { i = 1, j = 2, passableType = 3, portalOutI = 4, portalOutJ = 5 };
            var c = new Tile { i = 1, j = 2, passableType = 3, portalOutI = 4, portalOutJ = 6 };
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
        }
    }
}