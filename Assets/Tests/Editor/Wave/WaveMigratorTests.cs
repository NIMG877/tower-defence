using NUnit.Framework;
using UnityEngine;

namespace Wave.Tests
{
    public class WaveMigratorTests
    {
        // 模拟 v1 资产:Wave.Actions[] 含 N 个 Action,各自有 GapFromLastAction。
        // 调用 Migrate 后:Actions[].GapFromLastAction 应转换为单条 Track[0].Actions[].TriggerTime。
        // 模拟"旧数据"的方式:构造 Action[] 然后强行塞进 wave.LegacyActions(模拟 FormerlySerializedAs 已经把数据搬过来)。
        // 由于 FormerlySerializedAs 是 Unity 序列化器在加载时才生效,测试里直接操作 LegacyActions。

        static LevelActions.Action MakeV1Action(float gap, int commandType = 0)
        {
            var a = new LevelActions.Action
            {
                CommandType = commandType,
                GapFromLastAction = gap,
                TriggerTime = 0f,
                GapsFromLastRepeat = new float[0],
            };
            return a;
        }

        [Test]
        public void Migrate_single_wave_with_3_actions_creates_one_default_track_with_absolute_trigger_times()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.SchemaVersion = 0; // v1
            var wave = new LevelActions.Wave
            {
                LegacyActions = new[]
                {
                    MakeV1Action(0f),         // 第 1 条:TriggerTime = 0
                    MakeV1Action(2.5f),       // 第 2 条:TriggerTime = 0 + 2.5 = 2.5
                    MakeV1Action(1f),         // 第 3 条:TriggerTime = 2.5 + 1 = 3.5
                },
            };
            ld.Waves = new[] { wave };

            WaveMigrator.MigrateLevelData(ld);

            Assert.AreEqual(2, ld.SchemaVersion, "SchemaVersion should be bumped to 2");
            Assert.IsNull(ld.Waves[0].LegacyActions, "LegacyActions should be cleared");
            Assert.IsNotNull(ld.Waves[0].Tracks, "Tracks should be populated");
            Assert.AreEqual(1, ld.Waves[0].Tracks.Length, "single default track");
            Assert.AreEqual("默认", ld.Waves[0].Tracks[0].Name, "default track name");
            Assert.AreEqual(false, ld.Waves[0].Tracks[0].Locked);
            Assert.AreEqual(3, ld.Waves[0].Tracks[0].Actions.Length, "all actions migrated");

            var actions = ld.Waves[0].Tracks[0].Actions;
            Assert.AreEqual(0f, actions[0].TriggerTime, 0.0001f, "first action TriggerTime = 0");
            Assert.AreEqual(2.5f, actions[1].TriggerTime, 0.0001f);
            Assert.AreEqual(3.5f, actions[2].TriggerTime, 0.0001f);

            Object.DestroyImmediate(ld);
        }

        [Test]
        public void Migrate_skips_level_data_already_at_v2()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.SchemaVersion = 2;
            ld.Waves = new[]
            {
                new LevelActions.Wave { Tracks = new LevelActions.Track[0] }
            };

            // 不应改 SchemaVersion 或抛错
            Assert.DoesNotThrow(() => WaveMigrator.MigrateLevelData(ld));
            Assert.AreEqual(2, ld.SchemaVersion);
            Assert.AreEqual(0, ld.Waves[0].Tracks.Length);

            Object.DestroyImmediate(ld);
        }

        [Test]
        public void Migrate_skips_wave_with_null_legacy_actions()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.SchemaVersion = 0;
            ld.Waves = new[]
            {
                new LevelActions.Wave { LegacyActions = null, Tracks = new LevelActions.Track[0] }
            };

            WaveMigrator.MigrateLevelData(ld);

            Assert.AreEqual(2, ld.SchemaVersion);
            Assert.AreEqual(0, ld.Waves[0].Tracks.Length, "未修改 Tracks(已是新结构)");

            Object.DestroyImmediate(ld);
        }

        [Test]
        public void Migrate_negative_gap_clamps_to_zero()
        {
            // Wave 设计师在 v1 可能误填负数 gap,迁移时应取 max(0, gap) 不引入负 TriggerTime
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.SchemaVersion = 0;
            ld.Waves = new[]
            {
                new LevelActions.Wave
                {
                    LegacyActions = new[]
                    {
                        MakeV1Action(0f),
                        MakeV1Action(-1f), // 异常输入
                    }
                }
            };

            WaveMigrator.MigrateLevelData(ld);

            Assert.AreEqual(1f, ld.Waves[0].Tracks[0].Actions[1].TriggerTime, 0.0001f,
                "negative gap should not reduce TriggerTime below previous cumulative");

            Object.DestroyImmediate(ld);
        }

        [Test]
        public void Migrate_default_track_color_matches_palette_index_0()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.SchemaVersion = 0;
            ld.Waves = new[]
            {
                new LevelActions.Wave
                {
                    LegacyActions = new[] { MakeV1Action(0f) }
                }
            };

            WaveMigrator.MigrateLevelData(ld);

            // 灰: (0.55, 0.55, 0.55)
            var c = ld.Waves[0].Tracks[0].TrackColor;
            Assert.AreEqual(0.55f, c.r, 0.01f);
            Assert.AreEqual(0.55f, c.g, 0.01f);
            Assert.AreEqual(0.55f, c.b, 0.01f);

            Object.DestroyImmediate(ld);
        }
    }
}
