using System.Linq;
using NUnit.Framework;
using UnityEngine;

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
            _data.CheckPoints = new GameObject[0];
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
    }
}
