using System.Collections.Generic;
using AbilitySystem;
using UnityEngine;

/// <summary>
/// ability-server 任务接口（GET /jobs/{jobId}）的响应 DTO。运行时组件
/// （GenerateSkill）与 Editor 探针（GeneratedSkillProbe）共用；字段名与
/// 服务端 main.py 的返回形状一一对应，经 Newtonsoft 反序列化。
/// issues 的 Console 呈现助手也在这里（tag 参数区分调用方前缀），两处调用方共用一份。
/// </summary>
public class AgentJobStatus
{
    public bool done;
    public List<PhaseEntry> phases;
    public string error;
    public GenerateResponse response;

    /// <summary>Agent 阶段流水（词表 plan/act/review/submit/done/degraded/handshake）。</summary>
    public class PhaseEntry
    {
        public string phase;
        public string detail;
    }

    /// <summary>/generate-ability 的响应体：ok 时 ability 为已通过服务端校验的 DTO。</summary>
    public class GenerateResponse
    {
        public string status;
        public AbilityConfigDto ability;
        public Report report;
    }

    public class Report
    {
        public int attempts;
        public bool cached;
        public List<Issue> issues;
    }

    public class Issue
    {
        public string severity;
        public string path;
        public string message;
    }

    /// <summary>服务端 report.issues（撞名 warning、钳制、设计层失败原因等）按 severity 打 Console。</summary>
    public static void LogServerIssues(GenerateResponse response, string tag)
    {
        List<Issue> issues = response?.report?.issues;
        if (issues == null) return;
        foreach (Issue issue in issues)
        {
            if (issue.severity == "error")
                Debug.LogError($"{tag}[服务端 error] {issue.path}: {issue.message}");
            else
                Debug.LogWarning($"{tag}[服务端 warning] {issue.path}: {issue.message}");
        }
    }
}
