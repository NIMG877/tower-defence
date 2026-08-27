using MyUI;
using System.Collections.Generic;
using UnityEngine;

public class EntityManager : IManagerStartEnd
{
    private static EntityManager _instance;
    public static EntityManager Manager
    {
        get
        {
            if (_instance == null)
                _instance = new EntityManager();
            return _instance;
        }
    }

    public static float EntityR = 0.25f;
    private EntityManager()
    {
        _orientationImg = Resources.Load<GameObject>("Prefabs/EffectPrefabs/facing");
        _shadowImg = Resources.Load<GameObject>("Prefabs/EffectPrefabs/shadow");
        _poolManager = EntityPoolManager.Manager;
    }
    private readonly GameObject _orientationImg;
    private readonly GameObject _shadowImg;
    private readonly EntityPoolManager _poolManager;
    private readonly HashSet<Entity> _rangeSelectionSet = new HashSet<Entity>();
    private readonly HashSet<Entity> _staticEntityLookup = new HashSet<Entity>();
    private List<Entity> _turrets;
    private List<Entity> _monsters;
    private List<Entity> _staticEntities;
    private List<Entity>[,] _blockTurrets;
    private List<Entity>[,] _blockMonsters;
    private int _iSize;
    private int _jSize;

    public bool[,] StaticEntityExistBlock
    {
        get
        {
            bool[,] se = new bool[_iSize, _jSize];
            for (int i = 0; i < _staticEntities.Count; i++)
            {
                Vector2 pos = _staticEntities[i].Movement.Position;
                se[(int)(pos.y + 0.5), (int)(pos.x + 0.5)] = true;
            }
            return se;
        }
    }
    public delegate void OperationsAfterSetEntity(Entity entity);
    public event OperationsAfterSetEntity OnAfterSetEntity;

    /// <summary>
    /// Places a static entity.
    /// </summary>
    /// <param name="prefab_id">Entity id of the prefab to place.</param>
    /// <param name="destination">Placement position; snapped to the grid cell center.</param>
    /// <param name="camp">Entity camp.</param>
    /// <param name="orientation">Facing when placed: 0-up, 1-right, 2-down, 3-left.</param>
    /// <returns>The placed entity, or null when no pool exists for the id.</returns>
    public Entity SetStaticEntity(EntityID prefab_id, Vector2 destination, int camp, int orientation)
    {
        EntityPool entityPool = _poolManager.FetchEntityPool(prefab_id);
        destination.Set((int)(destination.x + 0.5), (int)(destination.y + 0.5));
        if (entityPool != null)
        {
            Entity staticEntity = entityPool.CallOut(destination, camp);
            AddEntityToStaticList(staticEntity);
            staticEntity.SetOrientation(orientation);
            Object.Instantiate(_shadowImg, destination, Quaternion.identity, staticEntity.TempContainer).transform.localScale *= EntityR / 0.5f;
            Object.Instantiate(_orientationImg, destination, Quaternion.identity, staticEntity.TempContainer).transform.Rotate(Vector3.forward, 90 * (1 - orientation));
            AddEntityToList(staticEntity, camp);
            if (camp == 1)
            {
                if (staticEntity.EntityData.ID.ID_C == "t")
                {
                    AudioManager.Manager.PlayAudio("token_set", 1, false, false);
                }
                else
                {
                    AudioManager.Manager.PlayAudio("char_set", 1, false, false);
                }
            }
            OnAfterSetEntity?.Invoke(staticEntity);
            return staticEntity;
        }
        Debug.LogError("No entity pool found for the prefab.");
        return null;
    }
    /// <summary>
    /// Spawns a movable entity.
    /// </summary>
    /// <param name="prefab_id">Entity id of the prefab to spawn.</param>
    /// <param name="destination">Spawn position.</param>
    /// <param name="camp">Entity camp.</param>
    /// <param name="pathSerial">Serial number of the path the entity follows.</param>
    /// <returns>The spawned entity, or null when no pool exists for the id.</returns>
    public Entity SetMovableEntity(EntityID prefab_id, Vector2 destination, int camp, int pathSerial)
    {
        EntityPool entityPool = _poolManager.FetchEntityPool(prefab_id);
        if (entityPool != null)
        {
            Entity movableEntity = entityPool.CallOut(destination, camp);
            movableEntity.MoveBase?.SetMoveParameters(pathSerial, 0, 0);
            Object.Instantiate(_shadowImg, destination, Quaternion.identity, movableEntity.TempContainer).transform.localScale *= EntityR / 0.5f;
            AddEntityToList(movableEntity, camp);
            OnAfterSetEntity?.Invoke(movableEntity);
            return movableEntity;
        }
        Debug.LogError($"No entity pool found for entity {prefab_id.ID_C}-{prefab_id.ID_N}.");
        return null;
    }
    /// <summary>
    /// Removes an entity from the per-block entity lists of the given blocks.
    /// </summary>
    /// <param name="inBlocks">Blocks to remove from; a (-1, -1) entry ends the list.</param>
    /// <param name="entity">Entity to remove.</param>
    /// <param name="camp">Entity camp (selects the monster/turret block grid).</param>
    public void RemoveEntityFromBlock((int i, int j)[] inBlocks, Entity entity, int camp)
    {
        List<Entity>[,] blockEntities = GetBlockEntities(camp);
        if (blockEntities == null)
        {
            return;
        }

        for (int i = 0; i < 4; i++)
        {
            if (inBlocks[i].i == -1)
            {
                break;
            }

            blockEntities[inBlocks[i].i, inBlocks[i].j].Remove(entity);
        }
    }
    /// <summary>
    /// Adds an entity to the per-block entity lists of the given blocks.
    /// </summary>
    /// <param name="inBlocks">Blocks to add to; a (-1, -1) entry ends the list.</param>
    /// <param name="entity">Entity to add.</param>
    /// <param name="camp">Entity camp (selects the monster/turret block grid).</param>
    public void AddEntityToBlock((int i, int j)[] inBlocks, Entity entity, int camp)
    {
        List<Entity>[,] blockEntities = GetBlockEntities(camp);
        if (blockEntities == null)
        {
            return;
        }

        for (int i = 0; i < 4; i++)
        {
            if (inBlocks[i].i == -1)
            {
                break;
            }

            blockEntities[inBlocks[i].i, inBlocks[i].j].Add(entity);
        }
    }
    /// <summary>
    /// Removes an entity from its camp's master list.
    /// </summary>
    /// <param name="entity">Entity to remove.</param>
    /// <param name="camp">Entity camp: 2 = monster list, 1 = turret list.</param>
    public bool RemoveEntityFromList(Entity entity, int camp)
    {
        if (camp == 2)
        {
            return _monsters.Remove(entity);
        }
        else if (camp == 1)
        {
            return _turrets.Remove(entity);
        }
        return false;

    }
    public void AddEntityToList(Entity entity, int camp)
    {
        if (camp == 2)
        {
            _monsters.Add(entity);
        }
        else if (camp == 1)
        {
            _turrets.Add(entity);
        }
    }
    public void RemoveEntityFromStaticList(Entity entity)
    {
        if (_staticEntities.Remove(entity))
        {
            if (!_staticEntities.Contains(entity))
            {
                _staticEntityLookup.Remove(entity);
            }

            for (int i = 0; i < _staticEntities.Count; i++)
            {
                _staticEntities[i].InteractableStatic.BuilderOrder = i + 1;
            }
        }
    }
    public void AddEntityToStaticList(Entity entity)
    {
        entity.InteractableStatic.BuilderOrder = _staticEntities.Count;
        _staticEntities.Add(entity);
        _staticEntityLookup.Add(entity);
    }
    /// <summary>
    /// Selects entities inside the given block range.
    /// </summary>
    /// <param name="range">Blocks to select from.</param>
    /// <param name="selectorCamp">Camp of the selecting entity.</param>
    /// <param name="sameCamp">True to select the selector's own camp, false for the opposing camp.</param>
    /// <param name="square_l">Half side length of the square test around each block.</param>
    /// <param name="force">Force-select entities that are marked unselectable.</param>
    /// <returns>The selected entity list.</returns>
    public List<Entity> EntitySelector_Range((int x, int y)[] range, int selectorCamp, bool sameCamp, float square_l, bool force)
    {
        List<Entity> list = new List<Entity>();
        List<Entity>[,] blockEntities = GetBlockEntities(GetTargetCamp(selectorCamp, sameCamp));
        if (blockEntities == null)
        {
            return list;
        }

        _rangeSelectionSet.Clear();
        for (int index = 0; index < range.Length; index++)
        {
            (int x, int y) block = range[index];
            List<Entity> entities = blockEntities[block.y, block.x];
            for (int jndex = 0; jndex < entities.Count; jndex++)
            {
                Entity entity = entities[jndex];
                if (!PassesSelectability(entity, sameCamp, force))
                {
                    continue;
                }

                Vector3 entityPosition = entity.transform.position;
                if (Mathf.Abs(entityPosition.x - block.x) <= square_l
                    && Mathf.Abs(entityPosition.y - block.y) <= square_l
                    && _rangeSelectionSet.Add(entity))
                {
                    list.Add(entity);
                }
            }
        }
        return list;
    }
    /// <summary>
    /// Selects entities within a radius around a position.
    /// </summary>
    /// <param name="pos">Selection center.</param>
    /// <param name="selectorCamp">Camp of the selecting entity.</param>
    /// <param name="sameCamp">True to select the selector's own camp, false for the opposing camp.</param>
    /// <param name="radius">Selection radius: 0 selects nothing; a negative value selects the whole camp regardless of distance.</param>
    /// <param name="force">Force-select entities that are marked unselectable.</param>
    /// <returns>The selected entity list.</returns>
    public List<Entity> EntitySelector_Radius((float x, float y) pos, int selectorCamp, bool sameCamp, float radius, bool force)
    {
        List<Entity> list = new List<Entity>();
        if (radius == 0)
        {
            return list;
        }

        List<Entity> entityList = GetEntities(GetTargetCamp(selectorCamp, sameCamp));
        if (entityList == null)
        {
            return list;
        }

        bool ignoreDistance = radius < 0;
        float radiusSquared = radius * radius;
        for (int i = 0; i < entityList.Count; i++)
        {
            Entity entity = entityList[i];
            if (!PassesSelectability(entity, sameCamp, force))
            {
                continue;
            }

            Vector3 entityPosition = entity.transform.position;
            float deltaX = entityPosition.x - pos.x;
            float deltaY = entityPosition.y - pos.y;
            if (ignoreDistance || deltaX * deltaX + deltaY * deltaY <= radiusSquared)
            {
                list.Add(entity);
            }
        }

        return list;
    }

    /// <summary>Shared per-entity gate for the EntitySelector_* methods: active,
    /// plus (unless forced) selectable and not isolated/dormant per camp side.</summary>
    private static bool PassesSelectability(Entity entity, bool sameCamp, bool force)
    {
        return entity.Stats.IsActive
            && (force || (entity.Stats.Selectable == 0 && ((!entity.Stats.IsIsolated && sameCamp) || (!entity.Stats.IsDormant && !sameCamp))));
    }

    private static int GetTargetCamp(int selectorCamp, bool sameCamp)
    {
        return sameCamp ? selectorCamp : (selectorCamp == 2 ? 1 : 2);
    }

    private List<Entity> GetEntities(int camp)
    {
        if (camp == 2)
        {
            return _monsters;
        }

        return camp == 1 ? _turrets : null;
    }

    private List<Entity>[,] GetBlockEntities(int camp)
    {
        if (camp == 2)
        {
            return _blockMonsters;
        }

        return camp == 1 ? _blockTurrets : null;
    }

    public void BlockEntitysInitialize(int iSize, int jSize)
    {
        _blockMonsters = new List<Entity>[iSize, jSize];
        _blockTurrets = new List<Entity>[iSize, jSize];
        _iSize = iSize;
        _jSize = jSize;
        for (int i = 0; i < iSize; i++)
        {
            for (int j = 0; j < jSize; j++)
            {
                _blockMonsters[i, j] = new List<Entity>();
                _blockTurrets[i, j] = new List<Entity>();
            }
        }
    }

    public Entity GetStaticEntityInBlock(int i, int j)
    {
        if (i >= 0 && i < _iSize && j >= 0 && j < _jSize)
        {
            for (int index = 0; index < _blockTurrets[i, j].Count; index++)
            {
                if (_staticEntityLookup.Contains(_blockTurrets[i, j][index]))
                {
                    return _blockTurrets[i, j][index];
                }
            }
            for (int index = 0; index < _blockMonsters[i, j].Count; index++)
            {
                if (_staticEntityLookup.Contains(_blockMonsters[i, j][index]))
                {
                    return _blockMonsters[i, j][index];
                }
            }
            return null;
        }
        return null;
    }
    public void Initialize()
    {

    }
    public void ToStart()
    {
        _turrets = new List<Entity>();
        _monsters = new List<Entity>();
        _staticEntities = new List<Entity>();
        _rangeSelectionSet.Clear();
        _staticEntityLookup.Clear();
    }
    public void ToEnd()
    {
        OnAfterSetEntity = null;
        for (int i = _turrets.Count - 1; i >= 0; i--)
        {
            _turrets[i].thisEntityPool.Return(_turrets[i]);
        }
        for (int i = _monsters.Count - 1; i >= 0; i--)
        {
            _monsters[i].thisEntityPool.Return(_monsters[i]);
        }
    }
}
