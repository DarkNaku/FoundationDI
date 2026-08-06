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

    public static class InitializeServiceVContainerExtensions
    {
        /// <summary>
        /// InitializeService를 컨테이너에 싱글턴으로 등록한다.
        /// IObjectResolver는 VContainer가 자동 주입한다.
        /// </summary>
        public static void RegisterInitializeService(this IContainerBuilder builder)
        {
            builder.Register<IInitializeService, InitializeService>(Lifetime.Singleton);
        }
    }
}
