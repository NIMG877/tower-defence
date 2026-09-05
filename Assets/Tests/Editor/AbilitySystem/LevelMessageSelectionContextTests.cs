using NUnit.Framework;
using UnityEngine;
using MyUI;

namespace AbilitySystem.Tests
{
    /// <summary>
    /// 统一"被查看实体"上下文契约：Inspected 唯一来源、IsDeployed 区分场上场下、
    /// SelectedStaticEntityID 由 Inspected 派生。SelectPlace 依赖 PlaceData 的
    /// 选择器视图层级，EditMode 无法构造，归 PlayMode 手动清单。
    /// </summary>
    public class LevelMessageSelectionContextTests
    {
        private LevelMessageSelectionContext _context;
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _context = new LevelMessageSelectionContext();
            _go = new GameObject("inspected-subject");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void FreshContext_HasNoSelection()
        {
            Assert.That(_context.HasSelection, Is.False);
            Assert.That(_context.SelectedStaticEntityID, Is.Null);
            Assert.That(_context.IsDeployed, Is.False);
        }

        [Test]
        public void SelectEntity_MarksInspectedDeployedAndDerivesID()
        {
            Entity entity = _go.AddComponent<Entity>();
            EntityID id = new EntityID("c3", 3);
            entity.EntityData = new EntityData { ID = id };

            _context.SelectEntity(entity);

            Assert.That(_context.Inspected, Is.EqualTo(entity));
            Assert.That(_context.IsDeployed, Is.True);
            Assert.That(_context.HasSelection, Is.True);
            Assert.That(_context.SelectedStaticEntityID, Is.EqualTo(id));
            Assert.That(_context.SelectedPlaceData, Is.Null);
        }

        [Test]
        public void Clear_ResetsInspection()
        {
            Entity entity = _go.AddComponent<Entity>();
            entity.EntityData = new EntityData { ID = new EntityID("c3", 3) };
            _context.SelectEntity(entity);

            _context.Clear();

            Assert.That(_context.HasSelection, Is.False);
            Assert.That(_context.SelectedStaticEntityID, Is.Null);
            Assert.That(_context.IsDeployed, Is.False);
        }
    }
}
