using System;

public sealed class PathEditingState
{
    public int SelectedPathIdx;
    public int SelectedCheckpointIdx = -1;
    public int MoveMethod = 0; // 0=地面 / 1=近地 / 2=飞行
    public bool Snap = false;  // 是否启用格点吸附(off 时新建/移动 cp 自由坐标)
    public ViewTransform View;
    public BlockMapCache Cache;

    public event Action Changed;

    public void NotifyChanged() => Changed?.Invoke();
}