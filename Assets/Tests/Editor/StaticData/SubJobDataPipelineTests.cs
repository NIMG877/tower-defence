using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace StaticData.Tests
{
    /// <summary>
    /// CharacterSubJob → SubJobTrait 数据读取链路测试（spec 2026-09-05）。
    /// 覆盖 TryParseSubJob 映射与 ResolveSubJobTrait 分支：
    /// 空→静默跳过；未知名/缺映射/缺资产→LogWarning 跳过不打断；已登记→装载资产。
    /// 纯静态数据链路验证，不触碰 EntityDataCollection.asset。
    /// </summary>
    public class SubJobDataPipelineTests
    {
        [Test]
        public void TryParseSubJob_NullOrEmpty_ReturnsZero()
        {
            Assert.IsTrue(XLSX2DataAsset.TryParseSubJob(null, out int a));
            Assert.AreEqual(0, a);
            Assert.IsTrue(XLSX2DataAsset.TryParseSubJob("", out int b));
            Assert.AreEqual(0, b);
        }

        [Test]
        public void TryParseSubJob_KnownNames_MapToIds()
        {
            Assert.IsTrue(XLSX2DataAsset.TryParseSubJob("秘术师", out int id1));
            Assert.AreEqual(1, id1);
            Assert.IsTrue(XLSX2DataAsset.TryParseSubJob("冲锋手", out int id2));
            Assert.AreEqual(2, id2);
            Assert.IsTrue(XLSX2DataAsset.TryParseSubJob("凝滞师", out int id3));
            Assert.AreEqual(3, id3);
        }

        [Test]
        public void TryParseSubJob_UnknownName_ReturnsFalse()
        {
            // "术师"是 CharacterJob 名,不是子职业名——未知名返回 false,由调用方记日志跳过
            Assert.IsFalse(XLSX2DataAsset.TryParseSubJob("术师", out int id));
            Assert.AreEqual(0, id);
        }

        [Test]
        public void ResolveSubJobTrait_EmptyCell_NoTrait()
        {
            var (subJob, trait) = XLSX2DataAsset.ResolveSubJobTrait("");
            Assert.AreEqual(0, subJob);
            Assert.IsNull(trait);
        }

        [Test]
        public void ResolveSubJobTrait_KnownSubJob_LoadsAsset()
        {
            // 走生产注册表:占位资产已在 Prefabs/Abilities/SubJobs/ 下,应能 Resources.Load 到
            var (subJob, trait) = XLSX2DataAsset.ResolveSubJobTrait("秘术师");
            Assert.AreEqual(1, subJob);
            Assert.IsNotNull(trait);
            Assert.AreEqual("mystic_t0", trait.abilityId);
        }

        [Test]
        public void ResolveSubJobTrait_UnknownName_WarnsAndSkips()
        {
            // 未知名不打断 Rebuild:LogWarning + 按无子职业处理
            LogAssert.Expect(LogType.Warning, new Regex("Unknown CharacterSubJob name"));
            var (subJob, trait) = XLSX2DataAsset.ResolveSubJobTrait("术师");
            Assert.AreEqual(0, subJob);
            Assert.IsNull(trait);
        }

        [Test]
        public void ResolveSubJobTrait_UnregisteredId_LogsWarningAndSkips()
        {
            var emptyPaths = new Dictionary<int, string>();
            LogAssert.Expect(LogType.Warning, new Regex("no trait asset registered"));
            var (subJob, trait) = XLSX2DataAsset.ResolveSubJobTrait("秘术师", emptyPaths);
            Assert.AreEqual(1, subJob);
            Assert.IsNull(trait);
        }

        [Test]
        public void ResolveSubJobTrait_MissingAsset_LogsWarningAndSkips()
        {
            var paths = new Dictionary<int, string> { [1] = "Prefabs/Abilities/SubJobs/does_not_exist" };
            LogAssert.Expect(LogType.Warning, new Regex("trait asset not found"));
            var (subJob, trait) = XLSX2DataAsset.ResolveSubJobTrait("秘术师", paths);
            Assert.AreEqual(1, subJob);
            Assert.IsNull(trait);
        }
    }
}
