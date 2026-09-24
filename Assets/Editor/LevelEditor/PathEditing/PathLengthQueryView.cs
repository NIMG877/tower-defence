using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 路径长度查询模块(路径 Tab 右侧栏,检查点列表与详情之间)。
/// 选起点/终点检查点,对区间内每对相邻检查点跑一次 A*(与 MapCanvasView.DrawPaths
/// 同一调用:cache.Blocks + cache.EntityR + state.MoveMethod),沿折线逐段累加长度
/// —— 结果与画布绿线完全同源,含绕障;某段不可达时标红提示,不给直线距离误导值。
/// 可选填移动速度(格/秒),>0 时折算移动耗时(长度/速度 + 途经点 WaitTime,口径=到点先等再走)。
/// </summary>
public static class PathLengthQueryView
{
    public static VisualElement Build(SerializedObject so, PathEditingState state)
    {
        var root = new VisualElement();
        root.style.backgroundColor = new Color(0.118f, 0.118f, 0.133f);
        root.style.borderTopLeftRadius = 3; root.style.borderTopRightRadius = 3;
        root.style.borderBottomLeftRadius = 3; root.style.borderBottomRightRadius = 3;
        root.style.borderLeftWidth = 1; root.style.borderRightWidth = 1;
        root.style.borderTopWidth = 1; root.style.borderBottomWidth = 1;
        root.style.borderLeftColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.borderRightColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.borderTopColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.borderBottomColor = new Color(0.235f, 0.235f, 0.275f);
        root.style.paddingTop = 4; root.style.paddingBottom = 4;
        root.style.paddingLeft = 6; root.style.paddingRight = 6;

        var header = new Label("▸ 长度查询");
        header.style.color = new Color(0.611f, 0.863f, 0.996f);
        header.style.fontSize = 11;
        header.style.marginBottom = 4;
        header.tooltip = "长度按工具栏当前 moveMethod(地面/近地/飞行)沿 A* 折线逐段累加,与画布绿线一致";
        root.Add(header);

        int startIdx = 0, endIdx = 0;
        bool initialized = false;
        var choices = new List<string>();
        var startPopup = new PopupField<string>(new List<string> { "-" }, 0);
        var endPopup = new PopupField<string>(new List<string> { "-" }, 0);
        startPopup.tooltip = "起点检查点";
        endPopup.tooltip = "终点检查点";

        var selRow = new VisualElement();
        selRow.style.flexDirection = FlexDirection.Row;
        selRow.style.alignItems = Align.Center;
        selRow.style.marginBottom = 4;
        selRow.Add(MakeSmallLabel("起"));
        startPopup.style.flexGrow = 1;
        startPopup.style.flexShrink = 1;
        startPopup.style.minWidth = 0;
        selRow.Add(startPopup);
        selRow.Add(MakeSmallLabel("→"));
        endPopup.style.flexGrow = 1;
        endPopup.style.flexShrink = 1;
        endPopup.style.minWidth = 0;
        selRow.Add(endPopup);
        selRow.Add(MakeSmallLabel("止"));
        root.Add(selRow);

        // 长度行(只读 TextField 才能选中复制;Label 不支持选中文本)
        var lengthField = new TextField("长度") { isReadOnly = true };
        lengthField.labelElement.style.minWidth = 0; // 默认主题 label min-width:120px 会把输入框挤远
        lengthField.tooltip = "选中后 Ctrl+A 全选、Ctrl+C 复制";
        lengthField.style.marginBottom = 4;
        root.Add(lengthField);

        // 底行:速度输入 + 耗时结果
        var speedField = new FloatField("速度") { value = 0f };
        speedField.labelElement.style.minWidth = 0;
        speedField.tooltip = "移动速度(格/秒)。>0 时折算移动耗时:长度/速度 + 途经点 WaitTime";
        speedField.style.flexGrow = 1;
        speedField.style.flexShrink = 1;
        speedField.style.minWidth = 0;

        var timeField = new TextField("耗时") { isReadOnly = true };
        timeField.labelElement.style.minWidth = 0;
        timeField.tooltip = "长度/速度 + 途经点 WaitTime(起点等计入,终点等不计,与运行时'到点先等再走'一致);只读可复制;速度 ≤0 时不计";
        timeField.style.flexGrow = 1;
        timeField.style.flexShrink = 1;
        timeField.style.minWidth = 0;
        timeField.style.marginLeft = 4;

        var bottomRow = new VisualElement();
        bottomRow.style.flexDirection = FlexDirection.Row;
        bottomRow.style.alignItems = Align.Center;
        bottomRow.Add(speedField);
        bottomRow.Add(timeField);
        root.Add(bottomRow);

        SerializedProperty PathEl()
        {
            var pathsArr = so.FindProperty("Paths");
            if (pathsArr == null || state.SelectedPathIdx < 0 || state.SelectedPathIdx >= pathsArr.arraySize)
                return null;
            return pathsArr.GetArrayElementAtIndex(state.SelectedPathIdx);
        }

        void RebuildPopups()
        {
            choices.Clear();
            var pathEl = PathEl();
            var cpsProp = pathEl != null ? pathEl.FindPropertyRelative("CheckPoints") : null;
            int count = cpsProp != null ? cpsProp.arraySize : 0;
            for (int k = 0; k < count; k++)
            {
                var pos = cpsProp.GetArrayElementAtIndex(k).vector2Value;
                choices.Add($"#{k} ({pos.x:F2}, {pos.y:0.##})");
            }
            if (choices.Count == 0) choices.Add("-");
            // PopupField 的下拉菜单渲染的是它持有的 choices 列表,SetValueWithoutNotify
            // 只改显示值 —— 每次重建必须把列表重新赋给 popup,菜单项才会跟着变
            startPopup.choices = choices;
            endPopup.choices = choices;
            if (!initialized && count > 1)
            {
                startIdx = 0;
                endIdx = count - 1;
                initialized = true;
            }
            startIdx = Mathf.Clamp(startIdx, 0, choices.Count - 1);
            endIdx = Mathf.Clamp(endIdx, 0, choices.Count - 1);
            startPopup.SetValueWithoutNotify(choices[startIdx]);
            endPopup.SetValueWithoutNotify(choices[endIdx]);
        }

        var normalText = new Color(0.706f, 0.706f, 0.706f);
        var errorText = new Color(0.95f, 0.4f, 0.4f);

        // 结果写进只读框;正常灰 / 异常红(主题若覆写输入框文字色则退化为普通色,文案仍在)
        void SetResult(string lengthText, Color? color)
        {
            lengthField.SetValueWithoutNotify(lengthText);
            lengthField.style.color = color ?? normalText;
        }

        void Recompute()
        {
            var pathEl = PathEl();
            var cpsProp = pathEl != null ? pathEl.FindPropertyRelative("CheckPoints") : null;
            var wtsProp = pathEl != null ? pathEl.FindPropertyRelative("WaitTimes") : null;
            int count = cpsProp != null ? cpsProp.arraySize : 0;
            if (state.Cache == null || state.Cache.Blocks == null || count < 2 || startIdx == endIdx)
            {
                SetResult("-", null);
                timeField.SetValueWithoutNotify("-");
                return;
            }

            var cache = state.Cache;
            int lo = Mathf.Min(startIdx, endIdx);
            int hi = Mathf.Max(startIdx, endIdx);
            float total = 0f;
            for (int k = lo; k < hi; k++)
            {
                var a = cpsProp.GetArrayElementAtIndex(k).vector2Value;
                var b = cpsProp.GetArrayElementAtIndex(k + 1).vector2Value;
                var path = MapPathFinder.AStar(cache.Blocks, cache.ISize, cache.JSize,
                    a, b, cache.EntityR, state.MoveMethod);
                if (path == null)
                {
                    SetResult($"段 #{k}→#{k + 1} 不可达", errorText);
                    timeField.SetValueWithoutNotify("-");
                    return;
                }
                // 传送门入口点(whetherToEnterPortal=true)→出口运行时是瞬移
                // (NormalMove 直写 position,A* 对 portal 边也 0 代价),这段距离不计
                for (int m = 1; m < path.Length; m++)
                {
                    if (path[m - 1].whetherToEnterPortal) continue;
                    total += Vector2.Distance(path[m - 1].targetPosition, path[m].targetPosition);
                }
            }

            // 运行时口径(MoveBase):到点先等 WaitTime 再走下一段。
            // 起点 lo 的等待算,终点 hi 的等待不算 —— 到达即查询结束。
            // 负值等待运行时按"≤0 立即走"处理,这里同样钳 0;WaitTimes 短于 CheckPoints 时缺省 0。
            float totalWait = 0f;
            for (int k = lo; k < hi; k++)
            {
                if (wtsProp == null || k >= wtsProp.arraySize) break;
                totalWait += Mathf.Max(0f, wtsProp.GetArrayElementAtIndex(k).floatValue);
            }

            SetResult(total.ToString("F6"), null);
            float speed = speedField.value;
            timeField.SetValueWithoutNotify(speed > 0f
                ? (total / speed + totalWait).ToString("F6")
                : "-");
        }

        void Refresh()
        {
            RebuildPopups();
            Recompute();
        }

        startPopup.RegisterValueChangedCallback(_ =>
        {
            int idx = choices.IndexOf(startPopup.value);
            if (idx >= 0) { startIdx = idx; Recompute(); }
        });
        endPopup.RegisterValueChangedCallback(_ =>
        {
            int idx = choices.IndexOf(endPopup.value);
            if (idx >= 0) { endIdx = idx; Recompute(); }
        });
        speedField.RegisterValueChangedCallback(_ => Recompute());

        state.Changed += Refresh;
        Undo.undoRedoPerformed += Refresh;
        root.RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            state.Changed -= Refresh;
            Undo.undoRedoPerformed -= Refresh;
        });
        Refresh();
        return root;
    }

    static Label MakeSmallLabel(string text)
    {
        var lbl = new Label(text);
        lbl.style.fontSize = 10;
        lbl.style.color = EditorTheme.MutedText;
        lbl.style.flexShrink = 0;
        lbl.style.marginLeft = 2;
        lbl.style.marginRight = 2;
        return lbl;
    }
}
