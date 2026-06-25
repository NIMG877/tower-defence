using System;
using UnityEngine.UIElements;

/// <summary>
/// 关卡编辑器 tab / toolbar 复用的小部件。覆盖 MapEditTab 与 PathEditTab 的共同 UI 模式
/// (竖直分隔条、↺ 重置视图按钮、◉/○ 二态切换按钮)。
///
/// 单选组(moveMethod / tool 互斥那种"绿底黑字"模式)不在这里 —— 它的视觉语义不同,
/// 单独实现或抽别的 helper 更合适。
/// </summary>
public static class EditorTabShell
{
    /// <summary>
    /// Toolbar 内 1×16 竖直分隔条。marginLeft/Right=6。
    /// </summary>
    public static VisualElement MakeVerticalSeparator()
    {
        var sep = new VisualElement();
        sep.style.width = 1;
        sep.style.height = 16;
        sep.style.backgroundColor = EditorTheme.Separator;
        sep.style.marginLeft = 6;
        sep.style.marginRight = 6;
        return sep;
    }

    /// <summary>
    /// "↺ 重置视图"按钮:state.Cache 非空时把 View 重置到 Fit,然后 NotifyChanged。
    /// </summary>
    public static Button MakeResetButton(PathEditingState state)
    {
        var resetBtn = new Button(() =>
        {
            if (state.Cache != null)
                state.View = ViewTransform.Fit(state.Cache.ISize, state.Cache.JSize);
            state.NotifyChanged();
        }) { text = "↺ 重置视图" };
        resetBtn.style.fontSize = 11;
        return resetBtn;
    }

    /// <summary>
    /// ◉/○ 二态切换按钮:点击 onClick;视觉(active ◉ + 绿字 / inactive ○ + 灰字)
    /// 随 state.Changed 自动同步。
    ///
    /// onClick 由调用方负责调用 state.NotifyChanged() —— helper 不隐式调用,以便
    /// 调用方能插入额外副作用(如 canvasContainer.MarkDirtyRepaint())。
    ///
    /// height 固定为 20:中英文字符字形度量不同(Portal / 格点吸附 那种带 Latin 的标签),
    /// 不固定会让按钮高度漂移,见 MapEditTab 原 tool 按钮的注释。
    /// </summary>
    public static Button MakeToggleButton(PathEditingState state, string label,
        Func<bool> isActive, Action onClick, string tooltip = null)
    {
        var btn = new Button(onClick) { text = $"○ {label}" };
        btn.style.fontSize = 11;
        btn.style.height = 20;
        if (!string.IsNullOrEmpty(tooltip)) btn.tooltip = tooltip;
        Action sync = () =>
        {
            bool on = isActive();
            btn.text = on ? $"◉ {label}" : $"○ {label}";
            btn.style.color = on ? EditorTheme.AccentGreen : EditorTheme.MutedText;
        };
        state.Changed += sync;
        sync();
        return btn;
    }
}
