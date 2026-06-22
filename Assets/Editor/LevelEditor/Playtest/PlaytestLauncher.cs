using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Playtest 流程: 校验当前 LevelData → 保存当前场景 → 切到测试场景 → 设 LevelDataToPlay → EnterPlay。
/// </summary>
public static class PlaytestLauncher
{
    public static void Playtest(LevelData data)
    {
        if (data == null)
        {
            EditorUtility.DisplayDialog("Playtest", "未选中 LevelData 资产。", "OK");
            return;
        }

        // 校验
        var issues = LevelDataValidator.Validate(data);
        var errors = issues.FindAll(i => i.Severity == Validation.ValidationSeverity.Error);
        if (errors.Count > 0)
        {
            if (!EditorUtility.DisplayDialog(
                "Playtest",
                $"当前 LevelData 有 {errors.Count} 个 Error:\n\n{string.Join("\n", errors.ConvertAll(e => "· " + e.Message))}\n\n是否仍要 Playtest?",
                "继续", "取消"))
            {
                return;
            }
        }

        // 确保测试场景
        var scenePath = LevelTestSceneBuilder.EnsureScene();

        // 保存当前场景 (询问)
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        // 打开测试场景
        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        // 设置 LevelDataToPlay
        var starter = Object.FindObjectOfType<LevelTestStarter>();
        if (starter == null)
        {
            Debug.LogError("[PlaytestLauncher] LevelTest.unity missing LevelTestStarter. Re-create scene?");
            return;
        }
        starter.LevelDataToPlay = data;

        // Enter Play
        EditorApplication.EnterPlaymode();
    }
}