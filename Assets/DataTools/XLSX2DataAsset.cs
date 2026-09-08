// 静态数据重建工具：从 EntityAttributes.xlsx 重新生成 EntityDataCollection.asset
// 菜单：Tools > Static Data > Rebuild Entity Collection
// 流程：读 xlsx → 校验 → 备份旧 .asset → 转换 → 写入 → AssetDatabase.SaveAssets
//
// 实现：手写 OpenXML 解析器（0 外部依赖）
//   xlsx = ZIP 文件 → 解出 xl/sharedStrings.xml + xl/worksheets/sheet1.xml
//   用 System.IO.Compression.ZipFile（Unity Mono BCL 内置）+ System.Xml.Linq
//   不依赖任何 NuGet / DLL —— 不受 Unity 平台 / 依赖链问题影响
//
// xlsx 布局：第 1-2 行是注释/分组，第 3 行（cell A3）开始是 header + 数据。
// 用 HeaderRow=2 / DataStart=3 显式定位。
// xlsx 列名 = 策划用 camelCase；EntityData 字段 = PascalCase。两者映射在 ReadEntityData() 中显式列出。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using AbilitySystem;

public class XLSX2DataAsset
{
    const string XlsxPath  = "Assets/DataTools/EntityAttributes.xlsx";
    const string AssetPath = "Assets/Resources/GameDatas/EntityDataCollection.asset";
    const string BackupDir = "Assets/DataTools/.backup";
    const int    HeaderRow = 3;   // 0-based：第 4 行是英文 header（xlsx 第 3 行是中文 header 装饰，跳过）
    const int    DataStart = 4;   // 0-based：第 5 行起是数据

    [MenuItem("Tools/Static Data/Rebuild Entity Collection")]
    static void Rebuild()
    {
        try
        {
            // 批处理模式（-executeMethod 调用）下 DisplayDialog 不可用，跳过人工确认。
            if (!Application.isBatchMode &&
                !EditorUtility.DisplayDialog(
                "Rebuild Entity Data Collection",
                "This will overwrite EntityDataCollection.asset from EntityAttributes.xlsx.\n\n" +
                "A timestamped backup of the current .asset will be written to:\n" + BackupDir + "\n\nContinue?",
                "Rebuild", "Cancel"))
                return;

            if (!File.Exists(XlsxPath))
                throw new FileNotFoundException($"xlsx not found: {XlsxPath}");

            // 1. 读 xlsx：解 ZIP → sharedStrings + sheet1
            var xlsx = ReadXlsx(XlsxPath);

            // 2. 解析 header → colMap
            var colMap = ParseHeader(xlsx.Rows[HeaderRow], xlsx.SharedStrings);

            // 3. 解析数据行
            var data = new List<EntityData>();
            for (int r = DataStart; r < xlsx.Rows.Count; r++)
            {
                var d = ReadEntityData(xlsx.Rows[r], xlsx.SharedStrings, colMap);
                if (d == null) continue;  // 跳过 ID_C 为空的空行
                data.Add(d);
            }

            // 4. 排序（与原 xlsx2json.py 行为一致）
            data = data.OrderBy(d => d.ID.ID_C).ThenBy(d => d.ID.ID_N).ToList();

            // 5. 校验
            ValidateIds(data);
            ValidateCanSpawnRefs(data);

            // 6. 备份
            if (File.Exists(AssetPath)) BackupCurrentAsset();

            // 7. 加载或创建 SO
            var so = AssetDatabase.LoadAssetAtPath<EntityDataCollection>(AssetPath);
            bool created = false;
            if (so == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
                so = ScriptableObject.CreateInstance<EntityDataCollection>();
                AssetDatabase.CreateAsset(so, AssetPath);
                created = true;
            }

            // 8. 写入
            so.SetEntityBasicDatas(data.ToArray());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[XLSX2DataAsset] OK. {data.Count} entries written to {AssetPath}" +
                      (created ? " (new asset created)" : ""));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[XLSX2DataAsset] FAILED: {ex.Message}\n{ex.StackTrace}");
            EditorUtility.DisplayDialog("Rebuild Failed", ex.Message, "OK");
        }
    }

    // ===== xlsx 解 ZIP + 解析 =====
    // xlsx 是 OpenXML spreadsheet，物理上是一个 ZIP 包：
    //   xl/sharedStrings.xml  → 共享字符串池
    //   xl/worksheets/sheet1.xml → 每个 <row r="N"> 是一行，<c r="A1" t="s"><v>idx</v></c> 是一个 cell

    class XlsxData
    {
        public List<string> SharedStrings;
        public List<Dictionary<int, CellValue>> Rows;  // 0-based 行索引 → 列索引 → cell
    }

    class CellValue
    {
        public string Type;   // "s"=shared string, "str"=inline string, "n"/缺=number, "b"=bool
        public string Raw;    // 原始字符串值
    }

    static XlsxData ReadXlsx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var ssEntry = zip.GetEntry("xl/sharedStrings.xml");
        var shEntry = zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet") && e.FullName.EndsWith(".xml"));
        if (shEntry == null) throw new InvalidDataException("xl/worksheets/sheetN.xml not found");

        var result = new XlsxData
        {
            SharedStrings = ssEntry != null ? ParseSharedStrings(ssEntry) : new List<string>(),
            Rows = ParseSheet(shEntry),
        };
        return result;
    }

    static List<string> ParseSharedStrings(ZipArchiveEntry entry)
    {
        var list = new List<string>();
        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        // <sst><si><t>text</t></si>...</sst>
        foreach (var si in doc.Descendants().Where(e => e.Name.LocalName == "si"))
        {
            // 一个 <si> 可能含多个 <t>（富文本片段），按出现顺序拼接
            var text = string.Concat(si.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
            list.Add(text);
        }
        return list;
    }

    static List<Dictionary<int, CellValue>> ParseSheet(ZipArchiveEntry entry)
    {
        var rows = new List<Dictionary<int, CellValue>>();
        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        // <worksheet><sheetData><row r="3"><c r="A3" t="s"><v>0</v></c>...</row>...</sheetData></worksheet>
        var rowElements = doc.Descendants().Where(e => e.Name.LocalName == "row");
        foreach (var rowEl in rowElements)
        {
            // XDocument 的 Attribute("r") 会用 XName.Get("r")（无 namespace），找不到 xmlns 下的属性。改用 LocalName 查
            var rAttr = rowEl.Attributes().FirstOrDefault(a => a.Name.LocalName == "r");
            var rowIdx = int.Parse(rAttr?.Value ?? "0") - 1;  // 0-based
            // 确保 list 长度足够（处理稀疏行号）
            while (rows.Count <= rowIdx) rows.Add(new Dictionary<int, CellValue>());
            var row = rows[rowIdx];
            foreach (var c in rowEl.Elements().Where(e => e.Name.LocalName == "c"))
            {
                var refAttr = c.Attributes().FirstOrDefault(a => a.Name.LocalName == "r")?.Value;  // e.g. "A3"
                var typeAttr = c.Attributes().FirstOrDefault(a => a.Name.LocalName == "t")?.Value; // "s" "str" "b" 等
                var vEl = c.Elements().FirstOrDefault(e => e.Name.LocalName == "v");
                var isEl = c.Elements().FirstOrDefault(e => e.Name.LocalName == "is");
                string raw;
                if (vEl != null) raw = vEl.Value;
                else if (isEl != null) raw = string.Concat(isEl.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
                else raw = null;
                var colIdx = RefToColIndex(refAttr);
                row[colIdx] = new CellValue { Type = typeAttr, Raw = raw };
            }
        }
        return rows;
    }

    // "A3" → 0 (A 是第 0 列)
    static int RefToColIndex(string cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return 0;
        int colEnd = 0;
        while (colEnd < cellRef.Length && char.IsLetter(cellRef[colEnd])) colEnd++;
        var colPart = cellRef.Substring(0, colEnd);
        int result = 0;
        foreach (var ch in colPart) result = result * 26 + (char.ToUpper(ch) - 'A' + 1);
        return result - 1;  // 0-based
    }

    // ===== header / 数据行 =====
    static Dictionary<string, int> ParseHeader(Dictionary<int, CellValue> headerRow, List<string> sharedStrings)
    {
        var map = new Dictionary<string, int>();
        foreach (var kv in headerRow)
        {
            var name = GetCellString(kv.Value, sharedStrings);
            if (!string.IsNullOrEmpty(name)) map[name] = kv.Key;
        }
        return map;
    }

    static EntityData ReadEntityData(Dictionary<int, CellValue> row, List<string> sharedStrings, Dictionary<string, int> colMap)
    {
        var idC = GetStr(row, colMap, "ID_C", sharedStrings);
        if (string.IsNullOrEmpty(idC)) return null;

        var (subJob, subJobTrait) = ResolveSubJobTrait(GetStr(row, colMap, "CharacterSubJob", sharedStrings));
        return new EntityData
        {
            ID = new EntityID(idC, GetInt(row, colMap, "ID_N", sharedStrings)),
            ChineseName = GetStr(row, colMap, "ChineseName", sharedStrings),
            EnglishName = GetStr(row, colMap, "EnglishName", sharedStrings),
            Description = GetStr(row, colMap, "Description", sharedStrings),
            Prefab = LoadResource<GameObject>(GetStr(row, colMap, "Prefab", sharedStrings)),
            HeadImage = LoadResource<Sprite>(GetStr(row, colMap, "HeadImage", sharedStrings)),
            HalfBodyImage = LoadResource<Sprite>(GetStr(row, colMap, "HalfBodyImage", sharedStrings)),
            WholeImage = LoadResource<Sprite>(GetStr(row, colMap, "WholeImage", sharedStrings)),
            DefaultCamp = GetInt(row, colMap, "DefaultCamp", sharedStrings),
            CharacterRarity = GetInt(row, colMap, "CharacterRarity", sharedStrings),
            CharacterJob = ParseJob(GetStr(row, colMap, "CharacterJob", sharedStrings)),
            CharacterSubJob = subJob,
            MonsterStatus = GetInt(row, colMap, "MonsterStatus", sharedStrings),
            MonsterLabel = GetStr(row, colMap, "MonsterLabel", sharedStrings),
            MonsterIsPrimary = GetInt(row, colMap, "MonsterIsPrimary", sharedStrings) != 0,
            MonsterCountOperated = GetInt(row, colMap, "MonsterCountOperated", sharedStrings) != 0,
            MonsterLevelHpConsume = GetInt(row, colMap, "MonsterLevelHpConsume", sharedStrings),
            VisionRange = ParseInt2DArrayAsList(GetStr(row, colMap, "VisionRange", sharedStrings)),
            VisionRadius = GetFloat(row, colMap, "VisionRadius", sharedStrings),
            Attack = GetFloat(row, colMap, "Attack", sharedStrings),
            BaseAttackTime = GetFloat(row, colMap, "BaseAttackTime", sharedStrings),
            AttackNum = GetInt(row, colMap, "AttackNum", sharedStrings),
            DamageType = GetInt(row, colMap, "DamageType", sharedStrings),
            TargetPriority = (OrderLogic)GetInt(row, colMap, "TargetPriority", sharedStrings),
            SplashRadius = GetFloat(row, colMap, "SplashRadius", sharedStrings),
            MaxHp = GetFloat(row, colMap, "MaxHp", sharedStrings),
            Defense = GetFloat(row, colMap, "Defense", sharedStrings),
            MagicResistance = GetFloat(row, colMap, "MagicResistance", sharedStrings),
            PhysicalDodge = GetFloat(row, colMap, "PhysicalDodge", sharedStrings),
            MagicDodge = GetFloat(row, colMap, "MagicDodge", sharedStrings),
            BlockOccupation = GetInt(row, colMap, "BlockOccupation", sharedStrings),
            TauntLevel = GetInt(row, colMap, "TauntLevel", sharedStrings),
            CanSpawnEntityIds = ParseEntityIdList(GetStr(row, colMap, "CanSpawnEntityIds", sharedStrings)),
            CanSpawnEntityCounts = ParseIntList(GetStr(row, colMap, "CanSpawnEntityCounts", sharedStrings)),
            IsStatic = GetInt(row, colMap, "IsStatic", sharedStrings) != 0,
            Cost = GetInt(row, colMap, "Cost", sharedStrings),
            CanCallBack = GetInt(row, colMap, "CanCallBack", sharedStrings) != 0,
            NeedsDirectionSelection = GetInt(row, colMap, "NeedsDirectionSelection", sharedStrings) != 0,
            CanSetType = GetInt(row, colMap, "CanSetType", sharedStrings),
            RespawnTime = GetFloat(row, colMap, "RespawnTime", sharedStrings),
            RespawnStrategy = ParseRespawnStrategy(GetStr(row, colMap, "RespawnStrategy", sharedStrings)),
            CanRespawn = GetInt(row, colMap, "CanRespawn", sharedStrings) != 0,
            RespawnCostUp = GetFloat(row, colMap, "RespawnCostUp", sharedStrings),
            MaxOccupyCount = GetInt(row, colMap, "MaxOccupyCount", sharedStrings),
            MoveSpeed = GetFloat(row, colMap, "MoveSpeed", sharedStrings),
            MassLevel = GetInt(row, colMap, "MassLevel", sharedStrings),
            MoveMethod = GetInt(row, colMap, "MoveMethod", sharedStrings),
            Skills = LoadResourceList<AbilityConfig>(GetStr(row, colMap, "Skills", sharedStrings)),
            Talents = LoadResourceList<AbilityConfig>(GetStr(row, colMap, "Talents", sharedStrings)),
            SubJobTrait = subJobTrait,
            AnimationResources = LoadResource<AnimationResources>(GetStr(row, colMap, "AnimationResources", sharedStrings)),
        };
    }

    // ===== cell value 读取 =====
    // cell type:
    //   t="s"   → raw 是 sharedStrings 索引
    //   t="str" → raw 是 inline string
    //   t="b"   → raw 是 "0" / "1"
    //   缺 / "n" → raw 是数字字面量

    static string GetCellString(CellValue cv, List<string> sharedStrings)
    {
        if (cv == null || string.IsNullOrEmpty(cv.Raw)) return null;
        if (cv.Type == "s")
        {
            if (int.TryParse(cv.Raw, out var idx) && idx >= 0 && idx < sharedStrings.Count)
                return sharedStrings[idx];
            return null;
        }
        return cv.Raw;
    }

    static string GetStr(Dictionary<int, CellValue> row, Dictionary<string, int> colMap, string col, List<string> sharedStrings)
    {
        if (!colMap.TryGetValue(col, out var c))
        {
            Debug.LogWarning($"[XLSX2DataAsset] Column not found: {col}");
            return null;
        }
        if (!row.TryGetValue(c, out var cv)) return null;
        return GetCellString(cv, sharedStrings);
    }

    static int GetInt(Dictionary<int, CellValue> row, Dictionary<string, int> colMap, string col, List<string> sharedStrings)
    {
        var s = GetStr(row, colMap, col, sharedStrings);
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    static float GetFloat(Dictionary<int, CellValue> row, Dictionary<string, int> colMap, string col, List<string> sharedStrings)
    {
        var s = GetStr(row, colMap, col, sharedStrings);
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
    }

    // ===== 嵌套结构解析（xlsx 里这些列是结构化字符串） =====
    // xlsx 实际格式：visionRange_L 是 Python tuple list 格式 [(0,0),(0,1)]（python xlsx2json.py 时代产物）
    //                canSpawnEntityID_L / canSpawnEntityNum_L 是 JSON 数组 ["t-1"] / [1]
    // 统一在解析前 normalize 为 JSON 数组

    // 把 (a,b,c) 转成 [a,b,c]，用于把 Python tuple list 转换成 JSON 数组
    static string PythonTupleListToJson(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, @"\(([^()]+)\)", m => "[" + m.Groups[1].Value + "]");

    static List<Vector2Int> ParseInt2DArrayAsList(string s)
    {
        var list = new List<Vector2Int>();
        if (string.IsNullOrEmpty(s)) return list;
        try
        {
            var arr = JsonConvert.DeserializeObject<int[][]>(PythonTupleListToJson(s));
            if (arr == null) return list;
            foreach (var pair in arr)
                if (pair != null && pair.Length >= 2) list.Add(new Vector2Int(pair[0], pair[1]));
        }
        catch { }
        return list;
    }

    static List<EntityID> ParseEntityIdList(string s)
    {
        var list = new List<EntityID>();
        if (string.IsNullOrEmpty(s)) return list;
        try
        {
            var arr = JsonConvert.DeserializeObject<string[]>(s);
            if (arr == null) return list;
            foreach (var idStr in arr)
            {
                if (string.IsNullOrEmpty(idStr)) continue;
                var parts = idStr.Split('-');
                if (parts.Length == 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    list.Add(new EntityID(parts[0], n));
            }
        }
        catch { }
        return list;
    }

    static List<int> ParseIntList(string s)
    {
        var list = new List<int>();
        if (string.IsNullOrEmpty(s)) return list;
        try
        {
            var arr = JsonConvert.DeserializeObject<int[]>(s);
            if (arr != null) list.AddRange(arr);
        }
        catch { }
        return list;
    }

    // ===== 中文枚举映射 =====
    static int ParseJob(string s) => s switch
    {
        "先锋" => 0, "近卫" => 1, "重装" => 2, "狙击" => 3, "术师" => 4,
        "医疗" => 5, "辅助" => 6, "特种" => 7, "装置" => 8, _ => 9,
    };

    // ===== 子职业（CharacterSubJob / SubJobTrait）=====
    // 0=无;xlsx 缺列/空格子经 GetStr→TryParseSubJob 得 0,让"没有子职业"成为零配置默认值
    // (怪物行零填写)。有意不沿用 CharacterJob "末位=_" 约定,见 spec 2026-09-05 §2。

    /// <summary>中文子职业名 → id。空=0(无,合法);未知名返回 false(数据错误,由调用方记日志跳过,不打断 Rebuild)。
    /// 全部 14 个子职业见 docs/profession_codes.md(资产用其中代码命名);1-3 是首批实现的,已持久化进
    /// EntityDataCollection,保持稳定不重编号,其后按主职业顺序追加。</summary>
    public static bool TryParseSubJob(string s, out int id)
    {
        switch (s)
        {
            case null:
            case "":
                id = 0; return true;
            case "秘术师": id = 1; return true;      // mystic
            case "冲锋手": id = 2; return true;      // charger
            case "凝滞师": id = 3; return true;      // slower
            case "尖兵": id = 4; return true;        // pioneer
            case "强攻手": id = 5; return true;      // centurion
            case "领主": id = 6; return true;        // lord
            case "无畏者": id = 7; return true;      // fearless
            case "铁卫": id = 8; return true;        // protector
            case "守护者": id = 9; return true;      // guardian
            case "速射手": id = 10; return true;     // fastshot
            case "炮手": id = 11; return true;       // aoesniper
            case "中坚术师": id = 12; return true;   // corecaster
            case "扩散术师": id = 13; return true;   // splashcaster
            case "医师": id = 14; return true;       // physician
            default:
                id = 0; return false;
        }
    }

    // 子职业 id → 特性资产(Resources 路径)。字典即"已实现特性注册表":
    // 没实现的子职业不进这张表,装载时按缺资产 LogWarning,不静默。
    static readonly Dictionary<int, string> SubJobAbilityPaths = new()
    {
        [1] = "Abilities/SubJobs/mystic",       // 秘术师:积攒攻击能量
        [2] = "Abilities/SubJobs/charger",      // 冲锋手:击杀获得 1 费用
        [3] = "Abilities/SubJobs/slower",       // 凝滞师:攻击造成停顿
        [4] = "Abilities/SubJobs/pioneer",      // 尖兵
        [5] = "Abilities/SubJobs/centurion",    // 强攻手
        [6] = "Abilities/SubJobs/lord",         // 领主
        [7] = "Abilities/SubJobs/fearless",     // 无畏者
        [8] = "Abilities/SubJobs/protector",    // 铁卫
        [9] = "Abilities/SubJobs/guardian",     // 守护者
        [10] = "Abilities/SubJobs/fastshot",    // 速射手
        [11] = "Abilities/SubJobs/aoesniper",   // 炮手
        [12] = "Abilities/SubJobs/corecaster",  // 中坚术师
        [13] = "Abilities/SubJobs/splashcaster", // 扩散术师
        [14] = "Abilities/SubJobs/physician",   // 医师
    };

    /// <summary>
    /// xlsx 子职业格 → (CharacterSubJob, SubJobTrait)。
    /// 三种不装载分支(未知名/缺映射/缺资产)一律 LogWarning 跳过,不打断 Rebuild;
    /// "trait 已装载 ⇒ 子职业已登记"由本方法构造期保证。paths 仅供测试注入,生产用默认注册表。
    /// </summary>
    public static (int subJob, AbilityConfig trait) ResolveSubJobTrait(string cell, Dictionary<int, string> paths = null)
    {
        paths ??= SubJobAbilityPaths;
        if (!TryParseSubJob(cell, out int id))
        {
            Debug.LogWarning($"[XLSX2DataAsset] Unknown CharacterSubJob name \"{cell}\"; skipped (treated as no subjob). Known names: see TryParseSubJob.");
            return (0, null);
        }
        if (id == 0) return (0, null);
        if (!paths.TryGetValue(id, out var path))
        {
            Debug.LogWarning($"[XLSX2DataAsset] CharacterSubJob {id} ({cell}) has no trait asset registered in SubJobAbilityPaths; SubJobTrait left null.");
            return (id, null);
        }
        var trait = Resources.Load<AbilityConfig>(path);
        if (trait == null)
            Debug.LogWarning($"[XLSX2DataAsset] CharacterSubJob {id} ({cell}) trait asset not found at: {path}; SubJobTrait left null.");
        return (id, trait);
    }

    static int ParseRespawnStrategy(string s) => s switch
    {
        "默认" => 0, "唯一" => 1, "禁用" => 2, _ => 3,
    };

    // ===== 校验 =====
    static void ValidateIds(List<EntityData> data)
    {
        var seen = new HashSet<string>();
        var dups = new List<string>();
        foreach (var d in data)
        {
            var key = $"{d.ID.ID_C}-{d.ID.ID_N}";
            if (!seen.Add(key)) dups.Add(key);
        }
        if (dups.Count > 0)
            throw new InvalidDataException($"Duplicate EntityID(s): {string.Join(", ", dups)}");
    }

    static void ValidateCanSpawnRefs(List<EntityData> data)
    {
        var idSet = new HashSet<string>(data.Select(d => $"{d.ID.ID_C}-{d.ID.ID_N}"));
        foreach (var d in data)
        {
            if (d.CanSpawnEntityIds == null) continue;
            foreach (var spawn in d.CanSpawnEntityIds)
            {
                var key = $"{spawn.ID_C}-{spawn.ID_N}";
                if (!idSet.Contains(key))
                    throw new InvalidDataException(
                        $"canSpawnEntityID reference not found: {spawn} (referenced by {d.ID})");
            }
        }
    }

    // ===== 备份 + 资源加载 =====
    static void BackupCurrentAsset()
    {
        Directory.CreateDirectory(BackupDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupPath = Path.Combine(BackupDir, $"EntityDataCollection.{stamp}.bak");
        File.Copy(AssetPath, backupPath, overwrite: true);
        Debug.Log($"[XLSX2DataAsset] Backed up to {backupPath}");
    }

    static T LoadResource<T>(string path) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(path)) return null;
        T assert = Resources.Load<T>(path);
        if (assert == null)
            Debug.LogWarning($"[XLSX2DataAsset] {typeof(T).Name} resource not found at: {path}");
        return assert;
    }
    static List<T> LoadResourceList<T>(string s) where T : UnityEngine.Object
    {
        var list = new List<T>();
        if (string.IsNullOrEmpty(s)) return list;
        try
        {
            var arr = JsonConvert.DeserializeObject<string[]>(s);
            if (arr == null) return list;
            foreach (var path in arr)
            {
                var asset = LoadResource<T>(path);
                list.Add(asset);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[XLSX2DataAsset] ParseAbilityConfigList failed: {ex.Message} (raw: {s})");
        }
        return list;
    }

}
