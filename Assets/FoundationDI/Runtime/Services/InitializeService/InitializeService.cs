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

        public InitializeService(IObjectResolver resolver)
        {
            _resolver = resolver;
        }

        public async Awaitable InitializeAsync(InitializeCatalog catalog)
        {
            foreach (var item in catalog.Items)
            {
                if (item == null) continue;
                await item.InitializeAsync(_resolver);
            }
        }

        public void Dispose()
        {
        }
    }
}
