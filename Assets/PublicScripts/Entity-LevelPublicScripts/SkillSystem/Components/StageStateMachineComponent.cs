using System;

namespace SkillSystem.Components
{
    [RegisterComponent("StageStateMachine")]
    public class StageStateMachineComponent : ITickingComponent
    {
        private StageConfig[] _stages;
        private int _currentIndex;
        private float _stageTimer;
        private Blackboard _activeBlackboard;
        private bool _isActive = true;

        // Test helpers (production code should use the OnInit + ParamList path).
        public string CurrentStageNameForTest =>
            (_stages != null && _currentIndex < _stages.Length) ? _stages[_currentIndex].name : null;

        public void OnInitForTest(StageConfig[] stages) { _stages = stages; }

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _activeBlackboard = ctx.blackboard;
            if (_stages == null)
            {
                var names = p.GetString("stageNames", "");
                if (string.IsNullOrEmpty(names)) return;
                var durs = p.GetString("stageDurations", "");
                var ns = names.Split('|');
                var ds = string.IsNullOrEmpty(durs) ? Array.Empty<string>() : durs.Split('|');
                _stages = new StageConfig[ns.Length];
                for (int i = 0; i < ns.Length; i++)
                {
                    _stages[i] = new StageConfig
                    {
                        name = ns[i].Trim(),
                        enterDuration = (i < ds.Length && float.TryParse(ds[i].Trim(), out var d)) ? d : -1f
                    };
                }
            }
            _currentIndex = 0;
            _stageTimer = 0f;
            _isActive = true;
            EnterCurrentStage(ctx);
        }

        public void OnTrigger(SkillContext ctx)
        {
            if (!_isActive || _stages == null || _currentIndex >= _stages.Length) return;
            var stage = _stages[_currentIndex];
            if (stage.transitionOn == null) return;
            for (int i = 0; i < stage.transitionOn.Length; i++)
            {
                var c = stage.transitionOn[i];
                var evalCtx = new ConditionEvalContext { Blackboard = _activeBlackboard, Event = ctx.currentEvent };
                if (ConditionEvaluator.Evaluate(c, evalCtx))
                {
                    Transition(ctx);
                    return;
                }
            }
        }

        public void OnTick(SkillContext ctx, float dt)
        {
            if (!_isActive || _stages == null || _currentIndex >= _stages.Length) return;
            _stageTimer += dt;
            var stage = _stages[_currentIndex];
            if (stage.enterDuration > 0f && _stageTimer >= stage.enterDuration)
                Transition(ctx);
        }

        public void OnTeardown(SkillContext ctx) { _isActive = false; }

        private void EnterCurrentStage(SkillContext ctx)
        {
            if (_stages == null || _currentIndex >= _stages.Length) return;
            if (ctx.blackboard != null)
            {
                ctx.blackboard.Set("__stage_name", _stages[_currentIndex].name);
                ctx.blackboard.Set("__stage_index", _currentIndex);
            }
            _stageTimer = 0f;
        }

        private void Transition(SkillContext ctx)
        {
            _currentIndex++;
            if (_currentIndex >= _stages.Length)
            {
                _isActive = false;
                return;
            }
            EnterCurrentStage(ctx);
        }
    }
}
