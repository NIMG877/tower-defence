using NUnit.Framework;

namespace AbilitySystem.Tests
{
    /// <summary>OrderLogic 序号契约：EntityDataCollection.asset 按枚举序号序列化
    /// TargetPriority，XLSX2DataAsset 导入亦按 int 落值。枚举只允许末尾追加，
    /// 不得插入或重排（同 BeforeTargetSelectBridgeTests 的 TriggerEvent 契约）。</summary>
    public class OrderLogicTests
    {
        [Test]
        public void OrderLogic_Members_AppendedAtEndWithoutRenumbering()
        {
            Assert.That((int)OrderLogic.Defense_Des, Is.EqualTo(3));
            Assert.That((int)OrderLogic.VisionFirst_Priority_Des, Is.EqualTo(4));
        }
    }
}
