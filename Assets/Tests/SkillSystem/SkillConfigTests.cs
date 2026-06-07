using NUnit.Framework;
using SkillSystem;
using UnityEngine;

public class SkillConfigTests
{
    [Test]
    public void SkillConfig_RoundTripsThroughSerialization()
    {
        var cfg = new SkillConfig
        {
            skillId = "zombie_angry",
            skillName = "狂暴",
            description = "受到致命伤后狂暴",
            kind = SkillKind.OnDeath,
            sp = new SPConfig { totalSp = 100, initialSp = 50, chargeNum = 1, skillDuration = -1f, recoverMode = SpRecoverMode.Natural, consumeMode = SpConsumeMode.Instant, openMode = SkillOpenMode.Natural, recoverForbidDuringSkill = false, canManualClose = false, skillAttackRange = new Vector2Int[0] },
            globalConditions = new ConditionConfig[0],
            components = new[] {
                new ComponentConfig { componentType = "ApplyBuff", parameters = new ParamList { entries = new[] { new ParamEntry { key = "buffType", type = ParamValueType.String, value = "atk" } } } }
            }
        };
        var json = JsonUtility.ToJson(cfg);
        var roundTripped = JsonUtility.FromJson<SkillConfig>(json);
        Assert.AreEqual("zombie_angry", roundTripped.skillId);
        Assert.AreEqual(100, roundTripped.sp.totalSp);
        Assert.AreEqual("ApplyBuff", roundTripped.components[0].componentType);
    }

    [Test]
    public void SPConfig_Defaults_AreSafe()
    {
        var sp = new SPConfig();
        Assert.AreEqual(0, sp.totalSp);
        Assert.AreEqual(1, sp.chargeNum);
        Assert.AreEqual(SpRecoverMode.Natural, sp.recoverMode);
    }
}
