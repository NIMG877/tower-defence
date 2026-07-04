using System;
using System.Collections.Generic;

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
                Entity subject = subjects[i];
                if (subject == null) continue;

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
                    return string.IsNullOrEmpty(key)
                        ? new List<Entity>()
                        : ctx.sharedBlackboard.Get<List<Entity>>(key, null) ?? new List<Entity>();
                case "eventtarget":
                    Entity eventTarget = GetEventTarget(ctx.currentEvent);
                    return eventTarget == null ? new List<Entity>() : new List<Entity> { eventTarget };
                default:
                    return ctx.entity == null ? new List<Entity>() : new List<Entity> { ctx.entity };
            }
        }

        private List<Entity> SelectForSubject(AbilityContext ctx, Entity subject)
        {
            switch (Normalize(_selectionMode()))
            {
                case "subject":
                    return new List<Entity> { subject };
                case "eventtarget":
                    Entity eventTarget = GetEventTarget(ctx.currentEvent);
                    return eventTarget == null ? new List<Entity>() : new List<Entity> { eventTarget };
                case "vision":
                    return SelectVision(subject);
                case "range":
                    return SelectRange(subject);
                case "ring":
                    return SelectRing(subject);
                default:
                    return SelectRadius(subject);
            }
        }

        private List<Entity> SelectVision(Entity subject)
        {
            var results = new List<Entity>();
            if (subject.Vision == null) return results;

            string relation = Normalize(_campRelation());
            if (relation == "same" || relation == "both")
            {
                AddUnique(results, subject.Camp == 1 ? subject.Vision.NearbyTurrets : subject.Vision.NearbyMonsters);
            }
            if (relation == "opposing" || relation == "both")
            {
                AddUnique(results, subject.Camp == 1 ? subject.Vision.NearbyMonsters : subject.Vision.NearbyTurrets);
            }
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

        private List<Entity> SelectRadius(Entity subject)
        {
            var results = new List<Entity>();
            if (EntityManager.Manager == null) return results;

            var position = subject.transform.position;
            AddByRelation(
                results,
                sameCamp => EntityManager.Manager.EntitySelector_Radius(
                    (position.x, position.y), subject.Camp, sameCamp, _radius(), _force()));
            return results;
        }

        private List<Entity> SelectRing(Entity subject)
        {
            List<Entity> results = SelectRadius(subject);
            float minRadius = Math.Max(0f, _minRadius());
            if (minRadius <= 0f) return results;

            float minRadiusSquared = minRadius * minRadius;
            var center = subject.transform.position;
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
