/// <summary>
/// 实体逻辑状态机状态（EntityStateMachine 持有）。
/// 声明顺序即转换优先级（见 EntityStateMachine.PriorityOf：Default<Idle<Move<Attack<Start<Cast<Die）。
/// 注：动画资源槽位由 <see cref="AnimationSlot"/> 标识，与本枚举的语义不重合。
/// </summary>
public enum EntityState
{
    Default,
    Idle,
    Move,
    Attack,
    Start,
    Cast,
    Die,
}
