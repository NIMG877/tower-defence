using System.Collections.Generic;

/// <summary>
/// 实体战斗子系统（POCO）。
/// 持有：
///   1. AttackBase 引用（sibling MonoBehaviour，保留作为子组件桥）
///   2. EntityUpdate 方法（原 Entity.EntityUpDate）—— 过滤不可参与或不可选择的实体
///   3. PriorityOrder 方法（原 Entity.PriorityOrder）—— 按 OrderLogic 排序
///
/// 设计要点：
///   - POCO，构造接受 Entity 引用。
///   - 需要访问其他子系统的属性：Stats.IsActive / Stats.Selectable / Stats.CurrentHpRate / Movement.ResistList / Movement.Priority / Movement.Camp。
///   - 其他实体的属性通过 `entity.Stats.X` / `entity.Movement.X` 访问（不再用旧的 `entity.participateIn` / `entity.entityResistList` 等字段）。
/// </summary>
public class EntityCombat
{
    private readonly Entity _entity;

    public EntityCombat(Entity entity)
    {
        _entity = entity;
    }

    /// <summary>
    /// 替代原 Entity.EntityUpDate。过滤掉不可参与（IsActive=false）或不可选择（Selectable!=0）的实体。
    /// </summary>
    public Entity[] EntityUpdate(Entity[] entitiesA)
    {
        List<Entity> entities = new List<Entity>(entitiesA);
        for (int i = entities.Count - 1; i >= 0; i--)
        {
            if (entities[i].Stats.IsActive == false || entities[i].Stats.Selectable != 0)
            {
                entities.RemoveAt(i);
            }
        }
        return entities.ToArray();
    }

    /// <summary>
    /// 替代原 Entity.PriorityOrder。按 OrderLogic 对实体列表排序。
    /// </summary>
    public List<Entity> PriorityOrder(List<Entity> originList, OrderLogic orderLogic)
    {
        List<Entity> entitiesList = new List<Entity>(originList);
        List<Entity> resistList = _entity.Movement.ResistList;
        int myCamp = _entity.Movement.Camp;
        switch (orderLogic)
        {
            case OrderLogic.ResistFirst_Priority_Des:
                for (int i = 0; i < resistList.Count; i++)
                {
                    if (resistList[i].Stats.Selectable == 0 && !entitiesList.Contains(resistList[i]))
                    {
                        entitiesList.Add(resistList[i]);
                    }
                }
                for (int i = 0; i < entitiesList.Count - 1; i++)
                {
                    for (int j = i + 1; j < entitiesList.Count; j++)
                    {
                        bool containI = resistList.Contains(entitiesList[i]);
                        bool containJ = resistList.Contains(entitiesList[j]);
                        if ((!containI && containJ) || (containI == containJ && entitiesList[i].Movement.Priority < entitiesList[j].Movement.Priority))
                        {
                            (entitiesList[i], entitiesList[j]) = (entitiesList[j], entitiesList[i]);
                        }
                    }
                }
                break;
            case OrderLogic.Priority_Des:
                entitiesList.Sort((x, y) => -x.Movement.Priority.CompareTo(y.Movement.Priority));
                break;
            case OrderLogic.Hprate_NoFull_Asc:
                for (int i = entitiesList.Count - 1; i >= 0; i--)
                {
                    if (entitiesList[i].Stats.CurrentHpRate >= 1)
                    {
                        entitiesList.RemoveAt(i);
                    }
                }
                entitiesList.Sort((x, y) => x.Stats.CurrentHpRate.CompareTo(y.Stats.CurrentHpRate));
                break;
            case OrderLogic.ResistFirst_OtherCampFirst_Priority_Des:
                for (int i = 0; i < entitiesList.Count - 1; i++)
                {
                    for (int j = i + 1; j < entitiesList.Count; j++)
                    {
                        bool containI = resistList.Contains(entitiesList[i]);
                        bool containJ = resistList.Contains(entitiesList[j]);
                        bool isTurretI = entitiesList[i].Movement.Camp != myCamp;
                        bool isTurretJ = entitiesList[j].Movement.Camp != myCamp;
                        if ((!containI && containJ) || (containI == containJ && entitiesList[i].Movement.Priority < entitiesList[j].Movement.Priority))
                        {
                            (entitiesList[i], entitiesList[j]) = (entitiesList[j], entitiesList[i]);
                        }
                    }
                }
                break;
            default: break;
        }
        return entitiesList;
    }
}
