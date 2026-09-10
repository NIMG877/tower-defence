using UnityEngine;

/// <summary>Legacy component shell. Runtime attack behavior belongs to EntityAttack.</summary>
public class AttackBase : MonoBehaviour, IPoolOperation
{

    public virtual void PreWarm() { }

    // Entity owns the POCO lifecycle. Keeping these callbacks preserves the old component contract.
    public virtual void Initialize() { }
    public virtual void Dormancy() { }
}
