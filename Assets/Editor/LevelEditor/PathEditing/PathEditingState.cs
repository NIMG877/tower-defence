using System;

public sealed class PathEditingState
{
    public int SelectedPathIdx;
    public int SelectedCheckpointIdx = -1;
    public int MoveMethod = 1; // 0=地面 / 1=近地 / 2=飞行
    public ViewTransform View;
    public BlockMapCache _cache; // 内部:设置时自动 Fit
    public BlockMapCache Cache
    {
        get => _cache;
        set
        {
            _cache = value;
            if (value != null) View = ViewTransform.Fit(value.ISize, value.JSize);
        }
    }

    public event Action Changed;

    public void NotifyChanged() => Changed?.Invoke();
}