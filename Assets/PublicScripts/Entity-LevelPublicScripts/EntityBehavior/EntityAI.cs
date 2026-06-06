using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;

public class EntityAI
{
    public class State
    {
        public float StateValue;
        public Func<bool> StateTryStart;
        public Func<bool> StateTryEnd;
        public Func<float> StateValueCaculator;
    }
    private List<State> _states;
    private int _currentStateIndex;
    private bool _isStart;
    public void ToStart(List<State> states)
    {
        _states = states;
        _currentStateIndex = -1;
        if (!_isStart)
        {
            _isStart = true;
            FixedUpdate();
        }
    }
    private async void FixedUpdate()
    {
        if (_currentStateIndex == -1)
        {
            for (int i = 0; i < _states.Count; i++)
            {
                if (_states[i].StateValueCaculator != null)
                {
                    _states[i].StateValue = _states[i].StateValueCaculator();
                }
            }
            _states.Sort((state1, state2) => state2.StateValue.CompareTo(state1.StateValue));
            for (int i = 0; i < _states.Count; i++)
            {
                if (_states[i].StateTryStart != null && _states[i].StateTryStart())
                {
                    _currentStateIndex = i;
                    break;
                }
            }
        }
        else
        {
            if (_states[_currentStateIndex].StateTryEnd == null || _states[_currentStateIndex].StateTryEnd())
            {
                _currentStateIndex = -1;
            }
        }
        await UniTask.WaitForFixedUpdate(LevelResourceSharing.LevelCtk);
    }
}
