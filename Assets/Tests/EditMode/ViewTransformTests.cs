using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public class ViewTransformTests
    {
        [Test]
        public void SnapToGrid_ReturnsHalfInteger()
        {
            var v = ViewTransform.SnapToGrid(new Vector2(1.2f, 2.7f));
            Assert.AreEqual(1.5f, v.x);
            Assert.AreEqual(2.5f, v.y);
        }

        [Test]
        public void SnapToGrid_NegativeRoundsToZero()
        {
            var v = ViewTransform.SnapToGrid(new Vector2(-0.4f, -0.4f));
            Assert.AreEqual(-0.5f, v.x);
            Assert.AreEqual(-0.5f, v.y);
        }

        [Test]
        public void WorldToScreen_RoundTripsWithScreenToWorld()
        {
            var vt = new ViewTransform { Offset = Vector2.zero, Zoom = 1f };
            var world = new Vector2(3.5f, 2.5f);
            var screen = vt.WorldToScreen(world, iSize: 5, jSize: 5);
            var back = vt.ScreenToWorld(screen, iSize: 5, jSize: 5);
            Assert.AreEqual(world.x, back.x, 0.001f);
            Assert.AreEqual(world.y, back.y, 0.001f);
        }

        [Test]
        public void Fit_MakesLargestDimensionTouchCanvas()
        {
            // 假设 iSize=8, jSize=12, Canvas 600x400
            // 最小维度 = min(600/12, 400/8) = min(50, 50) = 50
            var vt = ViewTransform.Fit(iSize: 8, jSize: 12);
            Assert.AreEqual(50f, vt.Zoom, 0.001f);
        }
    }
}
