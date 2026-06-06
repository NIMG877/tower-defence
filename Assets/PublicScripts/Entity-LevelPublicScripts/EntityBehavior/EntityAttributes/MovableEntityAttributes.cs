using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MovableEntityAttributes : EntityAttributes
{
    [Space(20), Header("movable attributes")]
    [SerializeField] private float _moveSpeed;
    [SerializeField] private int _massLevel;
    [SerializeField] private int _moveMethod;

    public float MoveSpeed { get { return _moveSpeed; } }
    public int MassLevel { get { return _massLevel; } }
    public int MoveMethod { get { return _moveMethod; } }
}
