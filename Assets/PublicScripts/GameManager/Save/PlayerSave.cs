using System;
using System.Collections.Generic;

/// <summary>
/// 运行时玩家存档。仅持久化玩家改得动的数据（拥有的角色、各队伍配置等）。
/// 静态配置（角色基础属性、技能数值等）走 ScriptableObject，不在这里。
///
/// 注意：P1-6 重构后，<see cref="TeamSave.Members"/> 不再是平行 List，而是 <see cref="MemberEntry"/> 列表——
/// 避免"两个 List 长度不一致"的隐性 bug。旧存档的平行 List 形态直接作废（开发初期无用户数据）。
/// </summary>
[Serializable]
public class PlayerSave
{
    public int saveVersion = 1;

    /// <summary>玩家拥有的角色 ID 列表</summary>
    public List<EntityID> charactersOwn = new List<EntityID>();

    /// <summary>用户可编辑的队伍（默认"编队1"，可新增）</summary>
    public List<TeamSave> teams = new List<TeamSave>();

    /// <summary>当前选中的编队名。始终对应 teams 中某支队伍的 teamName，出战时读取。</summary>
    public string currentTeam;
}

/// <summary>队伍成员条目（角色 ID + 该角色的技能选择索引）。</summary>
[Serializable]
public class MemberEntry
{
    public EntityID Id;
    public int SkillIndex;

    public MemberEntry() { }
    public MemberEntry(EntityID id, int skillIndex) { Id = id; SkillIndex = skillIndex; }
}

[Serializable]
public class TeamSave
{
    public string teamName;
    public List<MemberEntry> Members = new List<MemberEntry>();
}
