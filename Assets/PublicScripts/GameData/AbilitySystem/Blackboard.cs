using System.Collections.Generic;

namespace AbilitySystem
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

        /// <summary>Shallow copy: detached executions fork the shared board so
        /// later host writes (pool recycle clears it) cannot reach them.</summary>
        public Blackboard Clone()
        {
            var copy = new Blackboard();
            foreach (KeyValuePair<string, object> pair in _data)
                copy._data[pair.Key] = pair.Value;
            return copy;
        }

        public void Remove(string key) => _data.Remove(key);

        public void Clear() => _data.Clear();
    }
}
