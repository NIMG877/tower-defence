using System;
using System.Reflection;
using NUnit.Framework;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace AbilitySystem.Tests
{
    public class AnimationMachineCastModeTests
    {
        private GameObject _root;
        private AnimationReferenceAsset _castReference;

        [TearDown]
        public void TearDown()
        {
            if (_castReference != null) UnityEngine.Object.DestroyImmediate(_castReference);
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
        }

        [TestCase(CastMode.OneShot, false)]
        [TestCase(CastMode.Sustained, true)]
        public void EnterCast_ConfiguresSpineLoopFromCastMode(CastMode mode, bool expectedLoop)
        {
            _root = new GameObject("animation_machine_cast_mode_test");
            Entity entity = _root.AddComponent<Entity>();
            AnimationMachine machine = _root.AddComponent<AnimationMachine>();

            var skeletonObject = new GameObject("skeleton");
            skeletonObject.transform.SetParent(_root.transform);
            SkeletonAnimation skeleton = skeletonObject.AddComponent<SkeletonAnimation>();
            skeleton.state = new Spine.AnimationState(new AnimationStateData(new SkeletonData()));

            _castReference = ScriptableObject.CreateInstance<AnimationReferenceAsset>();
            var castAnimation = new Spine.Animation("cast", new ExposedList<Timeline>(), 1f);
            SetPrivateField(_castReference, "animation", castAnimation);

            var animations = new AnimationSet();
            animations.SetSingle(AnimationSlot.Cast, _castReference);
            SetPrivateField(machine, "skeleton", skeleton);
            SetPrivateField(machine, "_sm", entity.StateMachine);
            SetPrivateField(machine, "_baseAnimations", animations);

            MethodInfo onStateChanged = typeof(AnimationMachine).GetMethod(
                "OnStateChanged",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(onStateChanged, Is.Not.Null);
            Action<EntityState, EntityState> handler = (previous, next) =>
                onStateChanged.Invoke(machine, new object[] { previous, next });
            entity.StateMachine.StateChanged += handler;

            Assert.That(entity.StateMachine.TrySetCastState(true, mode), Is.True);

            TrackEntry track = skeleton.state.GetCurrent(0);
            Assert.That(track, Is.Not.Null);
            Assert.That(track.Loop, Is.EqualTo(expectedLoop));
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing private field {fieldName}");
            field.SetValue(target, value);
        }
    }
}
