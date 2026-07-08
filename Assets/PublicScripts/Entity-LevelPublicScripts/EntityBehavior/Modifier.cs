using System;

/// <summary>
/// 数值修改器——纯数值，与语义解耦。
/// op 只管同类内部怎么叠加，不掺"伤害/闪避"等用途语义。
/// </summary>
public enum ModifierOp
{
    /// <summary>直接加算：F = Σ fᵢ</summary>
    AddFlat,
    /// <summary>直接乘算：M = Σ mᵢ，结果 <0 钳为 0</summary>
    AddPercent,
    /// <summary>最终加算：G = Σ gᵢ</summary>
    AddFlatFinal,
    /// <summary>最终乘算：P = Π pᵢ，单项 <0 视为 1</summary>
    MulFinal,
}

/// <summary>
/// 纯数值修改器。readonly struct：零 GC、缓存友好（List&lt;Modifier&gt; 连续内存）。
/// 不可变；所有改动重建整个数组，而非改单个元素。
/// </summary>
[Serializable]
public readonly struct Modifier
{
    public readonly string attribute;   // "Attack", "PhysicalDamageRate", ...
    public readonly ModifierOp op;
    public readonly float magnitude;    // 纯数值，不含语义

    public Modifier(string attribute, ModifierOp op, float magnitude)
    {
        this.attribute = attribute;
        this.op = op;
        this.magnitude = magnitude;
    }
}

/// <summary>
/// 属性名常量注册表。代码侧用 Attributes.Xxx，.asset 侧用字符串 "Xxx"。
/// 集中定义避免拼写漂移。
/// </summary>
public static class Attributes
{
    public const string Attack = "Attack";
    public const string Defense = "Defense";
    public const string MagicResistance = "MagicResistance";
    public const string MaxHp = "MaxHp";
    public const string MoveSpeed = "MoveSpeed";
    public const string BaseAttackTime = "BaseAttackTime";
    public const string AttackSpeed = "AttackSpeed";            // base=100（设计常量，非来自 EntityData）
    public const string AttackNum = "AttackNum";
    public const string AttackMinNum = "AttackMinNum";
    public const string BlockOccupation = "BlockOccupation";
    public const string HpRecover = "HpRecover";                // base=0，纯增量
    public const string PhysicalDodge = "PhysicalDodge";        // 存未命中概率（1-旧值）
    public const string MagicDodge = "MagicDodge";              // 存未命中概率（1-旧值）
    public const string PhysicalDamageRate = "PhysicalDamageRate";  // 存最终乘数（1+旧值）
    public const string MagicDamageRate = "MagicDamageRate";        // 存最终乘数（1+旧值）
}
