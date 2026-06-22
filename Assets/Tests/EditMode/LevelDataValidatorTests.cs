using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Validation;

namespace Tests.EditMode
{
    public class LevelDataValidatorTests
    {
        LevelData _data;

        [SetUp]
        public void SetUp()
        {
            _data = ScriptableObject.CreateInstance<LevelData>();
            // 填一个能通过大部分规则的最小有效配置
            _data.LevelName = "TestLevel";
            _data.LevelCode = "test";
            _data.CameraSize = 5f;
            _data.CameraPos = Vector2.zero;
            _data.MapPrefab = new GameObject("dummy_map");
            _data.EnvironmentalControlDevice = null;
            _data.Waves = new LevelActions.Wave[] { new LevelActions.Wave { Actions = new LevelActions.Action[0] } };
#pragma warning disable CS0618
            _data.CheckPoints = new GameObject[0];
#pragma warning restore CS0618
            _data.WaveEntityPrefabIDs = new EntityID[] { new EntityID("c", 1) };
            _data.LevelHp = 10;
            _data.Cost0 = 100;
            _data.MaxCost = 200;
            _data.CanSetNum = 5;
            _data.CostRecoverSpeed = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            if (_data.MapPrefab != null) Object.DestroyImmediate(_data.MapPrefab);
            Object.DestroyImmediate(_data);
        }

        [Test]
        public void Validate_EmptyWaves_ReturnsError()
        {
            _data.Waves = new LevelActions.Wave[0];
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Path.Contains("Waves")),
                $"Expected error mentioning Waves, got: {string.Join("; ", issues.Select(i => i.Message))}");
        }

        [Test]
        public void Validate_WaveWithEmptyActions_ReturnsWarning()
        {
            _data.Waves = new LevelActions.Wave[] {
                new LevelActions.Wave { Actions = new LevelActions.Action[0] }
            };
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Warning && i.Message.Contains("空")),
                $"Expected warning about empty Wave actions, got: {string.Join("; ", issues.Select(i => i.Message))}");
        }

        [Test]
        public void Validate_EmptyWaveEntityPrefabIDs_ReturnsError()
        {
            _data.WaveEntityPrefabIDs = new EntityID[0];
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Path.Contains("WaveEntityPrefabIDs")),
                $"Expected error about WaveEntityPrefabIDs, got: {string.Join("; ", issues.Select(i => i.Message))}");
        }

        [Test]
        public void Validate_EntityPrefabSerialOutOfRange_ReturnsError()
        {
            _data.Waves = new LevelActions.Wave[] {
                new LevelActions.Wave { Actions = new LevelActions.Action[] {
                    new LevelActions.Action { CommandType = 0, EntityPrefabSerial = 99 }
                } }
            };
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Message.Contains("EntityPrefabSerial")),
                $"Expected error about EntityPrefabSerial range, got: {string.Join("; ", issues.Select(i => i.Message))}");
        }

        [Test]
        public void Validate_LevelHpZeroOrNegative_ReturnsError()
        {
            _data.LevelHp = 0;
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Path.Contains("LevelHp")));
        }

        [Test]
        public void Validate_MaxCostLessThanCost0_ReturnsError()
        {
            _data.Cost0 = 200;
            _data.MaxCost = 100;
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Path.Contains("MaxCost")));
        }

        [Test]
        public void Validate_NullMapPrefab_ReturnsError()
        {
            _data.MapPrefab = null;
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Path.Contains("MapPrefab")));
        }

        [Test]
        public void Validate_NullCutToLevelTexture_ReturnsWarning()
        {
            _data.CutToLevelTexture = null;
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Warning && i.Path.Contains("CutToLevelTexture")));
        }

        [Test]
        public void Validate_ZeroCameraSize_ReturnsError()
        {
            _data.CameraSize = 0f;
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Path.Contains("CameraSize")));
        }

        [Test]
        public void Validate_EmptyPaths_ReturnsWarning()
        {
            _data.Paths = new LevelData.PathData[0];
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Warning && i.Path.Contains("Paths")),
                $"Expected warning about empty Paths, got: {string.Join("; ", issues.Select(i => i.Message))}");
        }

        [Test]
        public void Validate_PathWithLessThanTwoCheckpoints_ReturnsError()
        {
            _data.Paths = new LevelData.PathData[] {
                new LevelData.PathData {
                    CheckPoints = new Vector2[] { new Vector2(1f, 1f) },
                    WaitTimes = new float[] { 0f }
                }
            };
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Path.Contains("Paths") && i.Message.Contains("2")),
                $"Expected error about <2 checkpoints, got: {string.Join("; ", issues.Select(i => i.Message))}");
        }

        [Test]
        public void Validate_PathCheckPointLengthMismatchWaitTimesLength_ReturnsError()
        {
            _data.Paths = new LevelData.PathData[] {
                new LevelData.PathData {
                    CheckPoints = new Vector2[] { new Vector2(1f, 1f), new Vector2(2f, 2f) },
                    WaitTimes = new float[] { 0f } // length mismatch
                }
            };
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Error && i.Message.Contains("WaitTimes")),
                $"Expected error about CheckPoints/WaitTimes length mismatch, got: {string.Join("; ", issues.Select(i => i.Message))}");
        }

        [Test]
        public void Validate_PathCheckPointNegativeWaitTime_ReturnsWarning()
        {
            _data.Paths = new LevelData.PathData[] {
                new LevelData.PathData {
                    CheckPoints = new Vector2[] { new Vector2(1f, 1f), new Vector2(2f, 2f) },
                    WaitTimes = new float[] { 0f, -1f }
                }
            };
            var issues = LevelDataValidator.Validate(_data);
            Assert.IsTrue(issues.Any(i => i.Severity == ValidationSeverity.Warning && i.Message.Contains("WaitTime")),
                $"Expected warning about negative WaitTime, got: {string.Join("; ", issues.Select(i => i.Message))}");
        }
    }
}
