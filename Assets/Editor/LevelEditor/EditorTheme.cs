using UnityEngine;

/// <summary>
/// 关卡编辑器 chrome 颜色常量。
/// 集中放置 tab/canvas/toolbar/panel 的边框、背景、强调色,避免字面量散落多处。
///
/// 域语义颜色(地块类型、portal 箭头、path 描边、cp 选中态)不进这里 —— 它们有独立含义,
/// 不应该跟着 chrome 主题一起改。详见 <c>MapCanvasView.BlockTypeColor</c> /
/// <c>PortalLayer.LineColor</c> / <c>CheckpointLayer</c> 的内联颜色。
/// </summary>
public static class EditorTheme
{
    /// <summary>强调绿:active 按钮文字 / 选中态边框 / Tab 主标题。</summary>
    public static readonly Color AccentGreen = new Color(0.306f, 0.788f, 0.627f);

    /// <summary>蓝色小标题(▸ 子段标题、字段名 label)。</summary>
    public static readonly Color SubHeader = new Color(0.611f, 0.863f, 0.996f);

    /// <summary>普通灰字(inactive 按钮 / 段内 label)。</summary>
    public static readonly Color MutedText = new Color(0.706f, 0.706f, 0.706f);

    /// <summary>更暗的灰字(canvas 底部 hint / 状态文字)。</summary>
    public static readonly Color HintText = new Color(0.55f, 0.55f, 0.55f);

    /// <summary>Tab 根容器背景(深灰偏蓝)。</summary>
    public static readonly Color TabBg = new Color(0.118f, 0.118f, 0.133f);

    /// <summary>Canvas 背景(更深)。</summary>
    public static readonly Color CanvasBg = new Color(0.078f, 0.078f, 0.094f);

    /// <summary>Toolbar 背景。</summary>
    public static readonly Color ToolbarBg = new Color(0.157f, 0.157f, 0.157f);

    /// <summary>侧边小面板背景(brush panel / cell panel / list view / detail view)。</summary>
    public static readonly Color PanelBg = new Color(0.118f, 0.118f, 0.118f);

    /// <summary>1px 通用灰边框(canvas / panel)。</summary>
    public static readonly Color Border = new Color(0.235f, 0.235f, 0.275f);

    /// <summary>Toolbar 内竖直分隔条。</summary>
    public static readonly Color Separator = new Color(0.314f, 0.314f, 0.314f);

    /// <summary>危险操作按钮(删除 Path 等)。</summary>
    public static readonly Color Danger = new Color(0.471f, 0.235f, 0.235f);
}
