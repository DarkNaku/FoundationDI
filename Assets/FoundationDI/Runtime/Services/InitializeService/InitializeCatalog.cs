using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "InitializeCatalog", menuName = "DarkNaku/InitializeCatalog")]
    public class InitializeCatalog : ScriptableObject
    {
        [SerializeField] private List<InitializeItem> _items = new();

        public IReadOnlyList<InitializeItem> Items => _items;
    }
}
