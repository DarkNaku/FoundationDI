using System;
using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    internal sealed class InstanceCache
    {
        private readonly Dictionary<Type, Stack<object>> _table = new();

        public bool TryGet(Type type, out object instance)
        {
            instance = null;
            if (_table.TryGetValue(type, out var stack) && stack.Count > 0)
            {
                instance = stack.Pop();
                return true;
            }
            return false;
        }

        public void Register(Type type, object instance)
        {
            if (!_table.TryGetValue(type, out var stack))
            {
                stack = new Stack<object>();
                _table[type] = stack;
            }
            stack.Push(instance);
        }

        public void Remove(Type type) => _table.Remove(type);

        public IReadOnlyCollection<object> AllInstances
        {
            get
            {
                var list = new List<object>();
                foreach (var stack in _table.Values) list.AddRange(stack);
                return list;
            }
        }

        public void Clear() => _table.Clear();
    }
}
