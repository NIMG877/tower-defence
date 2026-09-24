using System;

namespace AbilitySystem
{
    /// <summary>
    /// LLM/服务器侧生成的 AbilityConfig 传输形态。rules/sp 直接复用运行时类型
    /// （AbilityRuleConfig/StepConfig/ParamList/SPConfig 全部是字符串与数值的可序列化
    /// POCO，无 Unity 资产引用），反序列化时经 StringEnumConverter 传枚举名。
    /// 字段集是与服务端的契约：增删字段须同步契约版本
    /// <c>AgentGenerateRequest.ProtocolVersion</c>（与服务端 ability.db
    /// meta.protocolVersion 手动 bump）。
    /// 图标不走引用（LLM 无法生成 Sprite），用 iconKey 指向 <see cref="AbilityIconPool"/> 的键。
    /// </summary>
    [Serializable]
    public class AbilityConfigDto
    {
        public string abilityId;
        public string abilityName;
        public string description;
        public string iconKey;
        public SPConfig sp;
        public AbilityRuleConfig[] rules = Array.Empty<AbilityRuleConfig>();
    }
}
