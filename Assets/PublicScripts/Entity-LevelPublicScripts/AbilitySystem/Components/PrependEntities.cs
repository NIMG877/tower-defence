using System;
using System.Collections.Generic;

namespace AbilitySystem.Components
{
    /// <summary>
    /// 把黑板 sourceKey 处的 List&lt;Entity&gt; 去重后插到 blackboardKey 处目标列表的最前
    /// （原地，目标列表对象不变——下游组件继续读同一键）。去重是"提权"语义：源实体按源顺序
    /// 整体排到最前，目标里原有的重复项从原位移除（而不是保留在后面），因此典型用法——
    /// 攻击偏好流水线中 write_blackboard（source=event path=targets 候选副本）之后、
    /// attack_candidate_override 提交之前，把"优先目标"列表前插（如 eyjafjalla_t1 的泡泡）——
    /// 已在候选里的优先目标会被提到最前，而非重复计入。
    /// </summary>
    [RegisterComponent("PrependEntities")]
    public class PrependEntities : AbilityComponentBase
    {
        private Func<string> _blackboardKey;
        private Func<string> _sourceKey;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            _blackboardKey = p.GetStringLazy("blackboardKey", "", ctx.sharedBlackboard);
            _sourceKey = p.GetStringLazy("sourceKey", "", ctx.sharedBlackboard);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (ctx.sharedBlackboard == null) return;

            string targetKey = _blackboardKey();
            if (string.IsNullOrEmpty(targetKey))
            {
                OneShotWarn.WarnOnce("prepend-entities-key",
                    "PrependEntities: blackboardKey is required; skipping.");
                return;
            }
            string sourceKey = _sourceKey();
            if (string.IsNullOrEmpty(sourceKey))
            {
                OneShotWarn.WarnOnce("prepend-entities-source",
                    "PrependEntities: sourceKey is required; skipping.");
                return;
            }

            List<Entity> target = ctx.sharedBlackboard.Get<List<Entity>>(targetKey, null);
            if (target == null)
            {
                OneShotWarn.WarnOnce("prepend-entities:" + targetKey,
                    $"PrependEntities: blackboard key '{targetKey}' holds no entity list; skipping.");
                return;
            }
            List<Entity> source = ctx.sharedBlackboard.Get<List<Entity>>(sourceKey, null);
            if (source == null)
            {
                OneShotWarn.WarnOnce("prepend-entities:" + sourceKey,
                    $"PrependEntities: blackboard key '{sourceKey}' holds no entity list; skipping.");
                return;
            }

            // 前插段：源实体按源顺序去重（源内去重 + null 跳过；目标重复项稍后从原位移除）。
            List<Entity> front = new List<Entity>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                Entity entity = source[i];
                if (entity == null || front.Contains(entity)) continue;
                front.Add(entity);
            }
            if (front.Count == 0) return;

            target.RemoveAll(front.Contains);
            target.InsertRange(0, front);
        }
    }
}
