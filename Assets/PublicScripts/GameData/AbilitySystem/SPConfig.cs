using System;
using UnityEngine;

namespace AbilitySystem
{
    public enum SpRecoverMode { Natural, OnAttackSuccessfully, OnAfterHurt, Other }
    public enum SpConsumeMode { Natural, OnAttackSuccessfully, OnAfterHurt, Instant, Other, NoConsume }
    public enum AbilityOpenMode { Auto, OnAttackAnimBegin, OnBeforeHurt, Manual, Other, OnDeadlyHurt }

    [Serializable]
    public class SPConfig
    {
        [Tooltip("Total SP. Ability fires when current SP >= totalSp.")]
        public int totalSp;
        public int initialSp;
        [Tooltip("Max charges. >1 enables multi-charge behavior.")]
        public int chargeNum = 1;
        [Tooltip(">0 = active for that long after fire; <=0 = instant fire.")]
        public float abilityAmount;
        public SpRecoverMode recoverMode = SpRecoverMode.Natural;
        public SpConsumeMode consumeMode = SpConsumeMode.Natural;
        public AbilityOpenMode openMode = AbilityOpenMode.Auto;
        public bool recoverForbidDuringAbility;
        public bool canManualClose;
    }
}
