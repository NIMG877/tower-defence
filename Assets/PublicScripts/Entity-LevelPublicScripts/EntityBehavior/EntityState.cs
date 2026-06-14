/// <summary>
/// 实体动画状态机状态。
/// 替代 AnimationMachine 旧版"内部 enum 索引"和"公开 int 映射"两套不一致的编码。
/// 注：动画资源槽位由 <see cref="AnimationSlot"/> 标识，与本枚举的语义不重合。
/// </summary>
public enum EntityState
{
    Default,
    Idle,
    Move,
    Attack_Wait,
    Attack,
    Start,
    Die,
}
