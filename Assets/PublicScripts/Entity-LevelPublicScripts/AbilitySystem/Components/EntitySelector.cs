using System;
using System.Collections.Generic;
using UnityEngine;

namespace AbilitySystem.Components
{
    [RegisterComponent("EntitySelector")]
    public class EntitySelector : AbilityComponentBase
    {
        private Func<string> _subjectMode;
        private Func<string> _subjectBlackboardKey;
        private Func<string> _selectionMode;
        private Func<string> _campRelation;
        private Func<float> _radius;
        private Func<float> _minRadius;
        private Func<float> _squareLength;
        private Func<bool> _force;
        private Func<bool> _excludeSubjects;
        private Func<string> _outputEntitiesKey;
        private Func<string> _outputCountKey;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            Blackboard bb = ctx.sharedBlackboard;
            _subjectMode = p.GetStringLazy("subjectMode", "self", bb);
            _subjectBlackboardKey = p.GetStringLazy("subjectBlackboardKey", "", bb);
            _selectionMode = p.GetStringLazy("selectionMode", "radius", bb);
            _campRelation = p.GetStringLazy("campRelation", "opposing", bb);
            _radius = p.GetFloatLazy("radius", 1f, bb);
            _minRadius = p.GetFloatLazy("minRadius", 0f, bb);
            _squareLength = p.GetFloatLazy("squareLength", 1f, bb);
            _force = p.GetBoolLazy("force", false, bb);
            _excludeSubjects = p.GetBoolLazy("excludeSubjects", false, bb);
            _outputEntitiesKey = p.GetStringLazy("outputEntitiesKey", "", bb);
            _outputCountKey = p.GetStringLazy("outputCountKey", "", bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (ctx.sharedBlackboard == null) return;

            List<Entity> subjects = ResolveSubjects(ctx);
            var results = new List<Entity>();
            var seen = new HashSet<Entity>();

            for (int i = 0; i < subjects.Count; i++)
            {
                // null 只可能是 self+脱离执行的快照主体占位（见 ResolveSubjects），
                // 其余分支已在各自分支内滤掉 null。
                Entity subject = subjects[i];

                List<Entity> selected = SelectForSubject(ctx, subject);
                for (int j = 0; j < selected.Count; j++)
                {
                    Entity entity = selected[j];
                    if (entity == null || (_excludeSubjects() && subjects.Contains(entity))) continue;
                    if (seen.Add(entity)) results.Add(entity);
                }
            }

            string entitiesKey = _outputEntitiesKey();
            if (!string.IsNullOrEmpty(entitiesKey))
            {
                ctx.sharedBlackboard.Remove(entitiesKey);
                ctx.sharedBlackboard.Set(entitiesKey, results);
            }

            string countKey = _outputCountKey();
            if (!string.IsNullOrEmpty(countKey))
            {
                ctx.sharedBlackboard.Remove(countKey);
                ctx.sharedBlackboard.Set(countKey, results.Count.ToString());
            }
        }

        private List<Entity> ResolveSubjects(AbilityContext ctx)
        {
            switch (Normalize(_subjectMode()))
            {
                case "blackboard":
                case "blackboardentities":
                    string key = _subjectBlackboardKey();
                    if (string.IsNullOrEmpty(key)) return new List<Entity>();
                    var source = ctx.sharedBlackboard.Get<List<Entity>>(key, null);
                    var live = new List<Entity>();
                    if (source != null)
                        for (int i = 0; i < source.Count; i++)
                            if (source[i] != null) live.Add(source[i]);
                    return live;
                case "eventtarget":
                    Entity eventTarget = GetEventTarget(ctx.currentEvent);
                    return eventTarget == null ? new List<Entity>() : new List<Entity> { eventTarget };
                case "self":
                    // 脱离执行没有活实体（ctx.entity==null），镜像 SpawnEntity
                    // positionMode=self 的 fork 快照约定——主体退化为快照的
                    // (position, camp)，以 null 占位下传，仅纯位置选择可用。
                    if (ctx.entity != null) return new List<Entity> { ctx.entity };
                    return ctx.stepExecution?.DetachedSnapshot != null
                        ? new List<Entity> { null }
                        : new List<Entity>();
                default:
                    Debug.LogError($"[EntitySelector] Unknown subjectMode '{_subjectMode()}' (expected self/eventTarget/blackboard).");
                    return new List<Entity>();
            }
        }

        private List<Entity> SelectForSubject(AbilityContext ctx, Entity subject)
        {
            switch (Normalize(_selectionMode()))
            {
                case "vision":
                    if (subject == null)
                    {
                        Debug.LogError("[EntitySelector] selectionMode 'vision' requires a live entity; unavailable in detached executions.");
                        return new List<Entity>();
                    }
                    return SelectVision(subject);
                case "range":
                    if (subject == null)
                    {
                        Debug.LogError("[EntitySelector] selectionMode 'range' requires a live entity; unavailable in detached executions.");
                        return new List<Entity>();
                    }
                    return SelectRange(subject);
                case "ring":
                    return SelectRing(ctx, subject);
                case "all":
                    return SelectAll(ctx, subject);
                case "radius":
                    return SelectRadius(ctx, subject);
                default:
                    Debug.LogError($"[EntitySelector] Unknown selectionMode '{_selectionMode()}' (expected radius/ring/range/vision/all).");
                    return new List<Entity>();
            }
        }

        /// <summary>选择主体的锚点（位置/阵营）。活实体读现场坐标；脱离执行的
        /// 快照主体读 fork 时的 (position, camp)——延迟后实体可能已被池回收。</summary>
        private bool TryResolveAnchor(AbilityContext ctx, Entity subject, out Vector2 position, out int camp)
        {
            if (subject != null)
            {
                position = subject.transform.position;
                camp = subject.Camp;
                return true;
            }
            DetachedExecutionSnapshot snapshot = ctx.stepExecution?.DetachedSnapshot;
            if (snapshot != null)
            {
                position = snapshot.position;
                camp = snapshot.camp;
                return true;
            }
            position = default;
            camp = default;
            return false;
        }

        private List<Entity> SelectVision(Entity subject)
        {
            var results = new List<Entity>();
            if (subject.Vision == null) return results;

            AddByRelation(
                results,
                sameCamp => sameCamp
                    ? (subject.Camp == 1 ? subject.Vision.NearbyTurrets : subject.Vision.NearbyMonsters)
                    : (subject.Camp == 1 ? subject.Vision.NearbyMonsters : subject.Vision.NearbyTurrets));
            return results;
        }

        private List<Entity> SelectRange(Entity subject)
        {
            var results = new List<Entity>();
            if (subject.Vision?.Range == null || EntityManager.Manager == null) return results;

            AddByRelation(
                results,
                sameCamp => EntityManager.Manager.EntitySelector_Range(
                    subject.Vision.Range, subject.Camp, sameCamp, _squareLength(), _force()));
            return results;
        }

        private List<Entity> SelectRadius(AbilityContext ctx, Entity subject)
        {
            var results = new List<Entity>();
            if (EntityManager.Manager == null
                || !TryResolveAnchor(ctx, subject, out Vector2 position, out int camp)) return results;

            AddByRelation(
                results,
                sameCamp => EntityManager.Manager.EntitySelector_Radius(
                    (position.x, position.y), camp, sameCamp, _radius(), _force()));
            return results;
        }

        private List<Entity> SelectAll(AbilityContext ctx, Entity subject)
        {
            var results = new List<Entity>();
            if (EntityManager.Manager == null
                || !TryResolveAnchor(ctx, subject, out Vector2 position, out int camp)) return results;

            AddByRelation(
                results,
                sameCamp => EntityManager.Manager.EntitySelector_Radius(
                    (position.x, position.y), camp, sameCamp, -1f, _force()));
            return results;
        }

        private List<Entity> SelectRing(AbilityContext ctx, Entity subject)
        {
            List<Entity> results = SelectRadius(ctx, subject);
            float minRadius = Math.Max(0f, _minRadius());
            if (minRadius <= 0f || results.Count == 0) return results;
            if (!TryResolveAnchor(ctx, subject, out Vector2 center, out _)) return results;

            float minRadiusSquared = minRadius * minRadius;
            results.RemoveAll(entity =>
            {
                var position = entity.transform.position;
                float deltaX = position.x - center.x;
                float deltaY = position.y - center.y;
                return deltaX * deltaX + deltaY * deltaY <= minRadiusSquared;
            });
            return results;
        }

        private void AddByRelation(List<Entity> results, Func<bool, List<Entity>> selector)
        {
            string relation = Normalize(_campRelation());
            if (relation == "same" || relation == "both") AddUnique(results, selector(true));
            if (relation == "opposing" || relation == "both") AddUnique(results, selector(false));
        }

        private static Entity GetEventTarget(AbilityEvent evt)
        {
            return evt is DamageEventBase damageEvent ? damageEvent.target : null;
        }

        private static void AddUnique(List<Entity> destination, List<Entity> source)
        {
            if (source == null) return;
            for (int i = 0; i < source.Count; i++)
            {
                Entity entity = source[i];
                if (entity != null && !destination.Contains(entity)) destination.Add(entity);
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.Trim().ToLowerInvariant();
        }
    }
}
