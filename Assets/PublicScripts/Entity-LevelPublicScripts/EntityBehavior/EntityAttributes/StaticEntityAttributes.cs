using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StaticEntityAttributes : EntityAttributes
{
    [Space(20),Header("static attributes")]
    [SerializeField] private int cost;
    [SerializeField] private bool _canCallBack;
    [SerializeField] private bool _needSelectDirection;
    [SerializeField, Tooltip("0-地面，1-高台，2-均可")] private int _canSetType;
    [SerializeField] private float respawn_time;
    [SerializeField, Tooltip("再部署策略 0-从部署开始进入再部署时间；1-撤退进入再部署时间；2-全部部署后进入再部署时间")] private int respawn_time_caculate_strategy;
    [SerializeField] private bool _canRespawn;
    [SerializeField] private bool _respawnCostUp;
    [SerializeField] private int _canSetNumOccupy;
    public int Cost { get { return cost; } }
    public bool CanCallBack { get { return _canCallBack; } }
    public bool NeedSelectDirection { get { return _needSelectDirection; } }
    public int CanSetType { get { return _canSetType; } }
    public float RespawnTime { get { return respawn_time; } }
    public int RespawnTimeCaculateStrategy { get { return respawn_time_caculate_strategy; } }
    public bool CanRespawn { get { return _canRespawn; } }
    public int CanSetNumOccupy { get { return _canSetNumOccupy; } }
    public bool RespawnCostUp { get { return _respawnCostUp; } }
}
