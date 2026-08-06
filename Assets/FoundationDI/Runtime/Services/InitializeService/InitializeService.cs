using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    public interface IInitializeService : IDisposable
    {
        Awaitable InitializeAsync(InitializeCatalog catalog);
    }

    public sealed class InitializeService : IInitializeService
    {
        private readonly IObjectResolver _resolver;
        private readonly HashSet<InitializeItem> _initializedItems = new();
        private readonly HashSet<InitializeCatalog> _initializedCatalogs = new();

        public InitializeService(IObjectResolver resolver)
        {
            _resolver = resolver;
        }

        public async Awaitable InitializeAsync(InitializeCatalog catalog)
        {
            if (_initializedCatalogs.Contains(catalog)) return;

            foreach (var item in catalog.Items)
            {
                if (item == null) continue;
                if (_initializedItems.Contains(item)) continue;
                await item.InitializeAsync(_resolver);
                _initializedItems.Add(item);
            }

            _initializedCatalogs.Add(catalog);
        }

        public void Dispose()
        {
            _initializedItems.Clear();
            _initializedCatalogs.Clear();
        }
    }
}
