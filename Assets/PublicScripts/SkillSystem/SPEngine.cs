using System;

namespace SkillSystem
{
    public class SPEngine
    {
        private readonly SPConfig _cfg;
        private readonly Action _onFire;
        private float _currentSp;
        private int _currentCharge;
        private float _currentDuration;
        private bool _isActive;
        private int _recoverForbid;
        private bool _wasFiredThisTick;

        public float CurrentSp => _currentSp;
        public int CurrentCharge => _currentCharge;
        public bool IsActive => _isActive;
        public bool IsRecoverForbidden => _recoverForbid > 0;

        public SPEngine(SPConfig cfg, Action onFire)
        {
            _cfg = cfg ?? new SPConfig();
            _onFire = onFire ?? (() => { });
            _currentSp = _cfg.initialSp;
            _currentCharge = 0;
            _currentDuration = 0f;
        }

        public void OnTick(float dt, float dtMultiplier)
        {
            _wasFiredThisTick = false;
            // Recovery
            if (_cfg.recoverMode == SpRecoverMode.Natural && _recoverForbid == 0 && !_isActive)
            {
                _currentSp = Math.Min(_currentSp + dt * dtMultiplier, _cfg.totalSp);
            }
            // Duration consume
            if (_isActive && _cfg.consumeMode == SpConsumeMode.Duration)
            {
                _currentDuration -= dt;
                if (_currentDuration <= 0f)
                {
                    _currentDuration = 0f;
                    EndSkill();
                }
            }
            // Natural open
            if (!_isActive && _cfg.openMode == SkillOpenMode.Natural && CanBegin())
            {
                FireSkill();
            }
        }

        public void OnAttackSuccessfully()
        {
            if (_cfg.recoverMode == SpRecoverMode.OnAttackHit) AddSp(1f);
            if (_cfg.consumeMode == SpConsumeMode.OnAttackHit && _isActive) ConsumeChargeForHit();
        }

        public void OnAfterHurt(int applyType)
        {
            if (applyType != 0 && applyType != 1) return;
            if (_cfg.recoverMode == SpRecoverMode.OnAfterHurt) AddSp(1f);
            if (_cfg.consumeMode == SpConsumeMode.OnAfterHurt && _isActive) ConsumeChargeForHit();
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

        public void OnAttackHit()
        {
            if (_cfg.openMode == SkillOpenMode.OnAttackHit && CanBegin()) FireSkill();
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
            if (_cfg.skillDuration > 0f) _currentDuration = _cfg.skillDuration;
            else { _isActive = false; _wasFiredThisTick = true; }
            if (_cfg.recoverForbidDuringSkill) _recoverForbid++;
            _onFire();
        }

        public void EndSkill()
        {
            if (!_isActive) return;
            _isActive = false;
            _currentDuration = 0f;
            if (_cfg.recoverForbidDuringSkill && _recoverForbid > 0) _recoverForbid--;
        }

        public void SetRecoverForbid(bool forbid)
        {
            if (forbid) _recoverForbid++;
            else if (_recoverForbid > 0) _recoverForbid--;
        }

        private void AddSp(float v)
        {
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

        private void ConsumeChargeForHit()
        {
            // In a duration-based skill that consumes on hit, just end it
            if (_cfg.consumeMode == SpConsumeMode.OnAttackHit) EndSkill();
        }
    }
}
