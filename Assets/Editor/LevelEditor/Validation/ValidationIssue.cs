using System;

namespace Validation
{
    public enum ValidationSeverity
    {
        Warning,
        Error,
    }

    /// <summary>
    /// 单条校验结果: 严重度 + 字段路径 + 可读消息。
    /// </summary>
    [Serializable]
    public struct ValidationIssue
    {
        public ValidationSeverity Severity;
        public string Path;
        public string Message;

        public ValidationIssue(ValidationSeverity severity, string path, string message)
        {
            Severity = severity;
            Path = path;
            Message = message;
        }
    }
}
