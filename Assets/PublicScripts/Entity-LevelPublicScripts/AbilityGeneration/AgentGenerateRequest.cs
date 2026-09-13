using System.Collections.Generic;
using AbilitySystem;

/// <summary>
/// /generate-ability 请求 envelope 的单一构建点（客户端与服务端版本握手所依赖的契约）：
/// protocolVersion + opList + battleSnapshot + hostAssets + constraints。
/// Editor 探针（菜单④）与运行时 GenerateSkill 组件共用——契约字段只改这里，
/// 避免两份手抄漂移。返回匿名对象，由调用方自行序列化。
/// </summary>
public static class AgentGenerateRequest
{
    public static object Build(Entity self, object constraints)
    {
        return new
        {
            protocolVersion = AbilityOpsSchema.Load().protocolVersion,
            opList = AbilityStepOpRegistry.RegisteredOps,
            battleSnapshot = BattleSnapshotBuilder.Build(self),
            hostAssets = new
            {
                canSpawnEntityIds = self.EntityData.CanSpawnEntityIds != null
                    ? self.EntityData.CanSpawnEntityIds.ConvertAll(id => id.ToString())
                    : new List<string>(),
                bulletCount = self.EntityData.Bullets?.Count ?? 0,
                iconKeys = AbilityIconPool.GetAllKeys(),
            },
            constraints = constraints,
        };
    }
}
