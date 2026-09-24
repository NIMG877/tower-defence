using AbilitySystem;

/// <summary>
/// /generate-ability 请求 envelope 的单一构建点（客户端与服务端版本握手所依赖的契约）：
/// protocolVersion + opList + battleSnapshot + hostAssets + constraints。
/// Editor 探针（菜单④）与运行时 GenerateSkill 组件共用——契约字段只改这里，
/// 避免两份手抄漂移。返回匿名对象，由调用方自行序列化。
/// 快照 self 拍平进 entities；hostAssets 携带技能原型的第一决定因素
/// （job/subJob/攻击节奏/物法方向等决策字段）与资产清单投影（不传引用）——
/// bullets/canSpawnEntities 按下标登记（spawnIndex/bulletDataIndex 的合法域），
/// animations 是 apply_animation_override.resources 的合法域。skills/talents
/// 默认不传（宿主现有技能/天赋上下文按需开启）。不产出 iconKey（统一图标，
/// 技能卡走 AbilityIconPool null 兜底）。
/// hostAssets 的形状与构建器在 AbilitySystem.HostAssets（GameData 程序集）。
/// </summary>
public static class AgentGenerateRequest
{
    /// <summary>契约版本：与服务端 ability.db meta.protocolVersion 手动同步 bump
    /// （握手做相等断言，漂移即拒绝并给出 diff）。</summary>
    public const int ProtocolVersion = 4;

    public static object Build(Entity self, object constraints)
    {
        return new
        {
            protocolVersion = ProtocolVersion,
            opList = AbilityStepOpRegistry.RegisteredOps,
            battleSnapshot = BattleSnapshotBuilder.Build(self),
            hostAssets = HostAssets.FromEntityData(self.EntityData),
            constraints = constraints,
        };
    }
}
