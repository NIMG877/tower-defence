using System;

/// <summary>
/// 数值修改器的运算方式——只管同类内部怎么叠加，不掺"伤害/闪避"等用途语义。
/// </summary>
public enum ModifierOp
{
    /// <summary>直接加算：F = Σ fᵢ。作用于基础值，加法定值。</summary>
    AddFlat,
    /// <summary>直接乘算：M = Σ mᵢ，累加和 &lt;0 时钳为 0。以 (1+M) 形式作用于基础值。</summary>
    AddPercent,
    /// <summary>最终加算：G = Σ gᵢ。作用于"基础值经直接类后的中间结果"，加法定值。</summary>
    AddFlatFinal,
    /// <summary>最终乘算：P = Π pᵢ，单项 &lt;0 视为 1。作用于最终结果，乘法。</summary>
    MulFinal,
}

/// <summary>
/// 纯数值修改器。readonly struct：零 GC、缓存友好（List&lt;Modifier&gt; 连续内存）。
/// 不可变；所有改动重建整个数组，而非改单个元素。
///
/// <para>一个 Modifier = "改哪个属性 (attribute) + 怎么叠加 (op) + 改多少 (magnitude)"。
/// 三者组合才完整描述一条数值改动。</para>
/// </summary>
[Serializable]
public readonly struct Modifier
{
    /// <summary>
    /// 目标属性名。**裸字符串**，无中央注册表——新增属性只需直接写字符串，无需维护枚举/常量表。
    ///
    /// <para><b>怎么写</b>：写属性的英文名，与 <c>EntityStats</c> 里 <c>SetBase</c>/<c>GetFinal</c>
    /// 用的字符串完全一致。<c>EntityData</c> 的所有 int/float/bool/enum 字段均已接入 store
    /// （2026-07-09 全字段接入），完整清单见 <c>EntityStats.AttributesCaculateFirst</c> 的 <c>SetBase</c> 调用。
    /// 非数值字段也接入——bool 存 0/1、enum 存 (int)，读侧 <c>XxxS</c> 强转还原。</para>
    ///
    /// <para>按类型的约定：</para>
    /// <list type="bullet">
    /// <item><b>float 属性</b>（<c>Attack</c>/<c>Defense</c>/<c>MagicResistance</c>/<c>MaxHp</c>/<c>MoveSpeed</c>/
    ///   <c>BaseAttackTime</c>/<c>RespawnTime</c>/<c>RespawnCostUp</c>/<c>VisionRadius</c>）：
    ///   <c>AddFlat</c>/<c>AddPercent</c> 叠加，magnitude 即数值。</item>
    /// <item><b>int 属性</b>（<c>AttackNum</c>/<c>AttackMinNum</c>/<c>BlockOccupation</c>/<c>TauntLevel</c>/
    ///   <c>DefaultCamp</c>/<c>CharacterRarity</c>/<c>CharacterJob</c>/<c>MonsterStatus</c>/
    ///   <c>MonsterLevelHpConsume</c>/<c>DamageType</c>/<c>Cost</c>/<c>CanSetType</c>/
    ///   <c>RespawnStrategy</c>/<c>MaxOccupyCount</c>/<c>MassLevel</c>/<c>MoveMethod</c>）：
    ///   用 <c>AddFlat</c>，magnitude 是整数增量，读侧 <c>(int)GetFinal</c> 截断（&lt;1 的小增会被吞）。</item>
    /// <item><b>bool 属性</b>（<c>MonsterIsPrimary</c>/<c>MonsterCountOperated</c>/
    ///   <c>IsStatic</c>/<c>CanCallBack</c>/<c>NeedsDirectionSelection</c>/<c>CanRespawn</c>）：
    ///   用 <c>AddFlat</c>，magnitude 是 0/1 或正负数；读侧 <c>GetFinal &gt; 0</c> 为 true。
    ///   设为 true：<c>+1</c>；设为 false：<c>-1</c>（或用负值覆盖）。</item>
    /// <item><b>enum 属性</b>（<c>TargetPriority</c>，类型 <c>OrderLogic</c>）：
    ///   用 <c>AddFlat</c>，magnitude 是枚举的 int 值增量，读侧 <c>(OrderLogic)(int)GetFinal</c>。
    ///   通常只用于"切换"语义时需写覆盖逻辑，直接加 int 会偏移到相邻枚举值。</item>
    /// <item><b>特殊换算属性</b>：
    ///   <c>AttackSpeed</c>（base=100，设计常量）；<c>HpRecover</c>（base=0，纯增量）；
    ///   <c>PhysicalDodge</c>/<c>MagicDodge</c>（base=0，magnitude 存<b>未命中概率</b>，迁移用 <c>1-旧值</c>）；
    ///   <c>PhysicalDamageRate</c>/<c>MagicDamageRate</c>（base=1，magnitude 存<b>最终乘数本身</b>，迁移用 <c>1+旧值</c>）。</item>
    /// </list>
    /// <para><b>拼写注意</b>：裸字符串拼错不报编译错，运行时 <see cref="AttributeStore.GetFinal"/>
    /// 会 <c>OneShotWarn</c> 并返回 0。大小写敏感，必须与 <c>SetBase</c> 处一致。</para>
    /// </summary>
    public readonly string attribute;

    /// <summary>同类内部的叠加方式。见 <see cref="ModifierOp"/> 各项说明。</summary>
    public readonly ModifierOp op;

    /// <summary>
    /// 数值量。语义随 <see cref="op"/> 与 <see cref="attribute"/> 变化：
    /// <c>AddFlat</c>/<c>AddFlatFinal</c> 是定值；<c>AddPercent</c> 是百分比小数（0.15=+15%；
    /// 累加和负数会被钳 0）；<c>MulFinal</c> 是乘数（单项 &lt;0 视为 1）。
    /// 对 Dodge/Rate 类属性，magnitude 已按迁移约定换算（见 <see cref="attribute"/> 注释）。
    /// </summary>
    public readonly float magnitude;

    public Modifier(string attribute, ModifierOp op, float magnitude)
    {
        this.attribute = attribute;
        this.op = op;
        this.magnitude = magnitude;
    }
}
