using System;
using UnityEngine;

namespace SkillSystem
{
    public enum SpRecoverMode { Natural, OnAttackHit, OnAfterHurt }
    public enum SpConsumeMode { Natural, OnAttackHit, OnAfterHurt, Instant }
    public enum SkillOpenMode { Auto, OnAttackAnimBegin, OnBeforeHurt, Manual, OnAttackHit }

    [Serializable]
    public class SPConfig
    {
        [Tooltip("Total SP. Skill fires when current SP >= totalSp.")]
        public int totalSp;
        public int initialSp;
        [Tooltip("Max charges. >1 enables multi-charge behavior.")]
        public int chargeNum = 1;
        [Tooltip(">0 = active for that long after fire; <=0 = instant fire.")]
        public float skillDuration;
        public SpRecoverMode recoverMode = SpRecoverMode.Natural;
        public SpConsumeMode consumeMode = SpConsumeMode.Natural;
        public SkillOpenMode openMode = SkillOpenMode.Auto;
        public bool recoverForbidDuringSkill;
        public bool canManualClose;
    }
}
