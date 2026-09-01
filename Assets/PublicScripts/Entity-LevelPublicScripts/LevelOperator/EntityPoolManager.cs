using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class EntityPool
{
    public EntityID Id;
    public EntityData EntityData;
    [SerializeField] private List<Entity> inp_entities;
    private Transform entity_pool;
    private bool isStatic;
    public EntityPool(EntityID id, Transform entity_p, int count)
    {
        count = Math.Min(10, count);
        Id=id;
        EntityData = GameDataService.EntityRepository.Get(id);
        isStatic = EntityData.IsStatic;
        entity_pool = entity_p;
        inp_entities = new List<Entity>(count);
        for (int i = 0; i < count; i++)
        {
            inp_entities.Add(CreateNewEntity());
        }
    }
    private Entity CreateNewEntity(int skillIndex = 0)
    {
        GameObject gameObject = UnityEngine.Object.Instantiate(EntityData.Prefab, entity_pool);
        gameObject.SetActive(false);
        if (isStatic)
            gameObject.AddComponent<InteractableStatic>();
        gameObject.AddComponent<BuffController>();
        Entity newEntity = gameObject.AddComponent<Entity>();
        newEntity.thisEntityPool = this;
        newEntity.EntityData = EntityData;
        newEntity.SelectedSkillIndex = skillIndex;
        IPoolOperation[] poolOperations = newEntity.GetComponents<IPoolOperation>();
        for (int j = poolOperations.Length - 1; j >= 0; j--)
        {
            poolOperations[j].PreWarm();
        }
        return newEntity;
    }
    public Entity GetEntity()
    {
        return inp_entities[0];
    }
    public void ExpandEntityPool(int count)
    {
        int currentCount = inp_entities.Count;
        count = Math.Min(10 - currentCount, count - currentCount);
        for (int i = currentCount; i < currentCount + count; i++)
        {
            inp_entities.Add(CreateNewEntity());
        }
    }
    public Entity CallOut(Vector2 destination, int camp, int skillIndex = 0)
    {
        Entity outEntity;
        IPoolOperation[] poolOperations;
        if (inp_entities.Count == 0)
        {
            outEntity = CreateNewEntity(skillIndex);
        }
        else
        {
            outEntity = inp_entities[0];
            inp_entities.RemoveAt(0);
        }
        poolOperations = outEntity.GetComponents<IPoolOperation>();
        outEntity.Camp = camp;
        // 池化实体跨关卡复用,每次部署重新断言生效技能(不同则休眠期重建)
        outEntity.SetSelectedSkill(skillIndex);
        outEntity.Movement.SetPosition(destination);
        outEntity.gameObject.SetActive(true);
        for (int i = poolOperations.Length - 1; i >= 0; i--)
        {
            poolOperations[i].Initialize();
        }
        return outEntity;
    }
    public void Return(Entity returnEntity)
    {
        inp_entities.Add(returnEntity);
        returnEntity.gameObject.SetActive(false);
        IPoolOperation[] poolOperations = returnEntity.GetComponents<IPoolOperation>();
        for (int i = 0; i < poolOperations.Length; i++)
        {
            poolOperations[i].Dormancy();
        }
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
        entity_pools = new List<EntityPool>();
    }
    private List<EntityPool> entity_pools;
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
                entity_pools.Add(entityPool);
            }
            else
            {
                entityPool.ExpandEntityPool(count);
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
                        entity_pools.Add(rEP);
                    }
                    else
                    {
                        rEP.ExpandEntityPool(icount * rNum[j]);
                    }
                    GenerateCanSpawnEntityPool(rIdList[j], icount);
                }
            }
            GenerateCanSpawnEntityPool(id, count);
        }
    }
    public EntityPool FetchEntityPool(EntityID id)
    {
        for (int i = 0; i < entity_pools.Count; i++)
        {
            if (entity_pools[i].Id == id)
            {
                return entity_pools[i];
            }
        }
        return null;
    }

    public void Initialize()
    {

    }

    public void ToEnd()
    {

    }

    public void ToStart()
    {

    }
}
