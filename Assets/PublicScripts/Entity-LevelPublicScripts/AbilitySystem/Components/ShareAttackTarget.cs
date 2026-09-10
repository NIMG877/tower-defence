using System;
using System.Collections.Generic;
using UnityEngine;

namespace AbilitySystem.Components
{
    public sealed class SharedAttackRequest
    {
        public readonly Entity Sender;
        public readonly Entity Target;

        public SharedAttackRequest(Entity sender, Entity target)
        {
            Sender = sender;
            Target = target;
        }
    }

    [RegisterComponent("ShareAttackTarget")]
    public class ShareAttackTarget : AbilityComponentBase
    {
        private Func<string> _recipientKey;
        private Func<string> _receiverAbilityId;
        private Func<string> _queueKey;
        private Func<string> _activeSourceKey;
        private Func<string> _bulletPrefabResource;
        private Func<string> _bulletTrailResource;
        private Func<float> _bulletSpeed;

        public override void OnInit(AbilityContext ctx, ParamList p)
        {
            Blackboard bb = ctx.sharedBlackboard;
            _recipientKey = p.GetStringLazy("blackboardKey", "", bb);
            _receiverAbilityId = p.GetStringLazy("receiverAbilityId", "", bb);
            _queueKey = p.GetStringLazy("queueKey", "shared_attack_requests", bb);
            _activeSourceKey = p.GetStringLazy("activeSourceKey", "shared_attack_source", bb);
            _bulletPrefabResource = p.GetStringLazy("bulletPrefabResource", "", bb);
            _bulletTrailResource = p.GetStringLazy("bulletTrailResource", "", bb);
            _bulletSpeed = p.GetFloatLazy("bulletSpeed", 0f, bb);
        }

        public override void OnTrigger(AbilityContext ctx)
        {
            if (!(ctx.currentEvent is BeforeAttackEvent attackEvent) || attackEvent.target == null
                || ctx.entity == null || ctx.sharedBlackboard == null)
                return;

            string recipientKey = _recipientKey();
            if (string.IsNullOrEmpty(recipientKey)) return;
            List<Entity> recipients = ctx.sharedBlackboard.Get<List<Entity>>(recipientKey, null);
            if (recipients == null || recipients.Count == 0) return;

            Entity activeSource = ctx.sharedBlackboard.Get<Entity>(_activeSourceKey(), null);
            GameObject bulletPrefab = LoadResource(_bulletPrefabResource());
            GameObject trailPrefab = LoadResource(_bulletTrailResource());
            bool canSpawnBullet = bulletPrefab != null && trailPrefab != null && _bulletSpeed() > 0f;
            if (!canSpawnBullet)
            {
                OneShotWarn.WarnOnce(
                    "share-attack-target-bullet",
                    "ShareAttackTarget: communication bullet resources/speed are invalid; delivering requests immediately.");
            }

            var seen = new HashSet<Entity>();
            for (int i = 0; i < recipients.Count; i++)
            {
                Entity recipient = recipients[i];
                if (recipient == null || recipient == ctx.entity || recipient == activeSource || !seen.Add(recipient)) continue;
                if (!HasAbility(recipient, _receiverAbilityId())) continue;

                Entity sender = ctx.entity;
                Entity sharedTarget = attackEvent.target;
                if (!canSpawnBullet)
                {
                    Deliver(recipient, sender, sharedTarget, _queueKey());
                    continue;
                }

                var bulletData = ScriptableObject.CreateInstance<BulletData>();
                bulletData.hideFlags = HideFlags.HideAndDontSave;
                bulletData.BulletPrefab = bulletPrefab;
                bulletData.BulletTrailPrefab = trailPrefab;
                bulletData.BulletSpeed = _bulletSpeed();
                bulletData.BulletType = 0;
                bulletData.AllowNoTarget = false;
                new Bullet(
                    null,
                    (target, multiplier, defPenetrate, mgrPenetrate, defPenetrateValue,
                        mgrPenetrateValue, damageType, applyType, isDeadly) =>
                        Deliver(recipient, sender, sharedTarget, _queueKey()),
                    null,
                    bulletData,
                    sender,
                    recipient,
                    Vector2.zero,
                    sender.Movement.Position,
                    1f, 0f, 0f, 0f, 0f, 0f, 0, 0);
                UnityEngine.Object.Destroy(bulletData);
            }
        }

        private static void Deliver(Entity recipient, Entity sender, Entity target, string queueKey)
        {
            if (recipient == null || recipient.Stats == null || !recipient.Stats.IsActive
                || target == null || string.IsNullOrEmpty(queueKey) || recipient.AbilityRunner == null)
                return;
            if (CanCurrentlySee(recipient, target)) return;

            Blackboard bb = recipient.AbilityRunner.sharedBlackboard;
            var queue = bb.Get<List<SharedAttackRequest>>(queueKey, null) ?? new List<SharedAttackRequest>();
            queue.Add(new SharedAttackRequest(sender, target));
            bb.Set(queueKey, queue);
        }

        private static bool CanCurrentlySee(Entity recipient, Entity target)
        {
            if (recipient.Vision == null) return false;
            List<Entity> enemies = recipient.Camp == 1
                ? recipient.Vision.NearbyMonsters
                : recipient.Vision.NearbyTurrets;
            return enemies != null && enemies.Contains(target);
        }

        private static bool HasAbility(Entity entity, string abilityId)
        {
            if (entity?.AbilityRunner == null) return false;
            if (string.IsNullOrEmpty(abilityId)) return true;
            return ContainsAbility(entity.AbilityRunner.Skills, abilityId)
                || ContainsAbility(entity.AbilityRunner.Talents, abilityId)
                || ContainsAbility(entity.AbilityRunner.ExtraAbilities, abilityId);
        }

        private static bool ContainsAbility(IReadOnlyList<AbilityRuntime> abilities, string abilityId)
        {
            if (abilities == null) return false;
            for (int i = 0; i < abilities.Count; i++)
            {
                AbilityRuntime ability = abilities[i];
                if (ability?.config != null && ability.config.abilityId == abilityId) return true;
            }
            return false;
        }

        private static GameObject LoadResource(string path)
        {
            return string.IsNullOrEmpty(path) ? null : Resources.Load<GameObject>(path);
        }
    }
}
