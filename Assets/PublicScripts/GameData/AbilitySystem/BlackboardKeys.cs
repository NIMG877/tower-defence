namespace AbilitySystem
{
    /// <summary>黑板保留键。桥接与组件两侧共用一个常量，避免字面量散落。</summary>
    public static class BlackboardKeys
    {
        /// <summary>攻击索敌候选列表（List&lt;Entity&gt;）。仅在
        /// BeforeTargetSelectEvent 派发的同步窗口内存在（桥接前 Set、后 Remove），
        /// 窗口外读取走"键缺失"告警路径暴露配线错误。</summary>
        public const string AttackCandidates = "attackCandidates";
    }
}
