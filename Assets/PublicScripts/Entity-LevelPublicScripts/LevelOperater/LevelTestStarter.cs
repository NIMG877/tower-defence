using UnityEngine;

/// <summary>
/// 挂在测试场景 LevelTest.unity 上。PlaytestLauncher 会在 EnterPlay 前设置 LevelDataToPlay。
/// 启动时调用 LevelResourceSharing 的 LevelInitialize/LevelStart。
/// </summary>
public class LevelTestStarter : MonoBehaviour
{
    public LevelData LevelDataToPlay;

    void Awake()
    {
        if (LevelDataToPlay == null)
        {
            Debug.LogError("[LevelTestStarter] LevelDataToPlay is null; nothing to play.");
            return;
        }
        LevelResourceSharing.LD = LevelDataToPlay;
        LevelResourceSharing.LevelInitialize();
        LevelResourceSharing.LevelStart();
    }

    void OnDisable()
    {
        if (LevelResourceSharing.LD != null)
        {
            LevelResourceSharing.LevelEnd();
        }
    }
}