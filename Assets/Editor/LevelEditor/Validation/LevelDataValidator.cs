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

            return issues;
        }
    }
}
