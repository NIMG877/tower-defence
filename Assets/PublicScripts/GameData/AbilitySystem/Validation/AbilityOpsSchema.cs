using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace AbilitySystem
{
    /// <summary>
    /// ability-ops.json 的加载与查询。schema 是双端单一数据源：客户端校验器
    /// （<see cref="AbilityConfigValidator"/>）、服务端提示词组装与全量校验都读仓库里
    /// 同一份 Resources/Data/AbilityOps/ability-ops.json。本加载器只声明校验所需的
    /// 字段，ruleShape/triggerEvents/paramValueTypeEncoding 等提示词专用段由服务端直读。
    /// </summary>
    public sealed class AbilityOpsSchema
    {
        private const string ResourcePath = "Data/AbilityOps/ability-ops";

        private static AbilityOpsSchema _cached;

        public int protocolVersion;
        public string[] knownBlackboardKeys = Array.Empty<string>();
        public Dictionary<string, PrimitiveOpSchema> primitives = new Dictionary<string, PrimitiveOpSchema>();
        public Dictionary<string, ComponentOpSchema> componentOps = new Dictionary<string, ComponentOpSchema>();
        public Dictionary<string, float[]> clamps = new Dictionary<string, float[]>();

        private Dictionary<string, OpSchema> _opLookup;

        public static AbilityOpsSchema Load()
        {
            if (_cached != null) return _cached;
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
                throw new InvalidOperationException(
                    $"AbilityOps schema not found at Resources/{ResourcePath}.json; cannot validate generated abilities.");
            AbilityOpsSchema schema = JsonConvert.DeserializeObject<AbilityOpsSchema>(asset.text);
            if (schema == null)
                throw new InvalidOperationException($"AbilityOps schema at Resources/{ResourcePath}.json deserialized to null.");
            schema.BuildLookup();
            _cached = schema;
            return schema;
        }

        public static void ResetForTests() => _cached = null;

        /// <summary>op 名 → 统一视图：原语按 canonical 名，组件 op 接受 canonical 与
        /// PascalCase 别名（镜像运行时注册表行为）。schema 内名字冲突直接抛异常——
        /// 静默 last-writer-wins 会让校验器与运行时注册表分叉。</summary>
        public bool TryResolveOp(string op, out OpSchema schema)
        {
            schema = null;
            if (string.IsNullOrEmpty(op)) return false;
            return _opLookup.TryGetValue(op, out schema);
        }

        public bool TryGetClamp(string key, out float min, out float max)
        {
            min = max = 0f;
            if (!clamps.TryGetValue(key, out float[] range) || range == null || range.Length < 2) return false;
            min = range[0];
            max = range[1];
            return true;
        }

        private void BuildLookup()
        {
            _opLookup = new Dictionary<string, OpSchema>(StringComparer.Ordinal);
            if (primitives != null)
            {
                foreach (KeyValuePair<string, PrimitiveOpSchema> pair in primitives)
                {
                    if (pair.Value == null) continue;
                    RegisterLookup(_opLookup, pair.Value.ToOpSchema(pair.Key), new[] { pair.Key });
                }
            }
            if (componentOps != null)
            {
                foreach (KeyValuePair<string, ComponentOpSchema> pair in componentOps)
                {
                    if (pair.Value == null) continue;
                    // 运行时注册表对 component op 同时登记 canonical 与 PascalCase 别名
                    // （RegisterComponentAdapter 语义），这里必须对齐：只登记别名会让
                    // canonical 名全部 miss。
                    var names = new List<string>(1 + (pair.Value.aliases?.Length ?? 0)) { pair.Key };
                    names.AddRange(pair.Value.AliasNames());
                    RegisterLookup(_opLookup, pair.Value.ToOpSchema(pair.Key), names.ToArray());
                }
            }
        }

        private static void RegisterLookup(Dictionary<string, OpSchema> lookup, OpSchema schema, string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (string.IsNullOrEmpty(names[i])) continue;
                if (lookup.TryGetValue(names[i], out OpSchema existing) && !ReferenceEquals(existing, schema))
                    throw new InvalidOperationException(
                        $"ability-ops.json: op name '{names[i]}' is claimed by both '{existing.CanonicalOp}' and '{schema.CanonicalOp}'.");
                lookup[names[i]] = schema;
            }
        }
    }

    /// <summary>校验视角的统一 op 视图（原语与 component op 共用）。</summary>
    public sealed class OpSchema
    {
        public string CanonicalOp;
        public string ClassName;     // 组件 op 的类名；原语为 null
        public string Doc;
        public bool UsesCondition;
        public bool HasElse;
        public string[] FixedWrites = Array.Empty<string>();
        public string[] Aliases = Array.Empty<string>();
        public Dictionary<string, ParamSchema> ParamsByKey = new Dictionary<string, ParamSchema>(StringComparer.Ordinal);

        public ParamSchema GetParam(string key)
        {
            ParamsByKey.TryGetValue(key, out ParamSchema param);
            return param;
        }
    }

    public sealed class PrimitiveOpSchema
    {
        public bool usesCondition;
        public bool hasElse;
        [JsonProperty("params")] public ParamSchema[] params_;

        public OpSchema ToOpSchema(string canonical)
        {
            return new OpSchema
            {
                CanonicalOp = canonical,
                UsesCondition = usesCondition,
                HasElse = hasElse,
                ParamsByKey = AbilityOpsSchemaUtil.BuildParams(params_),
            };
        }
    }

    public sealed class ComponentOpSchema
    {
        [JsonProperty("class")] public string className;
        public string[] aliases;
        public string doc;
        public string[] fixedWrites;
        public string notes;
        [JsonProperty("params")] public ParamSchema[] params_;

        public string[] AliasNames()
        {
            string[] names = aliases;
            if (names == null || names.Length == 0) names = className != null ? new[] { className } : Array.Empty<string>();
            return names;
        }

        public OpSchema ToOpSchema(string canonical)
        {
            return new OpSchema
            {
                CanonicalOp = canonical,
                ClassName = className,
                Doc = doc,
                FixedWrites = fixedWrites ?? Array.Empty<string>(),
                Aliases = AliasNames(),
                ParamsByKey = AbilityOpsSchemaUtil.BuildParams(params_),
            };
        }
    }

    public sealed class ParamSchema
    {
        public string key;
        public string type;
        [JsonProperty("default")] public object DefaultValue;
        public bool fromBlackboard;
        public string[] values;
        public string valueEnum;
        public string bbRole;
        public string desc;
    }

    internal static class AbilityOpsSchemaUtil
    {
        public static Dictionary<string, ParamSchema> BuildParams(ParamSchema[] paramArray)
        {
            var byKey = new Dictionary<string, ParamSchema>(StringComparer.Ordinal);
            if (paramArray == null) return byKey;
            for (int i = 0; i < paramArray.Length; i++)
            {
                ParamSchema param = paramArray[i];
                if (param == null || string.IsNullOrEmpty(param.key)) continue;
                byKey.Add(param.key, param);
            }
            return byKey;
        }
    }
}
