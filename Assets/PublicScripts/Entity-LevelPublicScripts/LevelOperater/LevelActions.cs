using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class LevelActions
{
    [Serializable]
    public struct Wave
    {
        public Track[] Tracks;
    }

    [Serializable]
    public struct Track
    {
        public string Name;
        public bool Locked;
        public Action[] Actions;
    }

    [Serializable]
    public struct Action
    {
        [Tooltip("指令类型:0-召唤可移动实体,1-生成静止实体,2-显示地面路径,3-显示近地悬浮路径,4-显示飞行路径,5-显示右侧提示卡,6-显示剧情")] public int CommandType;
        [Tooltip("绝对触发时间(秒),Wave 开始后 N 秒触发")] public float TriggerTime;
        [Tooltip("动作开始前函数")] public UnityEvent OnBeforeAction;
        [Tooltip("实体 ID")] public EntityID EntityPrefabID;
        [Tooltip("召唤实体阵营,1-Turret 2-Monster")] public int Camp;
        [Tooltip("动作重复函数组")] public UnityEvent<Entity> OnActionRepeat;
        [Tooltip("路径预制体序号")] public int PathSerial;
        [Tooltip("动作重复间隔时间数组(长度为重复次数)")] public float[] GapsFromLastRepeat;
        [Tooltip("是否修改实体的目标价值、首要目标、计算击杀数属性")] public bool ModifyAttributes;
        [Tooltip("修改的实体目标价值")] public int ModifyLevelHpConsume;
        [Tooltip("修改的实体是否为首要目标")] public bool ModifyPrimary;
        [Tooltip("修改的实体是否计算击杀")] public bool ModifyCountOperate;
        [Tooltip("放置位置")] public Vector2 Destination;
        [Tooltip("放置朝向")] public int Orientation;
        [Tooltip("头像")] public Image HeadImage;
        [Tooltip("内容")] public string Content;
        [Tooltip("持续时间")] public float DurationTime;
        public string[] Contents;
    }
}
