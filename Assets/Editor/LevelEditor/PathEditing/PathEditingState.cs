using System;
using UnityEngine;

public sealed class PathEditingState
{
    public int SelectedPathIdx;
    public int SelectedCheckpointIdx = -1;
    public int MoveMethod = 0; // 0=地面 / 1=近地 / 2=飞行
    public bool Snap = false;  // 是否启用格点吸附(off 时新建/移动 cp 自由坐标)
    public ViewTransform View;
    public BlockMapCache Cache;
    public (int i, int j)? HoverCell; // 当前鼠标在画布上悬停的格子(map tab 用)
    /// <summary>
    /// 当前鼠标的 editor-world 坐标(= (j, i)),由 MapEditManipulator 在 OnMouseMove 写入。
    /// PortalLayer 用它画预览线终点。mouseOver=false 时不读,所以未初始化也安全。
    /// </summary>
    public Vector2 MouseWorld;

    public event Action Changed;

    public void NotifyChanged() => Changed?.Invoke();
}