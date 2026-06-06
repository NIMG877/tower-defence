using System.IO;
using UnityEngine;

/// <summary>
/// 玩家动态数据的存档系统。
/// 存档位置：Application.persistentDataPath（Editor 和 Build 都可写）。
/// 写入策略：先备份主档到 .bak，再写临时文件，最后原子 rename。
/// 读取策略：主档 -> 备份 -> 新建默认。三级降级保证不丢数据不卡死。
/// </summary>
public static class SaveSystem
{
    public const int CurrentVersion = 1;

    static string Dir        => Application.persistentDataPath;
    static string MainPath   => Path.Combine(Dir, "player_save.json");
    static string TempPath   => Path.Combine(Dir, "player_save.tmp.json");
    static string BackupPath => Path.Combine(Dir, "player_save.bak.json");

    static PlayerSave _cache;

    public static PlayerSave Current
    {
        get
        {
            if (_cache == null) Load();
            return _cache;
        }
    }

    public static void Load()
    {
        _cache = TryRead(MainPath) ?? TryRead(BackupPath) ?? CreateDefault();
    }

    /// <summary>把当前 _cache 落盘。调用方在数据修改完成后必须显式调一次。</summary>
    public static void Save()
    {
        if (_cache == null) return;
        _cache.saveVersion = CurrentVersion;
        var json = JsonUtility.ToJson(_cache, prettyPrint: true);

        // 1) 主档已有内容 -> 先备份
        if (File.Exists(MainPath))
        {
            try { File.Copy(MainPath, BackupPath, overwrite: true); }
            catch { /* 备份失败不阻塞主流程 */ }
        }

        // 2) 写到临时文件
        File.WriteAllText(TempPath, json);

        // 3) 原子 rename
        if (File.Exists(MainPath)) File.Delete(MainPath);
        File.Move(TempPath, MainPath);
    }

    static PlayerSave TryRead(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            var loaded = JsonUtility.FromJson<PlayerSave>(json);
            return loaded != null ? loaded : null;
        }
        catch
        {
            return null;
        }
    }

    static PlayerSave CreateDefault()
    {
        var save = new PlayerSave();
        // 拥有的角色默认 = 全部可用角色（来自静态配置）
        foreach (var c in GameDataService.EntityRepository.GetByCategory("c"))
            save.charactersOwn.Add(c.ID);
        // 4 支空队伍
        for (int i = 1; i <= 4; i++)
            save.teams.Add(new TeamSave { teamName = "Team" + i });
        return save;
    }

    // ===== 查询 helper =====

    /// <summary>按名字找一支队伍。找不到返回 null。</summary>
    public static TeamSave GetTeam(string teamName)
    {
        return Current.teams.Find(t => t.teamName == teamName);
    }
}
