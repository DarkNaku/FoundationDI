using System;
using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    internal static class UIPrefabKeyResolver
    {
        private static readonly Dictionary<Type, string> _cache = new();

        public static string Resolve(Type type)
        {
            if (_cache.TryGetValue(type, out var key)) return key;

            var attr = (UIPrefabAttribute)Attribute.GetCustomAttribute(type, typeof(UIPrefabAttribute));
            if (attr == null)
                throw new InvalidOperationException($"[UIManager] {type.Name}에 [UIPrefab] 속성이 없습니다.");

            _cache[type] = attr.Key;
            return attr.Key;
        }
    }
}
