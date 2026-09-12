using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace AbilitySystem
{
    /// <summary>
    /// DTO → 运行时 AbilityConfig。Parse 走严格反序列化（MissingMemberHandling.Error +
    /// StringEnumConverter）：LLM 幻觉出的未知字段/未知枚举名在解析层直接失败，
    /// 而不是静默丢字段后产出无声失效的技能。
    /// FromDto 把 wrapper 字段拷进 CreateInstance 出来的 SO。
    /// 产物 SO 的生命周期归调用方：不再被任何 AbilityRuntime 引用时由调用方
    /// Object.Destroy（RemoveSkill 与 AddExtraAbility 一样不销毁 config，设计师资产
    /// 同样走此路径，运行时无法区分两种来源）。
    /// DTO 视为单次使用：FromDto 直接引用 dto.sp/dto.rules，不做防御性克隆。
    /// </summary>
    public static class AbilityConfigBuilder
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            Converters = { new StringEnumConverter() },
        };

        /// <summary>反序列化 DTO。未知成员或非法枚举名抛 JsonSerializationException，
        /// 异常消息携带成员名/目标类型，供生成管线回喂重试。</summary>
        public static AbilityConfigDto Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) throw new ArgumentException("json is null or empty", nameof(json));
            return JsonConvert.DeserializeObject<AbilityConfigDto>(json, Settings);
        }

        public static AbilityConfig FromDto(AbilityConfigDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            AbilityConfig cfg = ScriptableObject.CreateInstance<AbilityConfig>();
            cfg.name = string.IsNullOrEmpty(dto.abilityId) ? "generated_ability" : dto.abilityId;
            cfg.abilityId = dto.abilityId;
            cfg.abilityName = dto.abilityName;
            cfg.description = dto.description;
            cfg.sp = dto.sp ?? new SPConfig();
            cfg.rules = dto.rules ?? Array.Empty<AbilityRuleConfig>();
            cfg.icon = AbilityIconPool.Resolve(dto.iconKey);
            return cfg;
        }
    }
}
