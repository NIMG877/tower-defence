using System;

namespace SkillSystem.Components
{
    [RegisterComponent("ApplyBuff")]
    public class ApplyBuffComponent : ISkillComponent
    {
        private string _buffTypesRaw = "";
        private string _buffValuesRaw = "";
        private string _buffId = "skill_buff";
        private float _priority = -10f;
        private bool _toSelf = true;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _buffTypesRaw = p.GetString("buffTypes", "");
            _buffValuesRaw = p.GetString("buffValues", "");
            _buffId = p.GetString("buffId", "skill_buff");
            _priority = p.GetFloat("priority", -10f);
            _toSelf = p.GetBool("toSelf", true);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (ctx.entity == null) return;
            var types = ParseEnums(_buffTypesRaw);
            var values = ParseFloats(_buffValuesRaw);
            var target = _toSelf ? ctx.entity : (ctx.currentEvent is BeforeTakeDamageEvent btd ? btd.target : null);
            if (target == null || target.buffController == null) return;
            target.buffController.CreateBuff(types, null, _buffId, values, _priority, true);
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }

        private static BuffType[] ParseEnums(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return Array.Empty<BuffType>();
            var parts = csv.Split(',');
            var arr = new BuffType[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                arr[i] = (BuffType)Enum.Parse(typeof(BuffType), parts[i].Trim());
            return arr;
        }
        private static float[] ParseFloats(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return Array.Empty<float>();
            var parts = csv.Split(',');
            var arr = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                arr[i] = float.Parse(parts[i].Trim());
            return arr;
        }
    }
}
