using System.Collections.Generic;
using UnityEngine;

namespace AbilitySystem
{
    /// <summary>
    /// 运行时技能图标池：聚合现有 AbilityConfig 资产的 icon（Resources.LoadAll 递归
    /// "Abilities" 目录），键为 Sprite 名。LLM 无法生成美术，生成配置只能从池中选
    /// key（清单经 GetAllKeys() 进入提示词）；miss 返回 null——AbilityCard 对 null
    /// icon 有兜底，图标留空不致命。
    /// </summary>
    public static class AbilityIconPool
    {
        private const string AbilitiesResourceRoot = "Abilities";

        private static Dictionary<string, Sprite> _bySpriteName;

        public static Sprite Resolve(string key)
        {
            EnsureLoaded();
            return key != null && _bySpriteName.TryGetValue(key, out Sprite sprite) ? sprite : null;
        }

        /// <summary>提示词可用图标清单（Sprite 名集合，去重后）。</summary>
        public static List<string> GetAllKeys()
        {
            EnsureLoaded();
            return new List<string>(_bySpriteName.Keys);
        }

        public static void ResetForTests() => _bySpriteName = null;

        private static void EnsureLoaded()
        {
            if (_bySpriteName != null) return;
            _bySpriteName = new Dictionary<string, Sprite>();
            foreach (AbilityConfig cfg in Resources.LoadAll<AbilityConfig>(AbilitiesResourceRoot))
            {
                Sprite icon = cfg != null ? cfg.icon : null;
                if (icon != null && !_bySpriteName.ContainsKey(icon.name))
                    _bySpriteName.Add(icon.name, icon);
            }
        }
    }
}
