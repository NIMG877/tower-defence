using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AbilitySystem
{
    /// <summary>
    /// Level-scoped driver for executions of rules marked detached. Executions
    /// live here instead of the host rule runtime, so they keep advancing after
    /// the host dies or returns to the entity pool. Wired into
    /// LevelResourceSharing like every other level manager: ToStart begins the
    /// fixed-update loop, ToEnd cancels everything before level teardown.
    /// </summary>
    public class DetachedStepScheduler : IManagerStartEnd
    {
        private static DetachedStepScheduler _instance;
        public static DetachedStepScheduler Manager
        {
            get
            {
                if (_instance == null)
                    _instance = new DetachedStepScheduler();
                return _instance;
            }
        }

        private readonly List<AbilityStepExecution> _executions =
            new List<AbilityStepExecution>();
        private bool _running;

        public void Initialize() { }

        public void ToStart()
        {
            if (_running) return;
            _running = true;
            Drive(LevelResourceSharing.LevelCtk).Forget();
        }

        public void ToEnd()
        {
            _running = false;
            CancelAll();
        }

        internal void Add(AbilityStepExecution execution)
        {
            if (execution == null) throw new ArgumentNullException(nameof(execution));
            _executions.Add(execution);
        }

        private async UniTaskVoid Drive(CancellationToken ct)
        {
            while (_running)
            {
                await UniTask.WaitForFixedUpdate(ct);
                TickAll(Time.fixedDeltaTime);
            }
        }

        /// <summary>Mirror of EntityAbilityRunner.Tick: snapshot before ticking
        /// so an execution that triggers another detached rule mid-tick cannot
        /// grow the list under iteration. Public so tests can advance the
        /// scheduler without the fixed-update loop.</summary>
        public void TickAll(float deltaTime)
        {
            var snapshot = new List<AbilityStepExecution>(_executions);
            for (int i = 0; i < snapshot.Count; i++)
                snapshot[i].Tick(deltaTime);
            for (int i = _executions.Count - 1; i >= 0; i--)
                if (_executions[i].IsComplete) _executions.RemoveAt(i);
        }

        internal void CancelAll()
        {
            for (int i = 0; i < _executions.Count; i++)
                _executions[i].Cancel();
            _executions.Clear();
        }
    }
}
