/// <summary>
/// 目标优先级排序策略。决定 <see cref="EntityCombat.PriorityOrder"/> 如何对候选目标列表排序。
/// 作为 <see cref="EntityData.TargetPriority"/> 的类型存储在静态数据中。
/// </summary>
public enum OrderLogic
{
    ResistFirst_Priority_Des,
    Priority_Des,
    Hprate_NoFull_Asc,
    Defense_Des,
}
