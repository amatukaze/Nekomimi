using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;

namespace Sakuno.Nekomimi
{
    public sealed class HttpHeaderCollection : IEnumerable<KeyValuePair<string, IEnumerable<string>>>
    {
        private readonly NameValueCollection _store;

        public int Count => _store.Count;

        public string this[string name]
        {
            get => _store[name];
            set => _store[name] = value;
        }

        internal HttpHeaderCollection()
        {
            _store = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
        }

        public void Add(string name, string value) => _store.Add(name, value);
        public void Remove(string name) => _store.Remove(name);

        public bool TryGetValue(string name, [NotNullWhen(true)] out string? value)
        {
            var values = _store.GetValues(name);
            if (values == null)
            {
                value = default;
                return false;
            }

            value = values[0];
            return true;
        }
        public bool TryGetValues(string name, out IEnumerable<string> values)
        {
            values = _store.GetValues(name);
            return values != null;
        }

        public IEnumerator<KeyValuePair<string, IEnumerable<string>>> GetEnumerator()
        {
            foreach (var key in _store.AllKeys)
                yield return new KeyValuePair<string, IEnumerable<string>>(key, _store.GetValues(key));
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
