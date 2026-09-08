using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

/// <summary>
/// 单个 EntityID 的实体池。空闲集合由 <see cref="ObjectPool{Entity}"/> 管理（LIFO），
/// 生命周期编排（IPoolOperation 派发、激活/失活）留在 CallOut/Return 中——
/// Initialize 依赖出池参数（Camp/位置），无法放进 actionOnGet 回调，故池回调全部留空，
/// ObjectPool 只承担"取一个/没有则新建/收回/关末销毁"的空闲集合职责。
/// </summary>
[Serializable]
public class EntityPool
{
    public EntityID Id;
    public EntityData EntityData;

    /// <summary>预热预算：单池预热不超过此数，防止 CanSpawnEntityIds 召唤链递归预热
    /// 在开关卡瞬间大量实例化。场上活体超过预算时由 CallOut 空池新建（不限量）。</summary>
    private const int PrewarmBudget = 10;

    private readonly Transform _parent;
    private readonly bool _isStatic;
    private readonly ObjectPool<Entity> _pool;

    public EntityPool(EntityID id, Transform entityParent, int prewarmCount)
    {
        Id = id;
        EntityData = GameDataService.EntityRepository.Get(id);
        _isStatic = EntityData.IsStatic;
        _parent = entityParent;
        _pool = new ObjectPool<Entity>(
            CreateNewEntity,
            actionOnGet: null,
            actionOnRelease: null,
            actionOnDestroy: entity => Object.Destroy(entity.gameObject),
            collectionCheck: true);
        ExpandPrewarm(prewarmCount);
    }

    private Entity CreateNewEntity()
    {
        GameObject gameObject = Object.Instantiate(EntityData.Prefab, _parent);
        gameObject.SetActive(false);
        if (_isStatic)
            gameObject.AddComponent<InteractableStatic>();
        gameObject.AddComponent<BuffController>();
        Entity newEntity = gameObject.AddComponent<Entity>();
        gameObject.AddComponent<EntityVisuals>();
        gameObject.AddComponent<EntityFacing>();
        newEntity.thisEntityPool = this;
        newEntity.EntityData = EntityData;
        // IPoolOperation 数组只在此扫描一次并缓存到实体上，出池/入池直接用
        newEntity.PoolOps = newEntity.GetComponents<IPoolOperation>();
        for (int i = newEntity.PoolOps.Length - 1; i >= 0; i--)
        {
            newEntity.PoolOps[i].PreWarm();
        }
        return newEntity;
    }

    /// <summary>当前空闲实体数。</summary>
    public int InactiveCount => _pool.CountInactive;

    /// <summary>按"目标总数"补充预热（受 <see cref="PrewarmBudget"/> 约束）。</summary>
    public void ExpandPrewarm(int targetTotal)
    {
        int target = Math.Min(PrewarmBudget, targetTotal);
        int add = target - _pool.CountInactive;
        for (int i = 0; i < add; i++)
        {
            _pool.Release(CreateNewEntity());
        }
    }

    /// <summary>
    /// 查看下一次 CallOut 将取出的实体（不移除、不触发 Initialize/Dormancy）。
    /// 实体处于休眠态，Stats/EntityData 等纯数据可读；部署 UI 用它做"即将出池实体"的
    /// 显示与取价。池空时返回 null："下一个出池者"尚不存在，取价/预览对它无从谈起，
    /// 由调用方跳过。Peek 不做实例化，保持零副作用。
    /// </summary>
    public Entity PeekNext()
    {
        if (_pool.CountInactive == 0)
            return null;
        Entity entity = _pool.Get();
        _pool.Release(entity);
        return entity;
    }

    public Entity CallOut(Vector2 destination, int camp, int skillIndex = 0)
    {
        Entity outEntity = _pool.Get();
        outEntity.Camp = camp;
        // 池化实体跨部署复用（回收再部署），每次部署重新断言生效技能（不同则休眠期重建）
        outEntity.SetSelectedSkill(skillIndex);
        outEntity.Movement.SetPosition(destination);
        outEntity.gameObject.SetActive(true);
        IPoolOperation[] poolOperations = outEntity.PoolOps;
        for (int i = poolOperations.Length - 1; i >= 0; i--)
        {
            poolOperations[i].Initialize();
        }
        return outEntity;
    }

    public void Return(Entity returnEntity)
    {
        // 必须先入空闲栈、后派发 Dormancy：Dormancy 链上会重入
        // MissionEnd → LevelEnd → EntityPoolManager.ToEnd（本实体往往正是触发
        // 关末的最后一只怪）——此刻它必须已在池内，Teardown 的 Clear 才能连同
        // 它一起销毁；若 Release 放在 Dormancy 之后，它会掉进"既不在池、也未
        // 销毁"的窗口，被 Release 进已清空的孤儿池而残留。
        _pool.Release(returnEntity);
        returnEntity.gameObject.SetActive(false);
        IPoolOperation[] poolOperations = returnEntity.PoolOps;
        for (int i = 0; i < poolOperations.Length; i++)
        {
            poolOperations[i].Dormancy();
        }
    }

    /// <summary>
    /// 退关销毁池（池生命周期与关卡对齐：每关创建、退关销毁）。
    /// LevelEnd 时序保证 EntityManager.ToEnd 先行还池，此处空闲实体应已全部在池内。
    /// 局内 buff 等实体状态随 GameObject 销毁自然消失，无需逐项清理。
    /// </summary>
    public void Teardown()
    {
        _pool.Clear();
    }
}
public class EntityPoolManager : IManagerStartEnd
{
    private static EntityPoolManager _instance;
    public static EntityPoolManager Manager
    {
        get
        {
            if (_instance == null)
                _instance = new EntityPoolManager();
            return _instance;
        }
    }
    private EntityPoolManager()
    {
        entity_pools = new Dictionary<EntityID, EntityPool>();
    }
    private readonly Dictionary<EntityID, EntityPool> entity_pools;

    public void CreateOrExpandEntityPool(Dictionary<EntityID, int> entityNum)
    {
        foreach (var kv in entityNum)
        {
            EntityID id = kv.Key;
            int count = kv.Value;
            EntityPool entityPool = FetchEntityPool(id);
            if (entityPool == null)
            {
                entityPool = new EntityPool(id, LevelResourceSharing.LM, count);
                entity_pools.Add(id, entityPool);
            }
            else
            {
                entityPool.ExpandPrewarm(count);
            }
            void GenerateCanSpawnEntityPool(EntityID iid, int icount)
            {
                EntityData entityData = GameDataService.EntityRepository.Get(iid);
                var rIdList = entityData.CanSpawnEntityIds;
                var rNum = entityData.CanSpawnEntityCounts;
                for (int j = 0; j < rIdList.Count; j++)
                {
                    EntityPool rEP = FetchEntityPool(rIdList[j]);
                    if (rEP == null)
                    {
                        rEP = new EntityPool(rIdList[j], LevelResourceSharing.LM, icount * rNum[j]);
                        entity_pools.Add(rIdList[j], rEP);
                    }
                    else
                    {
                        rEP.ExpandPrewarm(icount * rNum[j]);
                    }
                    GenerateCanSpawnEntityPool(rIdList[j], icount);
                }
            }
            GenerateCanSpawnEntityPool(id, count);
        }
    }
    public EntityPool FetchEntityPool(EntityID id)
    {
        return entity_pools.TryGetValue(id, out EntityPool pool) ? pool : null;
    }

    public void Initialize()
    {

    }

    public void ToEnd()
    {
        // 池生命周期与关卡对齐：销毁所有池并清空记录，下一关由 CreateOrExpandEntityPool 重建。
        foreach (var kv in entity_pools)
        {
            kv.Value.Teardown();
        }
        entity_pools.Clear();
    }

    public void ToStart()
    {

    }
}
