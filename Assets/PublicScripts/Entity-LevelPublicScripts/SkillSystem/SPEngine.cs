using System;

namespace SkillSystem
{
    public class SPEngine
    {
        private readonly SPConfig _cfg;
        private float _currentSp;
        private int _currentCharge;
        private float _currentAmount;
        private bool _isActive;
        private int _recoverForbid;

        /// <summary>Fires when the skill begins (after charge consumed, before any amount consume).</summary>
        public event Action OnBegin;
        /// <summary>Fires when the skill ends, either via amount expiry or instant-fire path.</summary>
        public event Action OnEnd;

        public float CurrentSp => _currentSp;
        public int CurrentCharge => _currentCharge;
        public bool IsActive => _isActive;
        public bool IsRecoverForbidden => _recoverForbid > 0;
        public float CurrentAmount => _currentAmount;

        public SPEngine(SPConfig cfg)
        {
            _cfg = cfg ?? new SPConfig();
            ResetState();
        }

        /// <summary>
        /// 复位到初始状态。池化实体重新部署时由 EntitySkillRunner.OnInitialize 调用，
        /// 不会重建事件订阅（订阅生命周期在 OnInitialize/OnTeardown）。
        /// </summary>
        public void Reset()
        {
            ResetState();
        }

        private void ResetState()
        {
            _currentSp = _cfg.initialSp;
            _currentCharge = 0;
            _currentAmount = 0f;
            _isActive = false;
            _recoverForbid = 0;
        }

        public void OnTick(float dt, float dtMultiplier)
        {
            // Recovery
            if (_cfg.recoverMode == SpRecoverMode.Natural)
            {
                AddSp(dt * dtMultiplier);
            }
            // Amount consume
            if (_isActive && _cfg.consumeMode == SpConsumeMode.Natural)
            {
                CounsumeAmount(dt);
            }
            // Natural open
            if (!_isActive && _cfg.openMode == SkillOpenMode.Auto && CanBegin())
            {
                FireSkill();
            }
        }

        public void OnAttackSuccessfully()
        {
            if (_cfg.openMode == SkillOpenMode.OnAttackSuccessfully && CanBegin()) FireSkill();
            if (_cfg.recoverMode == SpRecoverMode.OnAttackSuccessfully) AddSp(1f);
            if (_cfg.consumeMode == SpConsumeMode.OnAttackSuccessfully && _isActive) CounsumeAmount(1f);    
        }

        public void OnAfterHurt(int applyType)
        {
            if (applyType != 0 && applyType != 1) return;
            if (_cfg.recoverMode == SpRecoverMode.OnAfterHurt) AddSp(1f);
            if (_cfg.consumeMode == SpConsumeMode.OnAfterHurt && _isActive) CounsumeAmount(1f);
        }

        public void OnAttackAnimBegin()
        {
            if (_cfg.openMode == SkillOpenMode.OnAttackAnimBegin && CanBegin()) FireSkill();
        }

        public void OnBeforeHurt(int applyType)
        {
            if (applyType != 0 && applyType != 1) return;
            if (_cfg.openMode == SkillOpenMode.OnBeforeHurt && CanBegin()) FireSkill();
        }

        public bool CanBegin()
        {
            if (_isActive) return false;
            float total = _currentSp + _cfg.totalSp * _currentCharge;
            return total >= _cfg.totalSp;
        }

        public void FireSkill()
        {
            if (!CanBegin()) return;
            if (_currentCharge > 0) _currentCharge--;
            else _currentSp = 0f;
            _isActive = true;
            if (_cfg.skillAmount > 0f) _currentAmount = _cfg.skillAmount;
            if (_cfg.recoverForbidDuringSkill) _recoverForbid++;
            OnBegin?.Invoke();
            if (_cfg.skillAmount <= 0f) EndSkill();
        }

        public void EndSkill()
        {
            if (!_isActive) return;
            _isActive = false;
            if (_cfg.recoverForbidDuringSkill && _recoverForbid > 0) _recoverForbid--;
            OnEnd?.Invoke();
        }

        private void AddSp(float v)
        {
            if (_recoverForbid > 0) return;
            if (_cfg.chargeNum <= 1)
            {
                _currentSp = Math.Min(_currentSp + v, _cfg.totalSp);
            }
            else
            {
                _currentSp += v;
                while (_currentSp >= _cfg.totalSp && _currentCharge < _cfg.chargeNum)
                {
                    _currentSp -= _cfg.totalSp;
                    _currentCharge++;
                }
                if (_currentCharge >= _cfg.chargeNum)
                {
                    _currentCharge = _cfg.chargeNum;
                    _currentSp = 0f;
                }
            }
        }

        private void CounsumeAmount(float v)
        {
            if (!_isActive) return;
            _currentAmount -= v;
            if (_currentAmount <= 0f)
            {
                _currentAmount = 0f;
                EndSkill();
            }
        }
    }
}
