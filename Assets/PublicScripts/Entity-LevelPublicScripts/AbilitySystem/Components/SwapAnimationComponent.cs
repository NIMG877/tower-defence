using Spine.Unity;

namespace AbilitySystem.Components
{
    [RegisterComponent("SwapAnimation")]
    public class SwapAnimationComponent : IAbilityComponent
    {
        // All fields are AnimationReferenceAsset, populated via the migration tool in Phase 5/6.
        // Per-flag presence lets a skill swap just one of the six without resetting the rest.
        private AnimationReferenceAsset _idle, _move, _attackClose, _attackRemote, _start, _die;
        public bool HasIdle => _idle != null;
        public bool HasMove => _move != null;
        public bool HasAttackClose => _attackClose != null;
        public bool HasAttackRemote => _attackRemote != null;
        public bool HasStart => _start != null;
        public bool HasDie => _die != null;

        public void SetIdle(AnimationReferenceAsset a) { _idle = a; }
        public void SetMove(AnimationReferenceAsset a) { _move = a; }
        public void SetAttackClose(AnimationReferenceAsset a) { _attackClose = a; }
        public void SetAttackRemote(AnimationReferenceAsset a) { _attackRemote = a; }
        public void SetStart(AnimationReferenceAsset a) { _start = a; }
        public void SetDie(AnimationReferenceAsset a) { _die = a; }

        public void OnInit(AbilityContext ctx, ParamList p)
        {
            // For now, we accept GameObject prefab names; resource loading is handled by editor-side validation.
            // Concrete assets are assigned via the migration tool in Phase 5/6.
        }

        public void OnTrigger(AbilityContext ctx)
        {
            if (ctx.entity == null || ctx.entity.entityAM == null) return;
            var am = ctx.entity.entityAM;
            if (_idle != null) am.Idle = _idle;
            if (_move != null) am.Move = _move;
            if (_attackClose != null) am.Attack_Close = new[] { _attackClose };
            if (_attackRemote != null) am.Attack_Remote = new[] { _attackRemote };
            if (_start != null) am.Start = _start;
            if (_die != null) am.Die = _die;
        }

        public void OnTick(AbilityContext ctx, float dt) { }
        public void OnTeardown(AbilityContext ctx) { }
    }
}
