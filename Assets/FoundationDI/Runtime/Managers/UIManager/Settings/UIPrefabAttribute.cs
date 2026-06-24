using System;

namespace DarkNaku.FoundationDI
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class UIPrefabAttribute : Attribute
    {
        public string Key { get; }
        public UIPrefabAttribute(string key) => Key = key;
    }
}
