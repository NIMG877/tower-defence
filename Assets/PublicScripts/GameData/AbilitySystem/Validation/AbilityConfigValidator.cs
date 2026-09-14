using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

namespace AbilitySystem
{
    /// <summary>
    /// 客户端配置校验器：生成技能注入运行时前的最后一道防漂移门。读 ability-ops.json
    /// 对 DTO 做结构与词表校验，并产出清理后的副本（丢弃未知参数键、钳制越界数值）。
    ///
    /// Error（整技能拒绝）：未知 op、空 rules、规则无 triggers——这些配置在运行时
    /// 不可能产生任何效果；运行时对未知 op 只 warn+跳过（无声失效），这里升级为拒绝。
    /// Warning（仅记录）：未知参数键（丢弃该 entry，防止静默死配置）、字面量不可解析
    /// （保留原值，运行时 getter 会按默认值兜底）、词表外 token、读键无生产者、
    /// 死配置（component op 上的 condition / 非 branch 的 elseSteps）。
    ///
    /// 与服务端校验（重试闭环）互补：这里防的是 schema 版本漂移与绕过服务端的
    /// 注入路径；黑板键对称性是静态近似（动态键合法存在），因此只 warn 不拒。
    /// </summary>
    public static class AbilityConfigValidator
    {
        public sealed class Issue
        {
            public bool IsError;
            public string Path;
            public string Message;

            public override string ToString() =>
                $"{(IsError ? "error" : "warning")} at {Path}: {Message}";
        }

        public sealed class Result
        {
            public readonly List<Issue> Issues = new List<Issue>();
            public AbilityConfigDto Sanitized;
            private int _errorCount;

            public bool Ok => _errorCount == 0;

            internal void Add(bool isError, string path, string message)
            {
                if (isError) _errorCount++;
                Issues.Add(new Issue { IsError = isError, Path = path, Message = message });
            }
        }

        public static Result Validate(AbilityConfigDto dto, HostAssets hostAssets = null)
        {
            AbilityOpsSchema schema = AbilityOpsSchema.Load();
            var result = new Result();
            var ctx = new WalkContext(schema, result);

            if (dto == null)
            {
                result.Add(true, "", "dto is null");
                return result;
            }

            if (dto.rules == null || dto.rules.Length == 0)
                result.Add(true, "rules", "no rules; the ability would never execute");
            var sanitized = new AbilityConfigDto
            {
                abilityId = dto.abilityId,
                abilityName = dto.abilityName,
                description = dto.description,
                iconKey = dto.iconKey,
                sp = dto.sp == null ? null : SanitizeSp(dto.sp, ctx),
                rules = SanitizeRules(dto.rules, ctx),
            };
            // hostAssets 边界终检（与服务端同款语义）：引用只能指向宿主实际持有的
            // 资产，越界在注入前拒绝。hostAssets 为 null 时跳过（探针/旧调用点）。
            CheckHostAssetBounds(dto, hostAssets, ctx);
            CheckBlackboardSymmetry(ctx);
            result.Sanitized = sanitized;
            return result;
        }

        // ========= hostAssets 边界终检（与服务端 P0-2 对齐，error 级） =========

        private static void CheckHostAssetBounds(AbilityConfigDto dto,
            HostAssets host, WalkContext ctx)
        {
            if (host == null) return;
            int spawnCount = host.canSpawnEntities?.Count ?? 0;
            int bulletCount = host.bullets?.Count ?? 0;
            var animationNames = new HashSet<string>(StringComparer.Ordinal);
            if (host.animations != null)
            {
                CollectNames(host.animations.named, animationNames);
                CollectNames(host.animations.groups, animationNames);
            }
            if (dto.rules == null) return;
            foreach (AbilityRuleConfig rule in dto.rules)
            {
                WalkHostSteps(rule?.steps, spawnCount, bulletCount, animationNames, ctx);
            }
        }

        private static void CollectNames(List<string> names, HashSet<string> into)
        {
            if (names == null) return;
            foreach (string name in names)
            {
                if (!string.IsNullOrEmpty(name)) into.Add(name);
            }
        }

        private static void WalkHostSteps(StepConfig[] steps, int spawnCount, int bulletCount,
            HashSet<string> animationNames, WalkContext ctx)
        {
            if (steps == null) return;
            foreach (StepConfig step in steps)
            {
                if (step == null) continue;
                CheckStepHostBounds(step, spawnCount, bulletCount, animationNames, ctx);
                WalkHostSteps(step.steps, spawnCount, bulletCount, animationNames, ctx);
                WalkHostSteps(step.elseSteps, spawnCount, bulletCount, animationNames, ctx);
            }
        }

        private static void CheckStepHostBounds(StepConfig step, int spawnCount, int bulletCount,
            HashSet<string> animationNames, WalkContext ctx)
        {
            if (string.IsNullOrEmpty(step.op) || step.args?.entries == null) return;
            // DTO 的 op 可能是别名（探针/手写配置），去下划线小写归一后比对。
            string op = step.op.Replace("_", "").ToLowerInvariant();

            if (op == "spawnentity" || op == "firebullets")
            {
                bool isSpawn = op == "spawnentity";
                string param = isSpawn ? "spawnIndex" : "bulletDataIndex";
                string label = isSpawn ? "spawn_entity.spawnIndex" : "fire_bullets.bulletDataIndex";
                int count = isSpawn ? spawnCount : bulletCount;
                ParamEntry entry = FindEntry(step.args, param);
                if (entry == null || entry.fromBlackboard || string.IsNullOrEmpty(entry.value)) return;
                if (int.TryParse(entry.value.Trim(), out int index) && (index < 0 || index >= count))
                {
                    ctx.Result.Add(true, label,
                        $"{label}={index} out of range: hostAssets has {count} entries");
                }
            }
            else if (op == "applyanimationoverride" && animationNames.Count > 0)
            {
                ParamEntry entry = FindEntry(step.args, "resources");
                if (entry == null || entry.fromBlackboard || string.IsNullOrEmpty(entry.value)) return;
                foreach (string token in entry.value.Split(','))
                {
                    string name = token.Trim();
                    if (name.Length > 0 && !animationNames.Contains(name))
                    {
                        ctx.Result.Add(true, "apply_animation_override.resources",
                            $"animation resource '{name}' is not in hostAssets.animations (named ∪ groups)");
                    }
                }
            }
        }

        private static ParamEntry FindEntry(ParamList args, string key)
        {
            foreach (ParamEntry entry in args.entries)
            {
                if (entry != null && entry.key == key) return entry;
            }
            return null;
        }

        // ========= 内部遍历 =========

        private sealed class WalkContext
        {
            public readonly AbilityOpsSchema Schema;
            public readonly Result Result;
            public readonly HashSet<string> Reads = new HashSet<string>();
            public readonly HashSet<string> Writes = new HashSet<string>();
            public readonly HashSet<string> FixedWrites = new HashSet<string>();

            public WalkContext(AbilityOpsSchema schema, Result result)
            {
                Schema = schema;
                Result = result;
            }
        }

        private static AbilityRuleConfig[] SanitizeRules(AbilityRuleConfig[] rules, WalkContext ctx)
        {
            if (rules == null || rules.Length == 0) return Array.Empty<AbilityRuleConfig>();
            var output = new List<AbilityRuleConfig>(rules.Length);
            for (int i = 0; i < rules.Length; i++)
            {
                AbilityRuleConfig rule = rules[i];
                string path = $"rules[{i}]";
                if (rule == null)
                {
                    ctx.Result.Add(true, path, "null rule dropped");
                    continue;
                }
                if (rule.triggers == null || rule.triggers.Length == 0)
                    ctx.Result.Add(true, path, "rule has no triggers; it would never execute");
                output.Add(new AbilityRuleConfig
                {
                    triggers = CloneTriggers(rule.triggers),
                    reentry = rule.reentry,
                    detached = rule.detached,
                    steps = SanitizeSteps(rule.steps, $"{path}.steps", ctx),
                });
            }
            return output.ToArray();
        }

        private static StepConfig[] SanitizeSteps(StepConfig[] steps, string collectionPath, WalkContext ctx)
        {
            if (steps == null || steps.Length == 0) return Array.Empty<StepConfig>();
            var output = new List<StepConfig>(steps.Length);
            for (int i = 0; i < steps.Length; i++)
            {
                StepConfig step = steps[i];
                string path = $"{collectionPath}[{i}]";
                if (step == null)
                {
                    ctx.Result.Add(false, path, "null step dropped");
                    continue;
                }
                output.Add(SanitizeStep(step, path, ctx));
            }
            return output.ToArray();
        }

        private static StepConfig SanitizeStep(StepConfig step, string path, WalkContext ctx)
        {
            if (!ctx.Schema.TryResolveOp(step.op, out OpSchema opSchema))
            {
                ctx.Result.Add(true, path, $"unknown op '{step.op ?? "<null>"}' (not registered in ability-ops.json)");
                return CloneStepUnresolved(step);
            }

            var clone = new StepConfig { op = opSchema.CanonicalOp };
            clone.args = SanitizeArgs(step.args, opSchema, path, ctx);
            foreach (string fixedWrite in opSchema.FixedWrites) ctx.FixedWrites.Add(fixedWrite);

            if (step.condition != null && step.condition.Count > 0 && !opSchema.UsesCondition)
                ctx.Result.Add(false, path,
                    $"condition ignored on op '{step.op}' (only wait_until/branch/loop consume it)");
            clone.condition = CloneConditions(step.condition, ctx);

            if (step.elseSteps != null && step.elseSteps.Length > 0 && !opSchema.HasElse)
                ctx.Result.Add(false, path, $"elseSteps ignored on op '{step.op}' (only branch consumes them)");
            clone.steps = SanitizeSteps(step.steps, $"{path}.steps", ctx);
            clone.elseSteps = SanitizeSteps(step.elseSteps, $"{path}.elseSteps", ctx);
            return clone;
        }

        private static ParamList SanitizeArgs(ParamList args, OpSchema opSchema, string path, WalkContext ctx)
        {
            var output = new ParamList { entries = Array.Empty<ParamEntry>() };
            if (args?.entries == null || args.entries.Length == 0) return output;

            var entries = new List<ParamEntry>(args.entries.Length);
            for (int i = 0; i < args.entries.Length; i++)
            {
                ParamEntry entry = args.entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.key)) continue;

                ParamSchema param = opSchema.GetParam(entry.key);
                if (param == null)
                {
                    ctx.Result.Add(false, path,
                        $"unknown param key '{entry.key}' on op '{opSchema.CanonicalOp}'; entry dropped");
                    continue;
                }

                var clone = new ParamEntry
                {
                    key = entry.key,
                    value = entry.value,
                    fromBlackboard = entry.fromBlackboard,
                    type = entry.type,
                };
                ValidateEntryValue(clone, param, opSchema.CanonicalOp, path, ctx);
                CollectBlackboardRole(clone, param, ctx);
                entries.Add(clone);
            }
            output.entries = entries.ToArray();
            return output;
        }

        private static void ValidateEntryValue(ParamEntry entry, ParamSchema param, string canonicalOp, string path, WalkContext ctx)
        {
            string raw = entry.value ?? "";
            // fromBlackboard=true 时 value 是黑板键名而非字面量，不做字面量解析。
            if (entry.fromBlackboard || raw.Length == 0) return;

            switch (param.type)
            {
                case "int":
                    if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        WarnUnparsable(ctx, path, canonicalOp, entry.key, raw, "int");
                    break;
                case "float":
                    if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                        ClampEntry(entry, $"{canonicalOp}.{entry.key}", value, ctx);
                    else
                        WarnUnparsable(ctx, path, canonicalOp, entry.key, raw, "float");
                    break;
                case "bool":
                    if (!bool.TryParse(raw, out _))
                        WarnUnparsable(ctx, path, canonicalOp, entry.key, raw, "bool");
                    break;
                case "vector2int":
                    if (CsvTokenCount(raw) != 2 || !int.TryParse(raw.Split(',')[0].Trim(), out _) ||
                        !int.TryParse(raw.Split(',')[1].Trim(), out _))
                        WarnUnparsable(ctx, path, canonicalOp, entry.key, raw, "vector2int \"x,y\"");
                    break;
                case "vector2intArray":
                    if (!TryParseVector2IntArray(raw))
                        WarnUnparsable(ctx, path, canonicalOp, entry.key, raw, "vector2int array \"[[x,y],...]\"");
                    break;
                case "floatArray":
                    CheckCsvTokens(ctx, path, canonicalOp, entry.key, raw,
                        token => float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _), "float");
                    break;
                case "intArray":
                    CheckCsvTokens(ctx, path, canonicalOp, entry.key, raw,
                        token => int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out _), "int");
                    break;
                case "stringArray":
                    if (param.values != null)
                        CheckCsvTokens(ctx, path, canonicalOp, entry.key, raw,
                            token => InValues(param.values, token), $"token of [{string.Join("/", param.values)}]");
                    break;
                case "string":
                    if (param.values != null && !InValues(param.values, raw))
                        ctx.Result.Add(false, path,
                            $"value '{raw}' on '{canonicalOp}.{entry.key}' is outside the accepted tokens; " +
                            $"expected one of [{string.Join("/", param.values)}]");
                    break;
                case "any":
                    break;
                default:
                    // schema 演进出的新类型标签：宽松跳过（运行时按字符串存取）。
                    break;
            }
        }

        private static void ClampEntry(ParamEntry entry, string clampKey, float value, WalkContext ctx)
        {
            if (!ctx.Schema.TryGetClamp(clampKey, out float min, out float max)) return;
            if (value >= min && value <= max) return;
            float clamped = Math.Min(max, Math.Max(min, value));
            entry.value = clamped.ToString("R", CultureInfo.InvariantCulture);
            ctx.Result.Add(false, clampKey, $"value {value.ToString("R", CultureInfo.InvariantCulture)} clamped to [{min}, {max}] -> {entry.value}");
        }

        private static SPConfig SanitizeSp(SPConfig sp, WalkContext ctx)
        {
            var clone = new SPConfig
            {
                totalSp = sp.totalSp,
                initialSp = sp.initialSp,
                chargeNum = sp.chargeNum,
                abilityAmount = sp.abilityAmount,
                recoverMode = sp.recoverMode,
                consumeMode = sp.consumeMode,
                openMode = sp.openMode,
                recoverForbidDuringAbility = sp.recoverForbidDuringAbility,
                canManualClose = sp.canManualClose,
            };
            ClampInt(clone, "sp.totalSp", clone.totalSp, ctx);
            ClampInt(clone, "sp.initialSp", clone.initialSp, ctx);
            ClampInt(clone, "sp.chargeNum", clone.chargeNum, ctx);
            ClampFloat(clone, "sp.abilityAmount", clone.abilityAmount, ctx);
            return clone;
        }

        private static void ClampInt(SPConfig sp, string clampKey, int value, WalkContext ctx)
        {
            if (!ctx.Schema.TryGetClamp(clampKey, out float min, out float max)) return;
            if (value >= min && value <= max) return;
            int clamped = (int)Math.Min(max, Math.Max(min, value));
            if (clampKey == "sp.totalSp") sp.totalSp = clamped;
            else if (clampKey == "sp.initialSp") sp.initialSp = clamped;
            else if (clampKey == "sp.chargeNum") sp.chargeNum = clamped;
            ctx.Result.Add(false, clampKey, $"value {value} clamped to [{min}, {max}] -> {clamped}");
        }

        private static void ClampFloat(SPConfig sp, string clampKey, float value, WalkContext ctx)
        {
            if (!ctx.Schema.TryGetClamp(clampKey, out float min, out float max)) return;
            if (value >= min && value <= max) return;
            float clamped = Math.Min(max, Math.Max(min, value));
            if (clampKey == "sp.abilityAmount") sp.abilityAmount = clamped;
            ctx.Result.Add(false, clampKey, $"value {value.ToString("R", CultureInfo.InvariantCulture)} clamped to [{min}, {max}] -> {clamped.ToString("R", CultureInfo.InvariantCulture)}");
        }

        private static void CollectBlackboardRole(ParamEntry entry, ParamSchema param, WalkContext ctx)
        {
            if (param.bbRole == "readKey" && !string.IsNullOrEmpty(entry.value)) ctx.Reads.Add(entry.value);
            else if (param.bbRole == "writeKey" && !string.IsNullOrEmpty(entry.value)) ctx.Writes.Add(entry.value);
        }

        private static void CheckBlackboardSymmetry(WalkContext ctx)
        {
            foreach (string key in ctx.Reads)
            {
                if (ctx.Writes.Contains(key)) continue;
                if (Array.IndexOf(ctx.Schema.knownBlackboardKeys, key) >= 0) continue;
                if (ctx.FixedWrites.Contains(key)) continue;
                ctx.Result.Add(false, "blackboard",
                    $"read key '{key}' has no producer in this ability (externally-fed keys are legitimate; verify intent)");
            }
        }

        // ========= 克隆 helpers =========

        private static StepConfig CloneStepUnresolved(StepConfig step)
        {
            return new StepConfig
            {
                op = step.op,
                args = CloneArgs(step.args),
                condition = CloneConditions(step.condition, null),
                steps = CloneStepsUnresolved(step.steps),
                elseSteps = CloneStepsUnresolved(step.elseSteps),
            };
        }

        private static StepConfig[] CloneStepsUnresolved(StepConfig[] steps)
        {
            if (steps == null || steps.Length == 0) return Array.Empty<StepConfig>();
            var output = new List<StepConfig>(steps.Length);
            for (int i = 0; i < steps.Length; i++)
                if (steps[i] != null) output.Add(CloneStepUnresolved(steps[i]));
            return output.ToArray();
        }

        private static ParamList CloneArgs(ParamList args)
        {
            var clone = new ParamList { entries = Array.Empty<ParamEntry>() };
            if (args?.entries == null || args.entries.Length == 0) return clone;
            var entries = new List<ParamEntry>(args.entries.Length);
            for (int i = 0; i < args.entries.Length; i++)
            {
                ParamEntry entry = args.entries[i];
                if (entry == null) continue;
                entries.Add(new ParamEntry
                {
                    key = entry.key,
                    value = entry.value,
                    fromBlackboard = entry.fromBlackboard,
                    type = entry.type,
                });
            }
            clone.entries = entries.ToArray();
            return clone;
        }

        private static ConditionConfig[] CloneTriggers(ConditionConfig[] triggers)
        {
            if (triggers == null || triggers.Length == 0) return Array.Empty<ConditionConfig>();
            var output = new List<ConditionConfig>(triggers.Length);
            for (int i = 0; i < triggers.Length; i++)
            {
                ConditionConfig trigger = triggers[i];
                if (trigger == null) continue;
                output.Add(new ConditionConfig
                {
                    triggerEvent = trigger.triggerEvent,
                    groups = CloneConditionGroups(trigger.groups),
                });
            }
            return output.ToArray();
        }

        private static List<ConditionGroup> CloneConditions(List<ConditionGroup> groups, WalkContext ctx)
        {
            List<ConditionGroup> clone = CloneConditionGroups(groups);
            if (ctx != null && groups != null)
            {
                for (int g = 0; g < groups.Count; g++)
                {
                    ConditionGroup group = groups[g];
                    if (group?.units == null) continue;
                    for (int u = 0; u < group.units.Count; u++)
                    {
                        ConditionUnit unit = group.units[u];
                        if (unit == null) continue;
                        if (!string.IsNullOrEmpty(unit.leftKey)) ctx.Reads.Add(unit.leftKey);
                        if (unit.op == ConditionOp.KeyEqual || unit.op == ConditionOp.KeyNotEqual)
                        {
                            if (!string.IsNullOrEmpty(unit.rightKey)) ctx.Reads.Add(unit.rightKey);
                        }
                    }
                }
            }
            return clone;
        }

        private static List<ConditionGroup> CloneConditionGroups(List<ConditionGroup> groups)
        {
            if (groups == null) return new List<ConditionGroup>();
            var output = new List<ConditionGroup>(groups.Count);
            for (int g = 0; g < groups.Count; g++)
            {
                ConditionGroup group = groups[g];
                if (group == null) continue;
                var cloneGroup = new ConditionGroup();
                if (group.units != null)
                {
                    for (int u = 0; u < group.units.Count; u++)
                    {
                        ConditionUnit unit = group.units[u];
                        if (unit == null) continue;
                        cloneGroup.units.Add(new ConditionUnit
                        {
                            op = unit.op,
                            leftKey = unit.leftKey,
                            rightValue = unit.rightValue,
                            rightKey = unit.rightKey,
                        });
                    }
                }
                output.Add(cloneGroup);
            }
            return output;
        }

        // ========= 解析 helpers =========

        private static void WarnUnparsable(WalkContext ctx, string path, string canonicalOp, string key, string raw, string expected)
        {
            ctx.Result.Add(false, path,
                $"value '{raw}' on '{canonicalOp}.{key}' does not parse as {expected}; runtime will fall back to the parameter default");
        }

        private static void CheckCsvTokens(WalkContext ctx, string path, string canonicalOp, string key, string raw,
            Func<string, bool> tokenCheck, string expected)
        {
            string[] tokens = raw.Split(',');
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim();
                if (token.Length == 0 || !tokenCheck(token))
                {
                    ctx.Result.Add(false, path,
                        $"CSV token '{tokens[i]}' on '{canonicalOp}.{key}' does not parse as {expected}");
                }
            }
        }

        private static bool InValues(string[] values, string token)
        {
            for (int i = 0; i < values.Length; i++)
                if (string.Equals(values[i], token.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static int CsvTokenCount(string raw)
        {
            string[] tokens = raw.Split(',');
            return tokens.Length;
        }

        // 与 ParamList.ParseVector2IntArray 同语义的宽松探测：JSON int[][]，
        // 反序列化失败即视为畸形（运行时会得到空数组，等效 no-op）。
        private static bool TryParseVector2IntArray(string raw)
        {
            try
            {
                JsonConvert.DeserializeObject<int[][]>(raw);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
