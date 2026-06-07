using Cysharp.Threading.Tasks;
using UnityEngine;

namespace SkillSystem.Components
{
    [RegisterComponent("DeathSpawn")]
    public class DeathSpawnComponent : ISkillComponent
    {
        private EntityID _entityId;
        private int _num = 1;
        private float _gap = 0.1f;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            // ParamList carries a single CSV "c,n" string; split and construct the (category, number) tuple.
            var raw = p.GetString("entityId", "");
            if (!string.IsNullOrEmpty(raw))
            {
                var parts = raw.Split(',');
                if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out var n))
                    _entityId = new EntityID(parts[0].Trim(), n);
            }
            _num = p.GetInt("num", 1);
            _gap = p.GetFloat("gap", 0.1f);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (!(ctx.currentEvent is BeforeDieAnimationEvent)) return;
            if (ctx.entity == null) return;
            _ = SpawnAsync(ctx);
        }

        private async UniTask SpawnAsync(SkillContext ctx)
        {
            Vector2 thisP = new Vector2((int)(ctx.entity.transform.position.x + 0.5f), (int)(ctx.entity.transform.position.y + 0.5f));
            for (int i = 0; i < _num; i++)
            {
                Vector2 offset = new Vector2(Random.Range(-0.24f, 0.24f), Random.Range(-0.24f, 0.24f));
                var spawned = EntityManager.Manager.SetMovableEntity(_entityId, offset + thisP, ctx.entity.Camp, ctx.entity.MoveBase.CurrentPathSerial);
                spawned?.MoveBase.SetMoveParameters(ctx.entity.MoveBase.CurrentPathSerial, ctx.entity.MoveBase.CurrentSectionSerial, 0);
                await UniTask.WaitForSeconds(_gap);
            }
        }

        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }
}
