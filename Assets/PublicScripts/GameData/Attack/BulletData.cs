using UnityEngine;

/// <summary>Visual and trajectory settings shared by attack resources and runtime bullets.</summary>
[CreateAssetMenu(
    menuName = "TD/Entity/Bullet Data",
    fileName = "BulletData")]
public sealed class BulletData : ScriptableObject
{
    public GameObject BulletPrefab;
    public GameObject BulletTrailPrefab;
    public GameObject BulletSpawnEffect;
    public GameObject BulletDestroyEffect;
    public float BulletSpeed;
    public int BulletType;
    public bool AllowNoTarget;
    [Header("BulletType=1")]
    public float DevitationXRate;
    public float DevitationYValue;
}
