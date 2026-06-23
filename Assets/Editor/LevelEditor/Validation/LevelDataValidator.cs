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

            // 规则 3: (已删除 WaveEntityPrefabIDs 字段 — action 直接持有 EntityID)

            // 规则 4: EntityPrefabID 空 -> Error
            if (data.Waves != null)
            {
                for (int w = 0; w < data.Waves.Length; w++)
                {
                    var actions = data.Waves[w].Actions;
                    if (actions == null) continue;
                    for (int a = 0; a < actions.Length; a++)
                    {
                        var act = actions[a];
                        // 仅对需要 EntityPrefabID 的 CommandType 校验 (0, 1)
                        if (act.CommandType == 0 || act.CommandType == 1)
                        {
                            if (act.EntityPrefabID.IsNull)
                            {
                                issues.Add(new ValidationIssue(
                                    ValidationSeverity.Error,
                                    $"Waves[{w}].Actions[{a}].EntityPrefabID",
                                    $"EntityPrefabID 为空 (未指定 ID_C)"));
                            }
                        }
                    }
                }
            }

            // 规则 6: LevelHp <= 0 -> Error
            if (data.LevelHp <= 0)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "LevelHp",
                    $"LevelHp 必须 > 0, 当前 {data.LevelHp}"));
            }

            // 规则 7: MaxCost < Cost0 -> Error
            if (data.MaxCost < data.Cost0)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "MaxCost",
                    $"MaxCost ({data.MaxCost}) 不能小于 Cost0 ({data.Cost0})"));
            }

            // 规则 8: MapPrefab == null -> Error
            if (data.MapPrefab == null)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "MapPrefab",
                    "MapPrefab 不能为空"));
            }

            // 规则 9: CutToLevelTexture == null -> Warning
            if (data.CutToLevelTexture == null)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "CutToLevelTexture",
                    "CutToLevelTexture 未指定 (可选)"));
            }

            // 规则 10: CameraSize <= 0 -> Error
            if (data.CameraSize <= 0f)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "CameraSize",
                    $"CameraSize 必须 > 0, 当前 {data.CameraSize}"));
            }

            // === Paths 校验 ===
            if (data.Paths == null || data.Paths.Length == 0)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Path = "Paths",
                    Message = "没有任何路径数据"
                });
            }
            else
            {
                for (int p = 0; p < data.Paths.Length; p++)
                {
                    var path = data.Paths[p];
                    if (path.CheckPoints == null || path.CheckPoints.Length < 2)
                    {
                        issues.Add(new ValidationIssue
                        {
                            Severity = ValidationSeverity.Error,
                            Path = $"Paths[{p}]",
                            Message = $"Path {p} 至少需要 2 个 checkpoint,当前 {path.CheckPoints?.Length ?? 0} 个"
                        });
                        continue;
                    }

                    if (path.WaitTimes == null || path.CheckPoints.Length != path.WaitTimes.Length)
                    {
                        issues.Add(new ValidationIssue
                        {
                            Severity = ValidationSeverity.Error,
                            Path = $"Paths[{p}].WaitTimes",
                            Message = $"Path {p} WaitTimes 长度必须等于 CheckPoints 长度"
                        });
                    }

                    for (int k = 0; k < path.WaitTimes.Length; k++)
                    {
                        if (path.WaitTimes[k] < 0)
                        {
                            issues.Add(new ValidationIssue
                            {
                                Severity = ValidationSeverity.Warning,
                                Path = $"Paths[{p}].WaitTimes[{k}]",
                                Message = $"Path {p} WaitTime[{k}] = {path.WaitTimes[k]} 为负"
                            });
                        }
                    }
                }
            }

            return issues;
        }
    }
}
