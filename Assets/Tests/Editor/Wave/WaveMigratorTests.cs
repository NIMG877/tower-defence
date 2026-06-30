using NUnit.Framework;
using UnityEngine;

namespace Wave.Tests
{
    public class WaveMigratorTests
    {
        [Test]
        public void Migrate_skips_level_data_already_at_v2()
        {
            var ld = ScriptableObject.CreateInstance<LevelData>();
            ld.SchemaVersion = 2;
            ld.Waves = new[]
            {
                new LevelActions.Wave { Tracks = new LevelActions.Track[0] }
            };

            Assert.DoesNotThrow(() => WaveMigrator.MigrateLevelData(ld));
            Assert.AreEqual(2, ld.SchemaVersion);
            Assert.AreEqual(0, ld.Waves[0].Tracks.Length);

            Object.DestroyImmediate(ld);
        }

        [Test]
        public void DefaultTrackColor_palette_wraps_at_5()
        {
            Assert.AreEqual(WaveMigrator.DefaultTrackColor(0), WaveMigrator.DefaultTrackColor(5));
            Assert.AreEqual(WaveMigrator.DefaultTrackColor(1), WaveMigrator.DefaultTrackColor(6));
        }
    }
}
