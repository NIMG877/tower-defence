using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 关卡编辑器地图画布的共用外壳。Map tab / Path tab 共享同一套 chrome、status/hint label
/// 与 repaint 触发;区别只在额外绘制(>DrawBlocks 的部分)与附加 overlay(manipulator /
/// PortalLayer / CheckpointLayer),由 call site 自己装。
///
/// 产出 (canvas, status):
///   - canvas:600×400 + EditorTheme chrome + overflow hidden + position relative
///   - status:左下绝对定位 Label,fontSize 10,HintText。tab 各自赋予语义:
///       Path tab:把 status.name 改成 "cursor-readout",EditorPathManipulator 通过
///                canvas.Q<Label>("cursor-readout") 拿
///       Map  tab:直接传给 MapEditManipulator 构造函数,manipulator 写它
///
/// 修复的潜在 bug:原 MapCanvasView.Build 订阅了 state.Changed 与 Undo.undoRedoPerformed
/// 但没注册 DetachFromPanelEvent 清理,tab 切换会泄漏订阅。这里统一清理。
/// </summary>
public static class EditorCanvasShell
{
    public static (VisualElement canvas, Label status) Build(
        PathEditingState state,
        string statusText,
        string hintText,
        Action<MeshGenerationContext> drawExtra = null,
        Action onDetach = null)
    {
        var canvas = new VisualElement();
        canvas.style.width = ViewTransform.CanvasWidth;
        canvas.style.height = ViewTransform.CanvasHeight;
        canvas.style.backgroundColor = EditorTheme.CanvasBg;
        canvas.style.borderTopLeftRadius = 3;
        canvas.style.borderTopRightRadius = 3;
        canvas.style.borderBottomLeftRadius = 3;
        canvas.style.borderBottomRightRadius = 3;
        canvas.style.borderLeftWidth = 1;
        canvas.style.borderRightWidth = 1;
        canvas.style.borderTopWidth = 1;
        canvas.style.borderBottomWidth = 1;
        canvas.style.borderLeftColor = EditorTheme.Border;
        canvas.style.borderRightColor = EditorTheme.Border;
        canvas.style.borderTopColor = EditorTheme.Border;
        canvas.style.borderBottomColor = EditorTheme.Border;
        canvas.style.overflow = Overflow.Hidden;
        canvas.style.position = Position.Relative;

        var status = new Label(statusText);
        status.style.position = Position.Absolute;
        status.style.bottom = 4;
        status.style.left = 8;
        status.style.fontSize = 10;
        status.style.color = EditorTheme.HintText;
        status.style.unityFontStyleAndWeight = FontStyle.Normal;
        canvas.Add(status);

        var hint = new Label(hintText);
        hint.style.position = Position.Absolute;
        hint.style.bottom = 4;
        hint.style.right = 8;
        hint.style.fontSize = 10;
        hint.style.color = EditorTheme.HintText;
        canvas.Add(hint);

        canvas.generateVisualContent = ctx =>
        {
            if (state.Cache == null) return;
            MapCanvasView.DrawBlocks(ctx, state);
            drawExtra?.Invoke(ctx);
        };

        // Repaint triggers —— state.Changed 是 System.Action,Undo.undoRedoPerformed 是
        // UnityEditor.Undo.UndoRedoCallback,delegate 类型不同,不能共用一个 Action 实例。
        // 用方法组转换(canvas.MarkDirtyRepaint 是无参 void 方法,匹配两者签名)各得一个
        // delegate 实例,各自 +=/-= 才能匹配移除。
        Action stateRepaint = canvas.MarkDirtyRepaint;
        Undo.UndoRedoCallback undoRepaint = canvas.MarkDirtyRepaint;
        state.Changed += stateRepaint;
        Undo.undoRedoPerformed += undoRepaint;

        // Cleanup on detach —— 修原 MapCanvasView 漏掉的 DetachFromPanelEvent 订阅,
        // 防止 tab 切换后 state.Changed / Undo.undoRedoPerformed 列表里残留死引用。
        canvas.RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            state.Changed -= stateRepaint;
            Undo.undoRedoPerformed -= undoRepaint;
            onDetach?.Invoke();
        });

        return (canvas, status);
    }
}
