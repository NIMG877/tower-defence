using NUnit.Framework;
using UnityEngine;

namespace Wave.Tests
{
    public class WaveSchedulerTests
    {
        static LevelActions.Action MakeAction(float triggerTime, int trackIdx = 0, int actionIdx = 0, int commandType = 0)
        {
            return new LevelActions.Action
            {
                CommandType = commandType,
                TriggerTime = triggerTime,
                GapsFromLastRepeat = new float[0],
            };
        }

        [Test]
        public void Collect_returns_empty_when_wave_has_null_tracks()
        {
            var wave = new LevelActions.Wave { Tracks = null };
            var result = LevelActionScheduler.CollectAndSortActions(wave);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Collect_sorts_by_trigger_time_ascending()
        {
            var wave = new LevelActions.Wave
            {
                Tracks = new[]
                {
                    new LevelActions.Track
                    {
                        Actions = new[] { MakeAction(5f, 0, 0), MakeAction(2f, 0, 1), MakeAction(8f, 0, 2) }
                    }
                }
            };

            var result = LevelActionScheduler.CollectAndSortActions(wave);
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(2f, result[0].Action.TriggerTime, 0.0001f);
            Assert.AreEqual(5f, result[1].Action.TriggerTime, 0.0001f);
            Assert.AreEqual(8f, result[2].Action.TriggerTime, 0.0001f);
        }

        [Test]
        public void Collect_flattens_across_multiple_tracks()
        {
            var wave = new LevelActions.Wave
            {
                Tracks = new[]
                {
                    new LevelActions.Track { Actions = new[] { MakeAction(3f, 0, 0), MakeAction(7f, 0, 1) } },
                    new LevelActions.Track { Actions = new[] { MakeAction(1f, 1, 0), MakeAction(5f, 1, 1) } },
                    new LevelActions.Track { Actions = new[] { MakeAction(9f, 2, 0) } },
                }
            };

            var result = LevelActionScheduler.CollectAndSortActions(wave);
            Assert.AreEqual(5, result.Count);
            Assert.AreEqual(1f, result[0].Action.TriggerTime, 0.0001f);
            Assert.AreEqual(3f, result[1].Action.TriggerTime, 0.0001f);
            Assert.AreEqual(5f, result[2].Action.TriggerTime, 0.0001f);
            Assert.AreEqual(7f, result[3].Action.TriggerTime, 0.0001f);
            Assert.AreEqual(9f, result[4].Action.TriggerTime, 0.0001f);
        }

        [Test]
        public void Collect_tie_breaks_by_track_index_then_action_index()
        {
            var wave = new LevelActions.Wave
            {
                Tracks = new[]
                {
                    new LevelActions.Track { Actions = new[] { MakeAction(5f, 0, 0) } },
                    new LevelActions.Track { Actions = new[] { MakeAction(5f, 1, 0) } },
                    new LevelActions.Track { Actions = new[] { MakeAction(5f, 2, 0) } },
                }
            };

            var result = LevelActionScheduler.CollectAndSortActions(wave);
            Assert.AreEqual(0, result[0].TrackIndex, "tie-break: track 0 first");
            Assert.AreEqual(1, result[1].TrackIndex);
            Assert.AreEqual(2, result[2].TrackIndex);
        }

        [Test]
        public void Collect_tie_breaks_by_action_index_within_same_track()
        {
            var wave = new LevelActions.Wave
            {
                Tracks = new[]
                {
                    new LevelActions.Track { Actions = new[] { MakeAction(5f, 0, 0), MakeAction(5f, 0, 1) } }
                }
            };

            var result = LevelActionScheduler.CollectAndSortActions(wave);
            Assert.AreEqual(0, result[0].ActionIndex);
            Assert.AreEqual(1, result[1].ActionIndex);
        }

        [Test]
        public void Collect_skips_tracks_with_null_actions()
        {
            var wave = new LevelActions.Wave
            {
                Tracks = new[]
                {
                    new LevelActions.Track { Actions = null },
                    new LevelActions.Track { Actions = new[] { MakeAction(1f, 1, 0) } },
                }
            };

            var result = LevelActionScheduler.CollectAndSortActions(wave);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1f, result[0].Action.TriggerTime, 0.0001f);
        }
    }
}