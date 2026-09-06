using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace MyUI
{
    /// <summary>战斗飘字类型，颜色与文本格式统一在 <see cref="LevelMessageCombatModule.StyleFor"/>。</summary>
    public enum CombatTextKind
    {
        Damage,
        Heal,
        AddCost,
        ReduceCost,
        SpAdd,
        Miss,
    }

    /// <summary>
    /// Owns combat event subscriptions, floating combat text, and settlement statistics.
    /// </summary>
    internal sealed class LevelMessageCombatModule
    {
        private readonly Transform _textRoot;
        private readonly List<TextMeshProUGUI> _textPool = new List<TextMeshProUGUI>();
        private readonly HashSet<TextMeshProUGUI> _borrowedTexts = new HashSet<TextMeshProUGUI>();
        private readonly HashSet<Entity> _registeredEntities = new HashSet<Entity>();
        private readonly Dictionary<EntityID, int> _statisticsIndex =
            new Dictionary<EntityID, int>();

        private DamageStatisticData[] _damageStatistics;

        public DamageStatisticData[] DamageStatistics => _damageStatistics;

        public LevelMessageCombatModule(GameObject root)
        {
            _textRoot = LevelMessageViewLookup.Get<Transform>(root, "texts");
            _textPool.Add(LevelMessageViewLookup.Get<TextMeshProUGUI>(root, "texts/floatingText"));
            // 共享单池按全场并发峰值增长，预热 8 个（原 6 类型 × 各 4 个 = 30 个常驻）
            for (int i = 0; i < 8; i++)
                _textPool.Add(Object.Instantiate(_textPool[0], _textRoot));
        }

        public void OnEnter(EntityID[] characters)
        {
            EntityManager.Manager.OnAfterSetEntity -= RegisterEntity;
            EntityManager.Manager.OnAfterSetEntity += RegisterEntity;

            _statisticsIndex.Clear();
            _damageStatistics = new DamageStatisticData[characters.Length];
            for (int i = 0; i < characters.Length; i++)
            {
                _statisticsIndex[characters[i]] = i;
                _damageStatistics[i] = new DamageStatisticData(
                    characters[i],
                    new float[3],
                    new float[1],
                    new float[3]);
            }
        }

        public void OnExit()
        {
            ResetFloatingTexts();
            EntityManager.Manager.OnAfterSetEntity -= RegisterEntity;
            foreach (Entity entity in _registeredEntities)
            {
                if (entity != null)
                    entity.OnDamageResolved -= HandleDamageResolved;
            }
            _registeredEntities.Clear();
            _statisticsIndex.Clear();
        }

        public void OnPause()
        {
            ResetFloatingTexts();
        }

        public void ShowText(Vector2 entityPosition, CombatTextKind kind, int value)
        {
            TextMeshProUGUI text;
            if (_textPool.Count > 1)
            {
                text = _textPool[1];
                _textPool.RemoveAt(1);
            }
            else
            {
                text = Object.Instantiate(_textPool[0], _textRoot);
            }
            _borrowedTexts.Add(text);

            (Color color, string content) = StyleFor(kind, value);
            text.color = color;
            text.text = content;

            text.transform.position = entityPosition + 1.2f * Vector2.up + 0.06f * Random.insideUnitCircle;
            text.transform.localScale = Vector3.zero;
            text.gameObject.SetActive(true);
            text.transform.DOScale(1, 0.2f)
                .SetUpdate(true)
                .SetId("LevelMessagePanel")
                .OnComplete(() => HideTextAfterDelay(text).Forget());
        }

        /// <summary>类型 → 颜色与文本。颜色取自原 6 个样本节点的预制体值；
        /// 未知类型直接抛出，不静默落默认样式。</summary>
        private static (Color color, string content) StyleFor(CombatTextKind kind, int value)
        {
            switch (kind)
            {
                case CombatTextKind.Damage:
                    return (new Color(0.7264151f, 0f, 0f), value.ToString());
                case CombatTextKind.Heal:
                    return (new Color(0f, 0.745283f, 0f), "+" + value);
                case CombatTextKind.AddCost:
                    return (new Color(0.81960785f, 0.580853f, 0f), "COST " + value);
                case CombatTextKind.ReduceCost:
                    return (new Color(0.8773585f, 0f, 0f), "COST- " + value);
                case CombatTextKind.SpAdd:
                    return (new Color(0f, 0.5386607f, 0.8584906f), "SP " + value);
                case CombatTextKind.Miss:
                    return (new Color(0.9056604f, 0.83616626f, 0f), "MISS");
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public void AcceptDamageMessage(
            Entity target,
            Entity origin,
            float finalDamage,
            int damageType)
        {
            if (damageType >= 0 && damageType < 3)
                RecordDamage(target, origin, finalDamage, damageType);
            else
                RecordHealing(origin, finalDamage);
        }

        private async UniTask HideTextAfterDelay(TextMeshProUGUI text)
        {
            try
            {
                await UniTask.WaitForSeconds(
                    0.36f,
                    true,
                    PlayerLoopTiming.Update,
                    LevelResourceSharing.LevelCtk);
            }
            catch (System.OperationCanceledException)
            {
                ReturnText(text);
                return;
            }

            if (!_borrowedTexts.Contains(text))
                return;
            text.transform.DOScale(0, 0.2f)
                .SetUpdate(true)
                .SetId("LevelMessagePanel")
                .OnComplete(() => ReturnText(text));
        }

        private void ReturnText(TextMeshProUGUI text)
        {
            if (text == null || !_borrowedTexts.Remove(text))
                return;
            text.transform.DOKill();
            text.transform.localScale = Vector3.zero;
            text.gameObject.SetActive(false);
            _textPool.Add(text);
        }

        private void ResetFloatingTexts()
        {
            foreach (TextMeshProUGUI text in _borrowedTexts)
            {
                if (text == null)
                    continue;
                text.transform.DOKill();
                text.transform.localScale = Vector3.zero;
                text.gameObject.SetActive(false);
                _textPool.Add(text);
            }
            _borrowedTexts.Clear();
        }

        private void RecordDamage(Entity target, Entity origin, float amount, int damageType)
        {
            if (damageType < 0 || damageType >= 3)
                return;
            if (TryGetStatisticIndex(target, out int targetIndex))
                _damageStatistics[targetIndex].DamageReceive[damageType] += amount;
            if (TryGetStatisticIndex(origin, out int originIndex))
                _damageStatistics[originIndex].Damage[damageType] += amount;
        }

        private void RecordHealing(Entity origin, float amount)
        {
            if (TryGetStatisticIndex(origin, out int originIndex))
                _damageStatistics[originIndex].Healing[0] += amount;
        }

        private bool TryGetStatisticIndex(Entity entity, out int index)
        {
            index = -1;
            return entity != null &&
                   _statisticsIndex.TryGetValue(entity.EntityData.ID, out index);
        }

        private void RegisterEntity(Entity entity)
        {
            if (entity == null)
                return;

            // Dormancy clears entity events, so pooled entities must be registered on every checkout.
            entity.OnDamageResolved -= HandleDamageResolved;
            entity.OnDamageResolved += HandleDamageResolved;
            _registeredEntities.Add(entity);
        }

        private void HandleDamageResolved(DamageResolution resolution)
        {
            Entity target = resolution.Target;
            if (target == null)
                return;

            switch (resolution.Kind)
            {
                case DamageResolutionKind.Dodged:
                    ShowText(target.transform.position, CombatTextKind.Miss, 0);
                    break;
                case DamageResolutionKind.Damage:
                    RecordDamage(
                        target,
                        resolution.Origin,
                        resolution.Amount,
                        resolution.DamageType);
                    if (resolution.IsCritical)
                        ShowText(target.transform.position, CombatTextKind.Damage, (int)resolution.Amount);
                    break;
                case DamageResolutionKind.Healing:
                    RecordHealing(
                        resolution.Origin,
                        resolution.Amount);
                    ShowText(target.Movement.Position, CombatTextKind.Heal, (int)resolution.Amount);
                    break;
            }
        }
    }
}
