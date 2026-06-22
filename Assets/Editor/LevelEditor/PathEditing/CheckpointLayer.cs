using UnityEngine;
using UnityEngine.UIElements;

public static class CheckpointLayer
{
    public static VisualElement Build(SerializedObject so, PathEditingState state, VisualElement canvas)
    {
        var layer = new VisualElement();
        layer.style.position = Position.Absolute;
        layer.style.left = 0; layer.style.top = 0;
        layer.style.right = 0; layer.style.bottom = 0;
        layer.pickingMode = PickingMode.Ignore; // 让父 canvas 接收事件

        state.Changed += () => Rebuild(layer, so, state, canvas);
        Rebuild(layer, so, state, canvas);
        return layer;
    }

    static void Rebuild(VisualElement layer, SerializedObject so, PathEditingState state, VisualElement canvas)
    {
        layer.Clear();
        if (state.Cache == null || state.SelectedPathIdx < 0) return;

        var pathsProp = so.FindProperty("Paths");
        if (pathsProp == null || state.SelectedPathIdx >= pathsProp.arraySize) return;
        var pathProp = pathsProp.GetArrayElementAtIndex(state.SelectedPathIdx);
        var cpsProp = pathProp.FindPropertyRelative("CheckPoints");
        if (cpsProp == null) return;

        var cache = state.Cache;
        float diameter = cache.EntityR * 2f * (ViewTransform.CanvasWidth / cache.JSize) * state.View.Zoom;
        diameter = Mathf.Clamp(diameter, 12f, 32f); // 视觉上限下限

        for (int k = 0; k < cpsProp.arraySize; k++)
        {
            var cpProp = cpsProp.GetArrayElementAtIndex(k);
            var pos = cpProp.vector2Value;

            var screen = state.View.WorldToScreen(pos, cache.ISize, cache.JSize);

            var dot = new VisualElement();
            dot.style.position = Position.Absolute;
            dot.style.width = diameter;
            dot.style.height = diameter;
            dot.style.left = screen.x - diameter / 2f;
            dot.style.top = screen.y - diameter / 2f;
            dot.style.borderTopLeftRadius = diameter / 2f;
            dot.style.borderTopRightRadius = diameter / 2f;
            dot.style.borderBottomLeftRadius = diameter / 2f;
            dot.style.borderBottomRightRadius = diameter / 2f;
            dot.style.borderLeftWidth = 2;
            dot.style.borderRightWidth = 2;
            dot.style.borderTopWidth = 2;
            dot.style.borderBottomWidth = 2;

            bool selected = (k == state.SelectedCheckpointIdx);
            dot.style.backgroundColor = selected
                ? new Color(1f, 0.784f, 0.314f, 0.5f)
                : new Color(0.306f, 0.788f, 0.627f, 0.4f);
            dot.style.borderLeftColor = selected ? new Color(1f, 0.784f, 0.314f) : new Color(0.306f, 0.788f, 0.627f);
            dot.style.borderRightColor = dot.style.borderLeftColor;
            dot.style.borderTopColor = dot.style.borderLeftColor;
            dot.style.borderBottomColor = dot.style.borderLeftColor;
            if (selected)
            {
                dot.style.boxShadow = new Shadow
                {
                    offset = Vector2.zero,
                    blurRadius = 12,
                    color = new Color(1f, 0.784f, 0.314f, 0.7f)
                };
            }

            dot.style.alignItems = Align.Center;
            dot.style.justifyContent = Justify.Center;

            var label = new Label(k.ToString());
            label.style.color = Color.white;
            label.style.fontSize = 10;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.pickingMode = PickingMode.Ignore;
            dot.Add(label);

            dot.userData = k;
            dot.pickingMode = PickingMode.Position;

            layer.Add(dot);
        }
    }
}
