using Cysharp.Threading.Tasks;

namespace AbilitySystem.Components
{
    [RegisterComponent("CoroutineLoop")]
    public class CoroutineLoopComponent : ITickingComponent
    {
        private string _stopConditionKey;
        private bool _isRunning;

        public void OnInit(AbilityContext ctx, ParamList p)
        {
            _stopConditionKey = p.GetString("stopWhenBlackboardKeyMissing", "");
        }

        public void OnTrigger(AbilityContext ctx) { }

        public void OnTick(AbilityContext ctx, float dt)
        {
            if (!_isRunning)
            {
                _isRunning = true;
                // Fire-and-forget; the loop self-terminates when the stop key disappears or OnTeardown fires.
                _ = LoopAsync(ctx);
            }
        }

        public void OnTeardown(AbilityContext ctx)
        {
            _isRunning = false;
        }

        private async UniTask LoopAsync(AbilityContext ctx)
        {
            while (_isRunning)
            {
                if (!string.IsNullOrEmpty(_stopConditionKey) && ctx.sharedBlackboard != null
                    && !ctx.sharedBlackboard.Has(_stopConditionKey))
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
