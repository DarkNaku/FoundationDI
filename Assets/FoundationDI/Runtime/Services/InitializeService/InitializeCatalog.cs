using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public class InitializeCatalog : ScriptableObject
    {
        [SerializeField] private List<InitializeItem> _items = new();

        public IReadOnlyList<InitializeItem> Items => _items;
    }
}
