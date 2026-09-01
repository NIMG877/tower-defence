using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MyUI
{
    /// <summary>
    /// TeamPanel 的编队切换下拉（teamswitch）。
    ///
    /// TMP_Dropdown 每次展开都会克隆 Template 生成"Dropdown List"、收起即销毁，
    /// 运行时监听器不随克隆复制，因此绑定分两层：
    /// - 选项与选中：常驻绑定 onValueChanged，选项在 RefreshOptions 从存档重建；
    /// - 行内改名输入框与 Add 按钮：长在克隆列表里，每次展开后由 BindOpenedList 重新绑定。
    ///   展开入口是挂在 teamswitch 上的 EventTrigger.PointerClick：同物体上 TMP_Dropdown
    ///   是 prefab 序列化组件（先挂）、EventTrigger 由代码追加（后挂），事件按组件顺序派发，
    ///   保证回调触发时 TMP_Dropdown.OnPointerClick 已经同步克隆出列表。
    /// </summary>
    public class TeamSwitchUI
    {
        private readonly TMP_Dropdown _dropdown;
        private readonly RectTransform _templateRT;

        /// <summary>选中了新编队（参数为新编队名）。TeamPanel 据此刷新编队槽位。</summary>
        public event Action<string> TeamSelected;
        /// <summary>当前编队被改名（参数为新名）。TeamPanel 据此更新 TeamFrame 的目标编队。</summary>
        public event Action<string> CurrentTeamRenamed;
        /// <summary>新增了编队（TeamPanel 据此刷新 TeamFrame——唯一空编队时的删除按钮要解锁）。</summary>
        public event Action TeamAdded;

        public TeamSwitchUI(TMP_Dropdown dropdown)
        {
            _dropdown = dropdown;
            _templateRT = (RectTransform)_dropdown.template;
            _dropdown.onValueChanged.AddListener(OnValueChanged);

            EventTrigger trigger = _dropdown.gameObject.AddComponent<EventTrigger>();
            EventTrigger.Entry click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            click.callback.AddListener(_ => BindOpenedList());
            trigger.triggers.Add(click);
        }

        /// <summary>从存档重建选项并选中当前编队。TeamPanel 每次进入时调用。</summary>
        public void RefreshOptions()
        {
            _dropdown.ClearOptions();
            _dropdown.AddOptions(new List<string>(SaveSystem.GetTeamNames()));
            _dropdown.SetValueWithoutNotify(CurrentTeamIndex());
            _dropdown.RefreshShownValue();
        }

        private void OnValueChanged(int index)
        {
            string name = _dropdown.options[index].text;
            SaveSystem.SetCurrentTeam(name);
            TeamSelected?.Invoke(name);
        }

        private int CurrentTeamIndex()
        {
            string current = SaveSystem.CurrentTeamName;
            for (int i = 0; i < _dropdown.options.Count; i++)
                if (_dropdown.options[i].text == current) return i;
            // currentTeam 不在编队列表里 = 存档不变式已破坏，直接暴露而不是静默选错
            throw new InvalidOperationException($"存档中的当前编队 [{current}] 不存在于编队列表");
        }

        /// <summary>绑定刚克隆出的列表：恢复固定高度、绑定行内改名输入框与 Add 按钮。</summary>
        private void BindOpenedList()
        {
            // 克隆列表挂在 teamswitch 下（Template 的父级），名字由 TMP_Dropdown 固定指定。
            // 新克隆总是 append 在 teamswitch 末尾；OnAddClicked 的 Hide+Show 同帧重开时，
            // 旧克隆的 Destroy 要到帧末才生效、同名共存，因此反向查找取最新的那个。
            RectTransform list = null;
            for (int i = _dropdown.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = _dropdown.transform.GetChild(i);
                if (child.name == "Dropdown List")
                {
                    list = child as RectTransform;
                    break;
                }
            }
            if (list == null) return; // 列表未展开（淡出中再点等边界），没有可绑定对象

            // TMP 会按当前行数收缩列表高度，行数少时会把 Template 底部的 Add 按钮区域压没——恢复 Template 固定高度
            list.sizeDelta = new Vector2(list.sizeDelta.x, _templateRT.sizeDelta.y);

            // TMP_Dropdown 展开时列表停在顶部（无"滚到选中项"的原生支持）——
            // 把当前选中行滚到视口顶部，超出滚动范围时钳到底部
            ScrollRect scroll = list.GetComponent<ScrollRect>();
            float contentHeight = scroll.content.rect.height;
            float viewHeight = scroll.viewport.rect.height;
            if (contentHeight > viewHeight)
            {
                float rowHeight = contentHeight / _dropdown.options.Count;
                float target = Mathf.Clamp01(_dropdown.value * rowHeight / (contentHeight - viewHeight));
                scroll.verticalNormalizedPosition = 1f - target;
            }

            // Content 下第 0 个子物体是 Item 模板（已停用），其后按序对应各选项
            Transform content = list.Find("Viewport/Content");
            for (int i = 0; i < _dropdown.options.Count; i++)
            {
                TMP_InputField input = content.GetChild(i + 1).GetComponentInChildren<TMP_InputField>();
                // 克隆体的 InputField 内部文本与显示文本不同步，聚焦编辑时会清空显示——先对齐
                input.text = _dropdown.options[i].text;
                int index = i;
                input.onEndEdit.AddListener(newName => OnRenameCommitted(index, newName, input));
            }

            list.Find("Add").GetComponent<Button>().onClick.AddListener(OnAddClicked);
        }

        /// <summary>行内改名提交：未修改则规范化显示，空名/重名回退原名，不改存档。</summary>
        private void OnRenameCommitted(int index, string newName, TMP_InputField input)
        {
            string oldName = _dropdown.options[index].text;
            newName = (newName ?? string.Empty).Trim();
            if (newName == oldName)
            {
                input.text = oldName;
                return;
            }
            if (newName.Length == 0 || NameTaken(newName))
            {
                input.text = oldName;
                return;
            }

            bool wasCurrent = oldName == SaveSystem.CurrentTeamName;
            SaveSystem.RenameTeam(oldName, newName);
            _dropdown.options[index].text = newName;
            _dropdown.RefreshShownValue();
            if (wasCurrent) CurrentTeamRenamed?.Invoke(newName);
        }

        private bool NameTaken(string name)
        {
            foreach (string teamName in SaveSystem.GetTeamNames())
                if (teamName == name) return true;
            return false;
        }

        /// <summary>新增编队：选项已变，收起重开列表让新行出现。</summary>
        private void OnAddClicked()
        {
            SaveSystem.AddTeam();
            RefreshOptions();
            _dropdown.Hide();
            _dropdown.Show();
            BindOpenedList();
            TeamAdded?.Invoke();
        }
    }
}
