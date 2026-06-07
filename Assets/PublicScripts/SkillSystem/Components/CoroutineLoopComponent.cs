using Cysharp.Threading.Tasks;

namespace SkillSystem.Components
{
    [RegisterComponent("CoroutineLoop")]
    public class CoroutineLoopComponent : ITickingComponent
    {
        private string _stopConditionKey;
        private bool _isRunning;

        public void OnInit(SkillContext ctx, ParamList p)
        {
            _stopConditionKey = p.GetString("stopWhenBlackboardKeyMissing", "");
        }

        public void OnTrigger(SkillContext ctx) { }

        public void OnTick(SkillContext ctx, float dt)
        {
            if (!_isRunning)
            {
                _isRunning = true;
                // Fire-and-forget; the loop self-terminates when the stop key disappears or OnTeardown fires.
                _ = LoopAsync(ctx);
            }
        }

        public void OnTeardown(SkillContext ctx)
        {
            _isRunning = false;
        }

        private async UniTask LoopAsync(SkillContext ctx)
        {
            while (_isRunning)
            {
                if (!string.IsNullOrEmpty(_stopConditionKey) && ctx.blackboard != null
                    && !ctx.blackboard.Has(_stopConditionKey))
                {
                    _isRunning = false;
                    return;
                }
                // Per-tick body: dispatch a sub-event (OnIntervalTick equivalent on the same skill).
                await UniTask.WaitForFixedUpdate();
            }
        }
    }
}
