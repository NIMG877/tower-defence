using Cysharp.Threading.Tasks;
using MyUI;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LevelRescurceManager : IManagerStartEnd
{
    private static LevelRescurceManager _instance;
    public static LevelRescurceManager Manager
    {
        get
        {
            if (_instance == null)
                _instance = new LevelRescurceManager();
            return _instance;
        }
    }
    private LevelRescurceManager() { }
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
    public void ChangeCost(int changeNum)
    {
        _cost += changeNum;
        if (_cost < 0)
        {
            _cost = 0;
        }
        else if (_cost > _maxCost)
        {
            _cost = _maxCost;
        }
        if (_start)
            LevelMessagePanel.Panel.CostTextUpDate();
    }

    private async void CostRecover()
    {
        while (true)
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
        CostRecover();
        _start = true;
    }

    public void ToEnd()
    {
        _start = false;
    }
}
