using System;

namespace AbilitySystem.Components
{
    [RegisterComponent("ChargeStateController")]
    public class ChargeStateController : AbilityComponentBase
    {
        private Func<string> _targetCountKey;
        private Func<int> _minimumTargetCount;
        private Func<string> _phaseKey;
        private Func<string> _phaseEnteredKey;
        private Func<float> _chargeDuration;
        private Func<string> _backoutAnimation;
        private Func<string> _attackAnimation;
        private Func<float> _animationDurationScale;

        private ChargePhase _phase;
        private float _timer;
        private float _phaseDuration;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            Blackboard bb = ctx.sharedBlackboard;
            _targetCountKey = p.GetStringLazy("targetCountKey", "", bb);
            _minimumTargetCount = p.GetIntLazy("minimumTargetCount", 1, bb);
            _phaseKey = p.GetStringLazy("phaseKey", "charge_phase", bb);
            _phaseEnteredKey = p.GetStringLazy("phaseEnteredKey", "charge_phase_entered", bb);
            _chargeDuration = p.GetFloatLazy("chargeDuration", 1f, bb);
            _backoutAnimation = p.GetStringLazy("backoutAnimation", "", bb);
            _attackAnimation = p.GetStringLazy("attackAnimation", "", bb);
            _animationDurationScale = p.GetFloatLazy("animationDurationScale", 0.9f, bb);

            _phase = ChargePhase.Idle;
            _timer = 0f;
            _phaseDuration = 0f;
            WriteState(bb, true);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (!(ctx.currentEvent is TickEvent tick) || ctx.sharedBlackboard == null) return;

            WriteEntered(ctx.sharedBlackboard, false);
            switch (_phase)
            {
                case ChargePhase.Idle:
                    if (HasTargets(ctx.sharedBlackboard))
                        Transition(ctx, ChargePhase.Charging, Math.Max(0f, _chargeDuration()));
                    break;

                case ChargePhase.Charging:
                    if (!HasTargets(ctx.sharedBlackboard))
                    {
                        Transition(
                            ctx,
                            ChargePhase.Backout,
                            ResolveAnimationDuration(ctx.entity, _backoutAnimation()));
                        break;
                    }
                    _timer += tick.deltaTime;
                    if (_timer >= _phaseDuration)
                    {
                        Transition(
                            ctx,
                            ChargePhase.Attack,
                            ResolveAnimationDuration(ctx.entity, _attackAnimation()));
                    }
                    break;

                case ChargePhase.Backout:
                    _timer += tick.deltaTime;
                    if (_timer >= _phaseDuration) Transition(ctx, ChargePhase.Idle, 0f);
                    break;

                case ChargePhase.Attack:
                    _timer += tick.deltaTime;
                    if (_timer >= _phaseDuration) Transition(ctx, ChargePhase.Detonate, 0f);
                    break;

                case ChargePhase.Detonate:
                    Transition(ctx, ChargePhase.Finished, 0f);
                    break;
            }
        }

        public override void OnTeardown(AbilityContext ctx)
        {
            if (ctx.sharedBlackboard == null) return;
            ctx.sharedBlackboard.Remove(_phaseKey());
            ctx.sharedBlackboard.Remove(_phaseEnteredKey());
        }

        private bool HasTargets(Blackboard bb)
        {
            string key = _targetCountKey();
            if (string.IsNullOrEmpty(key)) return false;
            string raw = bb.Get(key, "0");
            return int.TryParse(raw, out int count) && count >= Math.Max(1, _minimumTargetCount());
        }

        private float ResolveAnimationDuration(Entity entity, string animationName)
        {
            if (entity?.entityAM == null || string.IsNullOrEmpty(animationName)) return 0f;
            return Math.Max(0f, entity.entityAM.ResolveNamedAnimationDuration(animationName)
                * Math.Max(0f, _animationDurationScale()));
        }

        private void Transition(AbilityContext ctx, ChargePhase phase, float duration)
        {
            _phase = phase;
            _timer = 0f;
            _phaseDuration = Math.Max(0f, duration);
            WriteState(ctx.sharedBlackboard, true);
        }

        private void WriteState(Blackboard bb, bool entered)
        {
            if (bb == null) return;
            string phaseKey = _phaseKey();
            if (!string.IsNullOrEmpty(phaseKey)) bb.Set(phaseKey, PhaseName(_phase));
            WriteEntered(bb, entered);
        }

        private void WriteEntered(Blackboard bb, bool entered)
        {
            string key = _phaseEnteredKey();
            if (!string.IsNullOrEmpty(key)) bb.Set(key, entered ? "True" : "False");
        }

        private static string PhaseName(ChargePhase phase)
        {
            switch (phase)
            {
                case ChargePhase.Charging: return "charging";
                case ChargePhase.Backout: return "backout";
                case ChargePhase.Attack: return "attack";
                case ChargePhase.Detonate: return "detonate";
                case ChargePhase.Finished: return "finished";
                default: return "idle";
            }
        }

        private enum ChargePhase
        {
            Idle,
            Charging,
            Backout,
            Attack,
            Detonate,
            Finished,
        }
    }
}
