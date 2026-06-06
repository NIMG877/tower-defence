using System;

/// <summary>
/// 实体 ID：(category, number) 二元组。
/// 例如 <c>new EntityID("c", 1)</c> 表示角色分类下的第 1 个角色。
/// 全局命名空间，跨层共享。
/// </summary>
[Serializable]
public struct EntityID : IEquatable<EntityID>
{
    public string ID_C;
    public int    ID_N;

    /// <summary>空槽位 ID（"占位空"），用于未填槽的格子。详见 CharacterCardManager 等。</summary>
    public static readonly EntityID Null = new EntityID(null, 0);

    public bool IsNull => ID_C == null;

    public EntityID(string idC, int idN)
    {
        ID_C = idC;
        ID_N = idN;
    }

    public bool Equals(EntityID other) => ID_C == other.ID_C && ID_N == other.ID_N;
    public override bool Equals(object obj) => obj is EntityID o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(ID_C, ID_N);
    public static bool operator ==(EntityID l, EntityID r) => l.Equals(r);
    public static bool operator !=(EntityID l, EntityID r) => !l.Equals(r);
    public override string ToString() => $"{ID_C}-{ID_N}";
}
