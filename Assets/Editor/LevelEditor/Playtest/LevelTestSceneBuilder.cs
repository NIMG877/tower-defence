using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 确保 Assets/Scenes/LevelTest.unity 存在: 存在则返回路径, 不存在则创建。
/// 场景内容: 空场景 + LM (空 Transform) + MCam (orthographic Camera) + UICam (depth=1) + LevelTestStarter。
/// </summary>
public static class LevelTestSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/LevelTest.unity";

    public static string EnsureScene()
    {
        if (System.IO.File.Exists(ScenePath))
        {
            return ScenePath;
        }

        // 创建目录
        var dir = System.IO.Path.GetDirectoryName(ScenePath);
        if (!System.IO.Directory.Exists(dir))
        {
            System.IO.Directory.CreateDirectory(dir);
        }

        // 新建空场景
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // LM
        var lm = new GameObject("LM");

        // MCam (Main Camera)
        var mcam = new GameObject("MCam");
        var mcamCam = mcam.AddComponent<Camera>();
        mcamCam.orthographic = true;
        mcamCam.orthographicSize = 5f;
        mcamCam.transform.position = new Vector3(0, 0, -10);

        // UICam
        var uicam = new GameObject("UICam");
        var uicamCam = uicam.AddComponent<Camera>();
        uicamCam.orthographic = true;
        uicamCam.orthographicSize = 5f;
        uicamCam.depth = 1;
        uicamCam.transform.position = new Vector3(0, 0, 0);

        // LevelTestStarter
        var starterGo = new GameObject("LevelTestStarter");
        starterGo.AddComponent<LevelTestStarter>();

        // 保存
        EditorSceneManager.SaveScene(scene, ScenePath);

        return ScenePath;
    }
}
