/// <summary>
/// 实体动画状态机状态。
/// 替代 AnimationMachine 旧版"内部 enum 索引"和"公开 int 映射"两套不一致的编码。
/// 注：动画资源槽编码（0/1/2/30/31/32/33/4/5，见 AnimationMachine.ResetAnimation）是另一套码表，
///     与本枚举的语义不重合，<see cref="ResetAnimation"/> 仍使用 int[] 形参。
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
