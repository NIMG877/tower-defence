using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BlockData : MonoBehaviour
{
    [SerializeField] private bool _highland;
    [SerializeField] private bool _canSet;
    [SerializeField] private int _passableType;//可通过类型：0-地面可通过，1-近地悬浮可通过，2-飞行可通过，3-不可通过
    private bool _tempOccupy;

    [Header("当ProtalOutBlock不为空时需要填写")]
    public BlockData ProtalOutBlock;
    public Color ProtalColor;
    [Space(20)]
    public bool Deadly;
    [HideInInspector] public Material Material;
    public int PassableType { get { return _passableType; } }
    public bool Highland { get { return _highland; } }
    public bool CanSet { get { return _canSet; } }
    public bool TempOccupy { get { return _tempOccupy; } }
}
