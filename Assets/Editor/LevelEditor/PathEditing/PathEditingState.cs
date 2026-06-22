using System;

public sealed class PathEditingState
{
    public int SelectedPathIdx;
    public int SelectedCheckpointIdx = -1;
    public int MoveMethod = 1; // 0=地面 / 1=近地 / 2=飞行
    public ViewTransform View;
    public BlockMapCache Cache;

    public event Action Changed;

    public void NotifyChanged() => Changed?.Invoke();
}