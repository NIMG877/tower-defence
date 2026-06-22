using System.Collections.Generic;

namespace Validation
{
    /// <summary>
    /// 纯函数校验: 给定 LevelData, 返回发现的 issue 列表。
    /// 不依赖 UI, 不修改传入数据。
    /// </summary>
    public static class LevelDataValidator
    {
        public static List<ValidationIssue> Validate(LevelData data)
        {
            var issues = new List<ValidationIssue>();
            if (data == null) return issues;

            // 规则 1: Waves 为 null 或空 -> Error
            if (data.Waves == null || data.Waves.Length == 0)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "Waves", "Waves 不能为空"));
            }

            // 规则 2: 任一 Wave.Actions 为 null 或空 -> Warning
            if (data.Waves != null)
            {
                for (int i = 0; i < data.Waves.Length; i++)
                {
                    var wave = data.Waves[i];
                    if (wave.Actions == null || wave.Actions.Length == 0)
                    {
                        issues.Add(new ValidationIssue(
                            ValidationSeverity.Warning,
                            $"Waves[{i}].Actions",
                            $"Wave {i} 的 Actions 为空"));
                    }
                }
            }

            // 规则 3: WaveEntityPrefabIDs 为 null 或空 -> Error
            if (data.WaveEntityPrefabIDs == null || data.WaveEntityPrefabIDs.Length == 0)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "WaveEntityPrefabIDs",
                    "WaveEntityPrefabIDs 不能为空"));
            }

            // 规则 4: EntityPrefabSerial 越界 -> Error
            if (data.Waves != null && data.WaveEntityPrefabIDs != null)
            {
                int prefabCount = data.WaveEntityPrefabIDs.Length;
                for (int w = 0; w < data.Waves.Length; w++)
                {
                    var actions = data.Waves[w].Actions;
                    if (actions == null) continue;
                    for (int a = 0; a < actions.Length; a++)
                    {
                        var act = actions[a];
                        // 仅对需要 EntityPrefabSerial 的 CommandType 校验 (0, 1)
                        if (act.CommandType == 0 || act.CommandType == 1)
                        {
                            if (act.EntityPrefabSerial < 0 || act.EntityPrefabSerial >= prefabCount)
                            {
                                issues.Add(new ValidationIssue(
                                    ValidationSeverity.Error,
                                    $"Waves[{w}].Actions[{a}].EntityPrefabSerial",
                                    $"EntityPrefabSerial={act.EntityPrefabSerial} 越界 (有效范围 0..{prefabCount - 1})"));
                            }
                        }
                    }
                }
            }

            return issues;
        }
    }
}
