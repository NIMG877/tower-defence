using UnityEngine;

namespace AbilitySystem.Components
{
    [RegisterComponent("FlashMove")]
    public class FlashMoveComponent : IAbilityComponent
    {
        private float _moveDis;

        public void OnInit(AbilityContext ctx, ParamList p)
        {
            _moveDis = p.GetFloat("moveDis", 0f);
        }

        public void OnTrigger(AbilityContext ctx)
        {
            if (ctx.entity == null || ctx.entity.MoveBase == null) return;
            var move = ctx.entity.MoveBase;
            var mp = move.CurrentSection;
            if (mp == null || mp.Length == 0) return;
            int path = move.CurrentPathSerial;
            int sec = move.CurrentSectionSerial;
            int pt = move.CurrentPointSerial;
            Vector2 pos = ctx.entity.EntityPosition;
            float currentDis = 0;
            for (int i = pt; i < mp.Length; i++)
            {
                currentDis += Vector2.Distance(pos, mp[i].targetPosition);
                if (currentDis < _moveDis)
                {
                    pos = mp[i].targetPosition;
                }
                else
                {
                    ctx.entity.EntityPosition = mp[i].targetPosition + (currentDis - _moveDis) * (pos - mp[i].targetPosition).normalized;
                    move.SetMoveParameters(path, sec, i);
                    return;
                }
            }
            ctx.entity.EntityPosition = mp[mp.Length - 1].targetPosition;
            move.SetMoveParameters(path, sec, 0);
        }

        public void OnTick(AbilityContext ctx, float dt) { }
        public void OnTeardown(AbilityContext ctx) { }
    }
}
