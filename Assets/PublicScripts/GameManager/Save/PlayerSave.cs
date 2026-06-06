using System;
using System.Collections.Generic;

/// <summary>
/// 运行时玩家存档。仅持久化玩家改得动的数据（拥有的角色、各队伍配置等）。
/// 静态配置（角色基础属性、技能数值等）走 ScriptableObject，不在这里。
/// </summary>
[Serializable]
public class PlayerSave
{
    public int saveVersion = 1;

    /// <summary>玩家拥有的角色 ID 列表</summary>
    public List<EntityID> charactersOwn = new List<EntityID>();

    /// <summary>用户可编辑的队伍（Team1 ~ Team4）</summary>
    public List<TeamSave> teams = new List<TeamSave>();
}

[Serializable]
public class TeamSave
{
    public string teamName;
    public List<EntityID> members = new List<EntityID>();
    /// <summary>与 members 平行：members[i] 的当前技能选择索引</summary>
    public List<int> skillSelects = new List<int>();
}
