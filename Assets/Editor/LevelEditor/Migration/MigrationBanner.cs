using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class MigrationBanner
{
    public static VisualElement Build(LevelData ld)
    {
        var root = new VisualElement();
        root.style.flexDirection = FlexDirection.Row;
        root.style.alignItems = Align.Center;
        root.style.backgroundColor = new Color(0.529f, 0.435f, 0.118f); // 黄
        root.style.paddingTop = 6; root.style.paddingBottom = 6;
        root.style.paddingLeft = 8; root.style.paddingRight = 8;
        root.style.marginBottom = 6;
        root.style.borderTopLeftRadius = 3; root.style.borderTopRightRadius = 3;
        root.style.borderBottomLeftRadius = 3; root.style.borderBottomRightRadius = 3;

#pragma warning disable CS0618
        var msg = new Label($"⚠ 此 LevelData 包含 {ld.CheckPoints.Length} 条旧 prefab 路径,建议迁移到 PathData 数据格式。");
#pragma warning restore CS0618
        msg.style.color = new Color(1f, 0.8f, 0.3f);
        msg.style.fontSize = 11;
        msg.style.flexGrow = 1;
        root.Add(msg);

        var migrateBtn = new Button(() =>
        {
            if (EditorPathMigrationTool.MigrateAsset(ld))
            {
                root.RemoveFromHierarchy();
                // 通知 editor 重新 Build
                var ed = UnityEditor.Editor.CreateEditor(ld);
                Selection.activeObject = null;
                Selection.activeObject = ld;
                Object.DestroyImmediate(ed);
            }
        }) { text = "迁移" };
        migrateBtn.style.backgroundColor = new Color(0.706f, 0.706f, 0.412f);
        migrateBtn.style.color = Color.black;
        root.Add(migrateBtn);

        var ignoreBtn = new Button(() => root.RemoveFromHierarchy()) { text = "忽略" };
        ignoreBtn.style.marginLeft = 4;
        root.Add(ignoreBtn);

        return root;
    }
}
