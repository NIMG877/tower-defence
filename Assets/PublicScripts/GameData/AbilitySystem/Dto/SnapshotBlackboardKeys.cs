namespace AbilitySystem
{
    /// <summary>
    /// 战局快照注入宿主黑板的协议键：由 BasicScripts 侧 BattleSnapshotBuilder 写入，
    /// 生成技能的 fromBlackboard 参数与 write_blackboard 可引用。GameData 侧集中定义
    /// （依赖方向 BasicScripts→GameData），校验器据此判定"读键无生产者"警告、
    /// ability-ops.json 的 knownBlackboardKeys 与之对应，三处单源防漂移。
    /// </summary>
    public static class SnapshotBlackboardKeys
    {
        public const string SelfHpRate = "snapshot:self_hp_rate";
        public const string EnemyCount = "snapshot:enemy_count";
        public const string AllyCount = "snapshot:ally_count";
        public const string NearestEnemyDistance = "snapshot:nearest_enemy_distance";
        public const string LowestEnemyHpRate = "snapshot:lowest_enemy_hp_rate";
        public const string LowestEnemyId = "snapshot:lowest_enemy_id";

        public static readonly string[] All =
        {
            SelfHpRate, EnemyCount, AllyCount, NearestEnemyDistance, LowestEnemyHpRate, LowestEnemyId,
        };
    }
}
