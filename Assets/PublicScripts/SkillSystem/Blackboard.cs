using System.Collections.Generic;

namespace SkillSystem
{
    public class Blackboard
    {
        private readonly Dictionary<string, object> _data = new Dictionary<string, object>();

        public T Get<T>(string key, T defaultValue = default)
        {
            return _data.TryGetValue(key, out var v) ? (T)v : defaultValue;
        }

        public void Set(string key, object value)
        {
            _data[key] = value;
        }

        public bool Has(string key) => _data.ContainsKey(key);

        public void Remove(string key) => _data.Remove(key);

        public void Clear() => _data.Clear();
    }
}
