using Codice.CM.Client.Differences.Merge;
using MyUI;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
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
    private GameObject _orientationImg, _shadowImg;
    private List<Entity> _turrets;
    private List<Entity> _monsters;
    private List<Entity> _staticEntities;
    private List<Entity>[,] _blockTurrets;
    private List<Entity>[,] _blockMonsters;
    private EntityPoolManager _poolManager;
    private int _iSize;
    private int _jSize;

    public bool[,] StaticEntityExistBlock
    {
        get
        {
            bool[,] se = new bool[_iSize, _jSize];
            for (int i = 0; i < _staticEntities.Count; i++)
            {
                Vector2 pos = _staticEntities[i].EntityPosition;
                se[(int)(pos.y + 0.5), (int)(pos.x + 0.5)] = true;
            }
            return se;
        }
    }
    public bool[,] MovableEntity0ExistBlock
    {
        get
        {
            bool[,] m0e = new bool[_iSize, _jSize];
            for (int i = 0; i < _iSize; i++)
            {
                for (int j = 0; j < _jSize; j++)
                {
                    for (int k = 0; k < _blockMonsters[i, j].Count; k++)
                    {
                        if (_blockTurrets[i, j][k].EntityData.MoveMethod == 0)
                        {
                            m0e[i, j] = true;
                            break;
                        }
                    }
                    if (m0e[i, j] != true)
                    {
                        for (int k = 0; k < _blockTurrets[i, j].Count; k++)
                        {
                            if (_blockTurrets[i, j][k].EntityData.MoveMethod == 0)
                            {
                                m0e[i, j] = true;
                                break;
                            }
                        }
                    }
                }
            }
            return m0e;
        }
    }
    public delegate void OperationsAfterSetEntity(Entity entity);
    public event OperationsAfterSetEntity OnAfterSetEntity;

    /// <summary>
    /// ���þ�̬ʵ��
    /// </summary>
    /// <param name="prefab">Ԥ����</param>
    /// <param name="destination">���õص�</param>
    /// <param name="camp">ʵ����Ӫ</param>
    /// <param name="orientation">���õĳ���:0-up��1-right��2-down��3-left</param>
    /// <returns>���ó���ʵ��</returns>
    public Entity SetStaticEntity(EntityID prefab_id, Vector2 destination, int camp, int orientation)
    {
        EntityPool entityPool = EntityPoolManager.Manager.FetchEntityPool(prefab_id);
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
        Debug.LogError("δ����Ԥ�����Ӧ��ʵ���");
        return null;
    }
    /// <summary>
    /// �����ƶ�ʵ��
    /// </summary>
    /// <param name="prefab">Ԥ����</param>
    /// <param name="destination">���õص�</param>
    /// <param name="camp">ʵ����Ӫ</param>
    /// <param name="pathSerial">·�����</param>
    /// <returns>���ó���ʵ��</returns>
    public Entity SetMovableEntity(EntityID prefab_id, Vector2 destination, int camp, int pathSerial)
    {
        EntityPool entityPool = EntityPoolManager.Manager.FetchEntityPool(prefab_id);
        if (entityPool != null)
        {
            Entity movableEntity = entityPool.CallOut(destination, camp);
            movableEntity.Camp = camp;
            movableEntity.MoveBase?.SetMoveParameters(pathSerial, 0, 0);
            Object.Instantiate(_shadowImg, destination, Quaternion.identity, movableEntity.TempContainer).transform.localScale *= EntityR / 0.5f;
            AddEntityToList(movableEntity, camp);
            OnAfterSetEntity?.Invoke(movableEntity);
            return movableEntity;
        }
        Debug.LogError($"δ����{prefab_id.ID_C}-{prefab_id.ID_N}��Ӧ��ʵ���");
        return null;
    }
    /// <summary>
    /// ��ָ����������ʵ���б���ɾ��ʵ��
    /// </summary>
    /// <param name="inBlocks">ָ������</param>
    /// <param name="entity">��Ҫɾ����ʵ��</param>
    /// <param name="camp">ʵ����Ӫ</param>
    public void RemoveEntityFromBlock((int i, int j)[] inBlocks, Entity entity, int camp)
    {
        for (int i = 0; i < 4; i++)
        {
            if (inBlocks[i].i == -1)
            {
                break;
            }
            else
            {
                if (camp == 2)
                {
                    _blockMonsters[inBlocks[i].i, inBlocks[i].j].Remove(entity);
                }
                else if (camp == 1)
                {
                    _blockTurrets[inBlocks[i].i, inBlocks[i].j].Remove(entity);
                }
            }
        }
    }
    /// <summary>
    /// ��ָ����������ʵ���б�������ʵ��
    /// </summary>
    /// <param name="inBlocks">ָ������</param>
    /// <param name="entity">��Ҫ���ӵ�ʵ��</param>
    /// <param name="camp">ʵ����Ӫ</param>
    public void AddEntityToBlock((int i, int j)[] inBlocks, Entity entity, int camp)
    {
        for (int i = 0; i < 4; i++)
        {
            if (inBlocks[i].i == -1)
            {
                break;
            }
            else
            {
                if (camp == 2)
                {
                    _blockMonsters[inBlocks[i].i, inBlocks[i].j].Add(entity);
                }
                else if (camp == 1)
                {
                    _blockTurrets[inBlocks[i].i, inBlocks[i].j].Add(entity);
                }
            }
        }
    }
    /// <summary>
    /// ��ʵ����б����Ƴ�
    /// </summary>
    /// <param name="entity">Ҫ�Ƴ���ʵ��</param>
    /// <param name="camp">ʵ����Ӫ</param>
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
    }
    /// <summary>
    /// ���ڷ�Χ��ʵ��ѡ����
    /// </summary>
    /// <param name="range">ѡ��Χ</param>
    /// <param name="target">Ŀ�����ͣ�1-Turret��2-Monster</param>
    /// <param name="square_l">���������εı߳���һ��</param>
    /// <param name="force">�Ƿ�ǿ��ѡ����ǿ��ѡ�����ѡȡ��ӵ�в���ѡ�����Ե�ʵ��</param>
    /// <returns>ѡ�񵽵�ʵ���б�</returns>
    public List<Entity> EntitySelector_Range((int x, int y)[] range, int selectorCamp, bool sameCamp, float square_l, bool force)
    {
        List<Entity> list = new List<Entity>();
        List<Entity>[,] blockEntities;
        int targetCamp = sameCamp ? selectorCamp : (selectorCamp == 2 ? 1 : 2);
        if (targetCamp == 2)
        {
            blockEntities = _blockMonsters;
        }
        else if (targetCamp == 1)
        {
            blockEntities = _blockTurrets;
        }
        else
        {
            blockEntities = null;
        }
        for (int index = 0; index < range.Length; index++)
        {
            List<Entity> entities = blockEntities[range[index].y, range[index].x];
            for (int jndex = 0; jndex < entities.Count; jndex++)
            {
                Entity entity = entities[jndex];
                if (entity.participateIn
                    && (force || (entity.selectable == 0 && ((!entity.isolate && sameCamp) || (!entity.dormant && !sameCamp))))
                    && Mathf.Abs(entity.transform.position.x - range[index].x) <= square_l
                    && Mathf.Abs(entity.transform.position.y - range[index].y) <= square_l
                    && !list.Contains(entity))
                {
                    list.Add(entity);
                }
            }
        }
        return list;
    }
    /// <summary>
    /// ���ڰ뾶��ʵ��ѡ����
    /// </summary>
    /// <param name="pos">ѡ������</param>
    /// <param name="target">Ŀ������:1-Turret��2-Monster</param>
    /// <param name="radius">ѡ��뾶:����0Ϊ��Чֵ��С��0Ϊȫѡ��ָ����Բ�ľ�</param>
    /// <param name="force">�Ƿ�ǿ��ѡ��:��ǿ��ѡ�����ѡȡ��ӵ�в���ѡ�����Ե�ʵ��</param>
    /// <returns>ѡ�񵽵�ʵ���б�</returns>
    public List<Entity> EntitySelector_Radius((float x, float y) pos, int selectorCamp, bool sameCamp, float radius, bool force)
    {
        List<Entity> list = new List<Entity>();
        if (radius == 0)
        {
            return list;
        }
        else
        {
            List<Entity> entityList;
            int targetCamp = sameCamp ? selectorCamp : (selectorCamp == 2 ? 1 : 2);
            if (targetCamp == 1)
            {
                entityList = _turrets;
            }
            else if (targetCamp == 2)
            {
                entityList = _monsters;
            }
            else
            {
                entityList = null;
            }
            for (int i = 0; i < entityList.Count; i++)
            {
                Entity entity = entityList[i];
                if (entity.participateIn
                   && (force || (entity.selectable == 0 && ((!entity.isolate && sameCamp) || (!entity.dormant && !sameCamp))))
                   && (radius < 0 || Vector2.Distance(new Vector2(pos.x, pos.y), entity.transform.position) <= radius))
                {
                    list.Add(entity);
                }
            }
            return list;
        }
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
                if (_staticEntities.Contains(_blockTurrets[i, j][index]))
                {
                    return _blockTurrets[i, j][index];
                }
            }
            for (int index = 0; index < _blockMonsters[i, j].Count; index++)
            {
                if (_staticEntities.Contains(_blockMonsters[i, j][index]))
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
