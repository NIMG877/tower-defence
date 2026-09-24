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
/// Repeat 标记:GapsFromLastRepeat.Length >= 1 时,每个 repeat 触发位置画一个小正方形标记。
/// </summary>
public static class WaveActionCard
{
    public class State
    {
        public List<Button> Cards = new();
        public List<VisualElement> SquareContainers = new();  // 每个 card 内一个,作为 square 的容器(覆盖 card)
        public int LastActionCount = -1;
    }

    const float CARD_HEIGHT = 23f;        // 卡片渲染高度
    const float SQUARE_SIZE = 10f;        // 正方形边长
    const float SQUARE_HALF = SQUARE_SIZE / 2f;
    const float SQUARE_TOP = (CARD_HEIGHT - SQUARE_SIZE) / 2f;  // 6.5 → 居中

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
                    card.style.paddingLeft = 0;
                    card.style.paddingRight = 0;
                    card.style.marginLeft=0;
                    card.style.marginRight=0;
                    container.Add(card);
                    state.Cards.Add(card);

                    // square 容器:覆盖整个 card,内含每个 repeat 的正方形标记
                    var squareContainer = new VisualElement();
                    squareContainer.style.position = Position.Absolute;
                    squareContainer.style.left = 0;
                    squareContainer.style.right = 0;
                    squareContainer.style.top = 0;
                    squareContainer.style.bottom = 0;
                    squareContainer.pickingMode = PickingMode.Ignore;  // 不拦截 card 点击
                    card.Add(squareContainer);
                    state.SquareContainers.Add(squareContainer);
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
            // 宽度规则:
            // - CommandType 2/3/4 path preview:固定 20px(不随 GapsFromLastRepeat 或 zoom 变化)
            // - 其他 cmd:duration * pxPerSec,最小 20px(防止纯点不可见)
            float width = (cmd == 2 || cmd == 3 || cmd == 4)
                ? 20f
                : Mathf.Max(20f, duration * pxPerSec);
            card.style.width = width;
            card.style.backgroundColor = CommandTypeColor(cmd);

            // 边框三态(优先级从高到低):选中 > 未激活 Track > 普通
            // - 选中:1px 亮琥珀,视觉强调当前编辑对象
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

            // ===== Repeat 正方形标记 =====
            // 仅 CommandType == 0(spawner)绘制。其他类型(静态/路径预览/dialog/剧情)无运行时 repeat 行为。
            // GapsFromLastRepeat.Length = N 时,画 N 个正方形 — 每次 repeat 触发点各一个。
            // g 从 0 开始:第 g 次 repeat 在 triggerTime + sum(gaps[0..g]) 位置。
            // 例:gaps=[0, 32, 32, 16] → 4 个正方形,中心在 card.left + cumGap×pxPerSec
            //   (默认 24 px/s 时即 +0/+768/+1536/+1920 px)。
            //   - g=0 时 cumGap=0 → 正方形贴卡片左端(主触发后立即 repeat 的视觉化)
            //   - g=3 时 cumGap=sum(全部) → 正方形在卡片右端(最后一次 repeat)
            var squareContainer = state.SquareContainers[i];
            squareContainer.Clear();
            if (cmd == 0)
            {
                var gapsProp = a.FindPropertyRelative("GapsFromLastRepeat");
                int gapCount = gapsProp.arraySize;
                if (gapCount >= 1)
                {
                    float cumGap = 0f;
                    for (int g = 0; g < gapCount; g++)
                    {
                        cumGap += Mathf.Max(0f, gapsProp.GetArrayElementAtIndex(g).floatValue);
                        float centerX = cumGap * pxPerSec;
                        var square = new VisualElement();
                        square.style.position = Position.Absolute;
                        square.style.width = SQUARE_SIZE;
                        square.style.height = SQUARE_SIZE;
                        square.style.left = centerX - SQUARE_HALF - 1;
                        square.style.top = SQUARE_TOP;
                        square.style.backgroundColor = new Color(0.05f, 0.3f, 0.15f);  // 深绿(比 spawner 卡片绿 0.3/0.8/0.6 深)
                        square.pickingMode = PickingMode.Ignore;  // 不拦截 card 点击
                        squareContainer.Add(square);
                    }
                }
            }
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