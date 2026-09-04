using System.Collections.Generic;
using System.Reflection;
using AbilitySystem.Components;
using NUnit.Framework;
using UnityEngine;

namespace AbilitySystem.Tests
{
    public class EntitySelectorVisionTests
    {
        private readonly List<GameObject> _scratch = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _scratch.Count; i++)
                Object.DestroyImmediate(_scratch[i]);
            _scratch.Clear();
        }

        [TestCase(1, "same")]
        [TestCase(1, "opposing")]
        [TestCase(1, "both")]
        [TestCase(2, "same")]
        [TestCase(2, "opposing")]
        [TestCase(2, "both")]
        public void VisionSelection_UsesRequestedCampRelation(int subjectCamp, string relation)
        {
            Entity subject = NewEntity("subject");
            Entity opposing = NewEntity("opposing");
            SetPrivateField(subject, "_camp", subjectCamp);

            var vision = new EntityVision(subject);
            var monsters = new List<Entity>();
            var turrets = new List<Entity>();
            if (subjectCamp == 1)
            {
                turrets.Add(subject);
                monsters.Add(opposing);
            }
            else
            {
                monsters.Add(subject);
                turrets.Add(opposing);
            }
            SetPrivateField(vision, "_monstersInRange", monsters);
            SetPrivateField(vision, "_turretsInRange", turrets);
            SetPrivateField(subject, "_vision", vision);

            var bb = new Blackboard();
            var ctx = new AbilityContext { entity = subject, sharedBlackboard = bb };
            var selector = new EntitySelector();
            selector.OnInit(ctx, Params(
                ("selectionMode", "vision"),
                ("campRelation", relation),
                ("outputEntitiesKey", "targets")));

            selector.OnTrigger(ctx);

            List<Entity> targets = bb.Get<List<Entity>>("targets", null);
            Assert.That(targets, Is.Not.Null);
            if (relation == "same")
            {
                Assert.That(targets, Is.EqualTo(new[] { subject }));
            }
            else if (relation == "opposing")
            {
                Assert.That(targets, Is.EqualTo(new[] { opposing }));
            }
            else
            {
                Assert.That(targets, Is.EqualTo(new[] { subject, opposing }));
            }
        }

        [Test]
        public void CampTwoOpposing_WhenOnlySubjectIsVisible_WritesZeroCount()
        {
            Entity subject = NewEntity("creeper");
            SetPrivateField(subject, "_camp", 2);

            var vision = new EntityVision(subject);
            SetPrivateField(vision, "_monstersInRange", new List<Entity> { subject });
            SetPrivateField(vision, "_turretsInRange", new List<Entity>());
            SetPrivateField(subject, "_vision", vision);

            var bb = new Blackboard();
            var ctx = new AbilityContext { entity = subject, sharedBlackboard = bb };
            var selector = new EntitySelector();
            selector.OnInit(ctx, Params(
                ("selectionMode", "vision"),
                ("campRelation", "opposing"),
                ("outputCountKey", "target_count")));

            selector.OnTrigger(ctx);

            Assert.That(bb.Get("target_count", "missing"), Is.EqualTo("0"));
        }

        private Entity NewEntity(string name)
        {
            var go = new GameObject(name);
            _scratch.Add(go);
            return go.AddComponent<Entity>();
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing private field '{name}' on {target.GetType().Name}");
            field.SetValue(target, value);
        }

        private static ParamList Params(params (string key, string value)[] values)
        {
            var entries = new ParamEntry[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                entries[i] = new ParamEntry
                {
                    key = values[i].key,
                    value = values[i].value,
                    type = ParamValueType.String,
                };
            }
            return new ParamList { entries = entries };
        }
    }
}
