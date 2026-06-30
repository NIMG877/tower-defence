using NUnit.Framework;
using UnityEngine;

namespace Wave.Tests
{
    public class LevelDataValidatorTests
    {
        static LevelData MakeValidLevelData()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.LevelName = "Test";
            ld.LevelCode = "T01";
            ld.iSize = 3;
            ld.jSize = 3;
            ld.MapData = new System.Collections.Generic.List<Tile>();
            ld.MapPrefab = null;
            ld.CutToLevelTexture = null;
            ld.CameraSize = 5f;
            ld.LevelHp = 10;
            ld.MaxCost = 100;
            ld.Cost0 = 50;
            ld.Waves = new[]
            {
                new LevelActions.Wave
                {
                    Tracks = new[]
                    {
                        new LevelActions.Track
                        {
                            Actions = new[]
                            {
                                new LevelActions.Action { CommandType = 0, TriggerTime = 1f, GapsFromLastRepeat = new float[0] }
                            }
                        }
                    }
                }
            };
            return ld;
        }

        [Test]
        public void Validator_flags_negative_trigger_time_as_error()
        {
            var ld = MakeValidLevelData();
            var action = ld.Waves[0].Tracks[0].Actions[0];
            action.TriggerTime = -0.5f;
            ld.Waves[0].Tracks[0].Actions[0] = action;

            var issues = Validation.LevelDataValidator.Validate(ld);
            Assert.IsTrue(issues.Exists(i => i.Severity == Validation.ValidationSeverity.Error && i.Message.Contains("TriggerTime")),
                $"expected TriggerTime error, got: {string.Join("; ", issues.ConvertAll(i => i.Message))}");

            Object.DestroyImmediate(ld);
        }

        [Test]
        public void Validator_passes_valid_level_data_no_trigger_time_errors()
        {
            var ld = MakeValidLevelData();
            var issues = Validation.LevelDataValidator.Validate(ld);

            // 应当没有 TriggerTime 相关错误
            var trigErrors = issues.FindAll(i => i.Message.Contains("TriggerTime"));
            Assert.AreEqual(0, trigErrors.Count, $"unexpected TriggerTime errors: {string.Join("; ", trigErrors.ConvertAll(i => i.Message))}");

            Object.DestroyImmediate(ld);
        }
    }
}
