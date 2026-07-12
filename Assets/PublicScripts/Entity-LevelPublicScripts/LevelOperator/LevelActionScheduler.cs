using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Wave → 排序后 ScheduledAction 列表的纯函数。从 LevelActionManager 抽出,便于单测。
/// 不依赖 Unity 时间 / 协程 / 单例,可纯逻辑测试。
/// </summary>
public static class LevelActionScheduler
{
    public struct ScheduledAction
    {
        public int TrackIndex;
        public int ActionIndex;
        public LevelActions.Action Action;
    }

    public static List<ScheduledAction> CollectAndSortActions(LevelActions.Wave wave)
    {
        var list = new List<ScheduledAction>();
        if (wave.Tracks == null) return list;

        for (int t = 0; t < wave.Tracks.Length; t++)
        {
            if (wave.Tracks[t].Locked) continue;  // 未激活 Track:其 Action 不进入调度列表
            var actions = wave.Tracks[t].Actions;
            if (actions == null) continue;
            for (int a = 0; a < actions.Length; a++)
            {
                list.Add(new ScheduledAction
                {
                    TrackIndex = t,
                    ActionIndex = a,
                    Action = actions[a],
                });
            }
        }

        list.Sort((x, y) =>
        {
            int byTime = x.Action.TriggerTime.CompareTo(y.Action.TriggerTime);
            if (byTime != 0) return byTime;
            int byTrack = x.TrackIndex.CompareTo(y.TrackIndex);
            if (byTrack != 0) return byTrack;
            return x.ActionIndex.CompareTo(y.ActionIndex);
        });

        return list;
    }
}