using Cysharp.Threading.Tasks;
using MyUI;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LevelResourceManager : IManagerStartEnd
{
    private static LevelResourceManager _instance;
    public static LevelResourceManager Manager
    {
        get
        {
            if (_instance == null)
                _instance = new LevelResourceManager();
            return _instance;
        }
    }
    private LevelResourceManager() { }
    private int _cost;
    private int _maxCost;
    private float _costRecoverSpeed;
    private float _costRecoverTimer;
    private int _canSetNumLeft;
    private bool _start;
    private int _levelHpLeft;
    private int _currentOperateCount;
    private int _needOperateCount;
    public int CanSetNumLeft
    {
        get
        {
            return _canSetNumLeft;
        }
        set
        {
            _canSetNumLeft = value;
            if (_start)
                LevelMessagePanel.Panel.CanSetNumUpDate();
        }
    }
    public int CurrentOperateCount
    {
        get
        {
            return _currentOperateCount;
        }
        set
        {
            _currentOperateCount = value;
            LevelMessagePanel.Panel.CurrentNumAndTotalNumUpdate();
        }
    }
    public int NeedOperateCount
    {
        get
        {
            return _needOperateCount;
        }
        set
        {
            _needOperateCount = value;
            LevelMessagePanel.Panel.CurrentNumAndTotalNumUpdate();
        }
    }
    public (int currentCost, int maxCost, float costTimer) CostMessage
    {
        get
        {
            return (_cost, _maxCost, _costRecoverTimer);
        }
    }
    public int LevelHpLeft
    {
        get
        {
            return _levelHpLeft;
        }
        set
        {
            _levelHpLeft = value;
            if (_levelHpLeft <= 0)
            {
                _levelHpLeft = 0;
                LevelActionManager.Manager.MissionEnd(false);
            }
            LevelMessagePanel.Panel.LevelHpLeftTextUpdate();
        }
    }
    public void SetCostMessage(int cost0, int maxCost, float costRecoverSpeed)
    {
        _cost = cost0;
        _maxCost = maxCost;
        _costRecoverSpeed = costRecoverSpeed;
        _costRecoverTimer = 0;
    }
    /// <summary>增减费用并 clamp 到 [0, <see cref="_maxCost"/>]。</summary>
    /// <returns>clamp 后实际生效的变化量，可正可负可为 0。</returns>
    public int ChangeCost(int changeNum)
    {
        int before = _cost;
        _cost = Mathf.Clamp(_cost + changeNum, 0, _maxCost);
        if (_start)
            LevelMessagePanel.Panel.CostTextUpDate();
        return _cost - before;
    }

    private async UniTaskVoid CostRecover()
    {
        while (_start)
        {
            if (_cost < _maxCost)
            {
                if (_costRecoverTimer < 1)
                {
                    _costRecoverTimer += Time.fixedDeltaTime * _costRecoverSpeed;
                }
                else
                {
                    _costRecoverTimer += Time.fixedDeltaTime * _costRecoverSpeed - 1;
                    _cost++;
                }
            }
            await UniTask.WaitForFixedUpdate(LevelResourceSharing.LevelCtk);
        }
    }

    public void Initialize()
    {

    }

    public void ToStart()
    {
        _start = true;
        CostRecover().Forget();
    }

    public void ToEnd()
    {
        _start = false;
    }
}
