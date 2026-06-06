using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Talent : MonoBehaviour, IPoolOperation
{
    protected Entity _thisEntity;
    [Header("天赋显示UI相关")]
    [SerializeField, Tooltip("天赋名称")] private string _talentName;
    [SerializeField, Tooltip("天赋描述"), TextArea(2,5)] private string _talentDescription;
    public string TalentName { get { return _talentName; } }
    public string TalentDescription { get { return _talentDescription; } }
    public virtual void Dormancy()
    {

    }

    public virtual void Initialize()
    {

    }

    public virtual void PreWarm()
    {
        _thisEntity = GetComponent<Entity>();
    }
}
