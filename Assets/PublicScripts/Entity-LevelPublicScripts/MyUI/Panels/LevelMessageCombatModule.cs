using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Pool;

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
        // 场景模板节点，只作克隆源、永不借出（原实现占 list[0] 的隐式约定改为显式字段）
        private readonly TextMeshProUGUI _textTemplate;
        private readonly ObjectPool<TextMeshProUGUI> _textPool;
        // 在展示中的飘字注册表：暂停/退出时据此全量收回；归还仅由补间链终点触发
        private readonly HashSet<TextMeshProUGUI> _borrowedTexts = new HashSet<TextMeshProUGUI>();
        private readonly HashSet<Entity> _registeredEntities = new HashSet<Entity>();
        private readonly Dictionary<EntityID, int> _statisticsIndex =
            new Dictionary<EntityID, int>();

        private DamageStatisticData[] _damageStatistics;

        public DamageStatisticData[] DamageStatistics => _damageStatistics;

        public LevelMessageCombatModule(GameObject root)
        {
            _textRoot = LevelMessageViewLookup.Get<Transform>(root, "texts");
            _textTemplate = LevelMessageViewLookup.Get<TextMeshProUGUI>(root, "texts/floatingText");
            _textPool = new ObjectPool<TextMeshProUGUI>(
                () => Object.Instantiate(_textTemplate, _textRoot),
                actionOnRelease: ResetText,
                collectionCheck: true);
            // 共享单池按全场并发峰值增长，预热 8 个（原 6 类型 × 各 4 个 = 30 个常驻）
            for (int i = 0; i < 8; i++)
                _textPool.Release(_textPool.Get());
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
            TextMeshProUGUI text = _textPool.Get();
            _borrowedTexts.Add(text);

            (Color color, string content) = StyleFor(kind, value);
            text.color = color;
            text.text = content;

            text.transform.position = entityPosition + 1.2f * Vector2.up + 0.06f * Random.insideUnitCircle;
            text.transform.localScale = Vector3.zero;
            text.gameObject.SetActive(true);
            // 弹出→停顿→收缩是同一条补间链（延时挂在收缩补间上）：暂停/退出由
            // ResetText 的 DOKill 与面板 Kill("LevelMessagePanel") 整链取消，
            // 归还只发生在链正常走完时
            text.transform.DOScale(1, 0.2f)
                .SetUpdate(true)
                .SetId("LevelMessagePanel")
                .OnComplete(() =>
                {
                    text.transform.DOScale(0, 0.2f)
                        .SetDelay(0.36f)
                        .SetUpdate(true)
                        .SetId("LevelMessagePanel")
                        .OnComplete(() => ReturnText(text));
                });
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

        private void ReturnText(TextMeshProUGUI text)
        {
            _borrowedTexts.Remove(text);
            _textPool.Release(text);
        }

        /// <summary>归还前的统一重置，也是池的 actionOnRelease（预热/收回路径复用）：
        /// 杀掉该节点上的整条展示补间链并复位视觉。</summary>
        private void ResetText(TextMeshProUGUI text)
        {
            text.transform.DOKill();
            text.transform.localScale = Vector3.zero;
            text.gameObject.SetActive(false);
        }

        private void ResetFloatingTexts()
        {
            foreach (TextMeshProUGUI text in _borrowedTexts)
                _textPool.Release(text); // actionOnRelease(ResetText) 负责杀链与视觉复位
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
