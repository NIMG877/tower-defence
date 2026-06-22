using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 校验状态条: 显示当前 LevelData 的 issue 数量, 颜色按 Error/Warning/OK 区分。
/// </summary>
public static class ValidationBar
{
    public static VisualElement Build(SerializedObject so, out Label statusLabel)
    {
        var bar = new VisualElement();
        bar.style.flexDirection = FlexDirection.Row;
        bar.style.alignItems = Align.Center;

        statusLabel = new Label("...");
        statusLabel.style.fontSize = 11;
        bar.Add(statusLabel);

        Refresh(so, statusLabel);

        // 监听: 任何字段改动后延迟一帧刷新
        so.Update();
        bar.TrackSerializedObjectValue(so, _ =>
        {
            // 延迟 1 帧
            bar.schedule.Execute(() => Refresh(so, statusLabel)).StartingIn(50);
        });

        return bar;
    }

    static void Refresh(SerializedObject so, Label statusLabel)
    {
        var data = so.targetObject as LevelData;
        if (data == null)
        {
            statusLabel.text = "(无目标)";
            return;
        }
        var issues = LevelDataValidator.Validate(data);
        int errors = 0, warnings = 0;
        foreach (var i in issues)
        {
            if (i.Severity == Validation.ValidationSeverity.Error) errors++;
            else warnings++;
        }

        if (errors == 0 && warnings == 0)
        {
            statusLabel.text = "✓ 校验通过";
            statusLabel.style.color = new Color(0.3f, 0.8f, 0.6f);
        }
        else if (errors == 0)
        {
            statusLabel.text = $"⚠ {warnings} 个 Warning";
            statusLabel.style.color = new Color(0.85f, 0.7f, 0.3f);
        }
        else
        {
            statusLabel.text = $"✗ {errors} 个 Error, {warnings} 个 Warning";
            statusLabel.style.color = new Color(0.95f, 0.4f, 0.4f);
        }
    }
}
