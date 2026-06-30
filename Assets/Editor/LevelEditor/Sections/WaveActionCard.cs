using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Action 卡片渲染。
/// 结构性变化(add/delete)整 cardsContainer 重建,字段级变化只更新现有卡片的 left/width/backgroundColor/border(O(N) 增量)。
/// 边框三态(优先级从高到低):选中(1px 亮琥珀) > 未激活 Track(1px 琥珀) > 普通(1px 灰)。
/// </summary>
public static class WaveActionCard
{
    public class State
    {
        public List<Button> Cards = new();
        public int LastActionCount = -1;
    }

    static Color CommandTypeColor(int cmd) => WaveTimelineSection.CommandTypeColor(cmd);

    public static void Render(
        VisualElement container,
        SerializedProperty actionsProp,
        int waveIdx,
        int trackIdx,
        Action<int, int, int> onActionSelected,
        Func<bool> isLocked,
        Func<float> getPxPerSec,
        Func<(int, int, int)> getCurrentSelection)
    {
        var state = container.userData as State;
        if (state == null)
        {
            state = new State();
            container.userData = state;
        }

        // 结构性变化(add/delete)整树重建;字段级变化走下面的增量更新
        if (state.LastActionCount != actionsProp.arraySize)
        {
            container.Clear();
            state.Cards.Clear();

            if (actionsProp.arraySize == 0)
            {
                var empty = new Label("(空)");
                empty.style.color = new Color(0.4f, 0.4f, 0.4f);
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                empty.style.position = Position.Absolute;
                empty.style.left = 8;
                empty.style.top = 3.5f;
                container.Add(empty);
            }
            else
            {
                for (int i = 0; i < actionsProp.arraySize; i++)
                {
                    int actionIdx = i;
                    var card = new Button(() => onActionSelected?.Invoke(waveIdx, trackIdx, actionIdx))
                    { text = $"A{i}" };
                    card.style.position = Position.Absolute;
                    card.style.top = 0;
                    card.style.height = 23;
                    card.style.marginTop = 3.5f;
                    // card.style.width = 60;  // 起始宽度,Render 阶段按 Duration 调整
                    card.style.color = new Color(0, 0, 0);
                    card.style.fontSize = 9;
                    card.style.paddingLeft = 2;
                    card.style.paddingRight = 2;
                    container.Add(card);
                    state.Cards.Add(card);
                }
            }

            state.LastActionCount = actionsProp.arraySize;
        }

        // 增量更新:位置 + 宽度 + 颜色
        float pxPerSec = getPxPerSec();
        var (selW, selT, selA) = getCurrentSelection();
        for (int i = 0; i < actionsProp.arraySize; i++)
        {
            var a = actionsProp.GetArrayElementAtIndex(i);
            var card = state.Cards[i];

            float triggerTime = a.FindPropertyRelative("TriggerTime").floatValue;
            card.style.left = triggerTime * pxPerSec;

            int cmd = a.FindPropertyRelative("CommandType").intValue;
            float duration = ComputeEndTime(a, cmd) - triggerTime;
            float width = Mathf.Max(20f, duration * pxPerSec);
            card.style.width = width;
            card.style.backgroundColor = CommandTypeColor(cmd);

            // 边框三态(优先级从高到低):选中 > 未激活 Track > 普通
            // - 选中:3px 亮琥珀,视觉强调当前编辑对象
            // - 未激活 Track(Locked):1px 琥珀,提示运行时该 Action 不参与
            // - 普通:1px 灰
            bool isSelected = selW == waveIdx && selT == trackIdx && selA == i;
            bool locked = isLocked();
            Color borderColor;
            float borderWidth;
            if (isSelected)
            {
                borderColor = new Color(1f, 0.7f, 0.15f);  // 亮琥珀(高亮)
                borderWidth = 1f;
            }
            else if (locked)
            {
                borderColor = new Color(0.8f, 0.6f, 0.2f);  // 琥珀(未激活)
                borderWidth = 1f;
            }
            else
            {
                borderColor = new Color(0.3f, 0.3f, 0.3f);  // 灰(普通)
                borderWidth = 1f;
            }
            card.style.borderLeftWidth = borderWidth;
            card.style.borderRightWidth = borderWidth;
            card.style.borderTopWidth = borderWidth;
            card.style.borderBottomWidth = borderWidth;
            card.style.borderLeftColor = borderColor;
            card.style.borderRightColor = borderColor;
            card.style.borderTopColor = borderColor;
            card.style.borderBottomColor = borderColor;
        }
    }

    /// <summary>
    /// 计算 Action 右端时间(TriggerTime + 占用时长)。
    /// 见 spec §2.4 派生表。
    /// </summary>
    public static float ComputeEndTime(SerializedProperty actionProp, int commandType)
    {
        float triggerTime = actionProp.FindPropertyRelative("TriggerTime").floatValue;
        switch (commandType)
        {
            case 0: // spawner:TriggerTime + sum(GapsFromLastRepeat)
                var gapsProp = actionProp.FindPropertyRelative("GapsFromLastRepeat");
                float sum = 0f;
                for (int i = 0; i < gapsProp.arraySize; i++)
                    sum += Mathf.Max(0f, gapsProp.GetArrayElementAtIndex(i).floatValue);
                return triggerTime + sum;
            case 2:
            case 3:
            case 4: // path preview:GapsFromLastRepeat 硬编码 = {0, printerLifeTime}
                var gapsProp2 = actionProp.FindPropertyRelative("GapsFromLastRepeat");
                float sum2 = 0f;
                for (int i = 0; i < gapsProp2.arraySize; i++)
                    sum2 += Mathf.Max(0f, gapsProp2.GetArrayElementAtIndex(i).floatValue);
                return triggerTime + sum2;
            case 5: // dialog:TriggerTime + DurationTime
                return triggerTime + Mathf.Max(0f, actionProp.FindPropertyRelative("DurationTime").floatValue);
            case 1:
            case 6: // 静态 / 剧情:无右端,返回 triggerTime(纯点)
            default:
                return triggerTime;
        }
    }
}