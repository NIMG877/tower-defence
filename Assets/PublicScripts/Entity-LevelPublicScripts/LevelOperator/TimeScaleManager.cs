using UnityEngine;

/// <summary>
/// 战斗时间倍速（Time.timeScale）唯一收口：三档语义请求（暂停/慢速/倍速）按
/// 计数叠加，再按 挂起 > 暂停 > 慢速 > 倍速 > 正常 的优先级合成。
/// 任何系统不得直写 Time.timeScale——UI 按钮、查看慢速、技能生成等都是这里的
/// 一个申请来源；同档多来源互不顶替，调用方负责 true/false 配平（一次申请对应
/// 一次释放）。关卡进入/退出边界调 Reset 全量归零。
/// </summary>
public class TimeScaleManager
{
    public const float PauseScale = 0f;
    public const float SlowScale = 0.1f;
    public const float NormalScale = 1f;
    public const float FastScale = 2f;

    private static TimeScaleManager _instance;
    public static TimeScaleManager Manager
    {
        get
        {
            if (_instance == null)
                _instance = new TimeScaleManager();
            return _instance;
        }
    }
    private TimeScaleManager() { }

    private int _pauseCount;
    private int _slowCount;
    private int _fastCount;
    private int _suspendCount;

    public void SetPause(bool on)
    {
        _pauseCount += on ? 1 : -1;
        Apply();
    }

    public void SetSlow(bool on)
    {
        _slowCount += on ? 1 : -1;
        Apply();
    }

    public void SetFast(bool on)
    {
        _fastCount += on ? 1 : -1;
        Apply();
    }

    /// <summary>面板栈压栈期间战场强制正常速度（各档申请保留，恢复后重新合成）。</summary>
    public void Suspend()
    {
        _suspendCount++;
        Apply();
    }

    public void Resume()
    {
        _suspendCount--;
        Apply();
    }

    /// <summary>关卡进入/退出边界：全部计数归零并恢复正常速度。</summary>
    public void Reset()
    {
        _pauseCount = 0;
        _slowCount = 0;
        _fastCount = 0;
        _suspendCount = 0;
        Apply();
    }

    private void Apply()
    {
        Time.timeScale =
            _suspendCount > 0 ? NormalScale :
            _pauseCount > 0 ? PauseScale :
            _slowCount > 0 ? SlowScale :
            _fastCount > 0 ? FastScale :
            NormalScale;
    }
}
