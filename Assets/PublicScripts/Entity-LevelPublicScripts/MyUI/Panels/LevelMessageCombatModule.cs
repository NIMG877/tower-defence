using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace MyUI
{
    /// <summary>
    /// Owns combat event subscriptions, floating combat text, and settlement statistics.
    /// </summary>
    internal sealed class LevelMessageCombatModule
    {
        private readonly Transform _textRoot;
        private readonly List<TextMeshProUGUI>[] _textPools;
        private readonly HashSet<Entity> _registeredEntities = new HashSet<Entity>();
        private readonly Dictionary<TextMeshProUGUI, int> _borrowedTexts =
            new Dictionary<TextMeshProUGUI, int>();
        private readonly Dictionary<EntityID, int> _statisticsIndex =
            new Dictionary<EntityID, int>();

        private DamageStatisticData[] _damageStatistics;

        public DamageStatisticData[] DamageStatistics => _damageStatistics;

        public LevelMessageCombatModule(GameObject root)
        {
            _textRoot = LevelMessageViewLookup.Get<Transform>(root, "texts");
            string[] textNames =
                { "textDamage", "textHeal", "textAddCost", "textReduceCost", "spAdd", "miss" };
            _textPools = new List<TextMeshProUGUI>[textNames.Length];
            for (int i = 0; i < textNames.Length; i++)
            {
                TextMeshProUGUI sample =
                    LevelMessageViewLookup.Get<TextMeshProUGUI>(root, $"texts/{textNames[i]}");
                _textPools[i] = new List<TextMeshProUGUI> { sample };
                for (int j = 0; j < 4; j++)
                    _textPools[i].Add(Object.Instantiate(sample, _textRoot));
            }
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

        public void ShowText(Vector2 entityPosition, int textType, int value)
        {
            if (textType < 0 || textType >= _textPools.Length)
            {
                Debug.LogWarning($"Unsupported level combat text type: {textType}");
                return;
            }

            TextMeshProUGUI text;
            if (_textPools[textType].Count > 1)
            {
                text = _textPools[textType][1];
                _textPools[textType].RemoveAt(1);
            }
            else
            {
                text = Object.Instantiate(_textPools[textType][0], _textRoot);
            }
            _borrowedTexts[text] = textType;

            switch (textType)
            {
                case 0: text.text = value.ToString(); break;
                case 1: text.text = "+" + value; break;
                case 2: text.text = "COST " + value; break;
                case 3: text.text = "COST- " + value; break;
                case 4: text.text = "SP " + value; break;
            }

            text.transform.position = entityPosition + 1.2f * Vector2.up + 0.1f * Random.insideUnitCircle;
            text.transform.localScale = Vector3.zero;
            text.gameObject.SetActive(true);
            text.transform.DOScale(1, 0.2f)
                .SetUpdate(true)
                .SetId("LevelMessagePanel")
                .OnComplete(() => HideTextAfterDelay(text).Forget());
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
                    0.3f,
                    true,
                    PlayerLoopTiming.Update,
                    LevelResourceSharing.LevelCtk);
            }
            catch (System.OperationCanceledException)
            {
                ReturnText(text);
                return;
            }

            if (!_borrowedTexts.ContainsKey(text))
                return;
            text.transform.DOScale(0, 0.2f)
                .SetUpdate(true)
                .SetId("LevelMessagePanel")
                .OnComplete(() => ReturnText(text));
        }

        private void ReturnText(TextMeshProUGUI text)
        {
            if (text == null || !_borrowedTexts.TryGetValue(text, out int textType))
                return;
            text.transform.DOKill();
            text.transform.localScale = Vector3.zero;
            text.gameObject.SetActive(false);
            _borrowedTexts.Remove(text);
            if (!_textPools[textType].Contains(text))
                _textPools[textType].Add(text);
        }

        private void ResetFloatingTexts()
        {
            foreach (KeyValuePair<TextMeshProUGUI, int> pair in _borrowedTexts)
            {
                TextMeshProUGUI text = pair.Key;
                if (text == null)
                    continue;
                text.transform.DOKill();
                text.transform.localScale = Vector3.zero;
                text.gameObject.SetActive(false);
                if (!_textPools[pair.Value].Contains(text))
                    _textPools[pair.Value].Add(text);
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
                    ShowText(target.transform.position, 5, 0);
                    break;
                case DamageResolutionKind.Damage:
                    RecordDamage(
                        target,
                        resolution.Origin,
                        resolution.Amount,
                        resolution.DamageType);
                    if (resolution.IsCritical)
                        ShowText(target.transform.position, 0, (int)resolution.Amount);
                    break;
                case DamageResolutionKind.Healing:
                    RecordHealing(
                        resolution.Origin,
                        resolution.Amount);
                    ShowText(target.Movement.Position, 1, (int)resolution.Amount);
                    break;
            }
        }
    }
}
