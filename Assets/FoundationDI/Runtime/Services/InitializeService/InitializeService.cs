using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public interface IInitializeService : IDisposable
    {
        Awaitable InitializeAsync(InitializeCatalog catalog);
    }

    public sealed class InitializeService : IInitializeService
    {
        private readonly IServiceResolver _resolver;
        private readonly HashSet<InitializeItem> _initializedItems = new();
        private readonly HashSet<InitializeCatalog> _initializedCatalogs = new();

        public InitializeService(IServiceResolver resolver)
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
        /// IServiceResolver는 ServiceResolverBootstrap이 자동 등록한다.
        /// </summary>
        public static Reflex.Core.ContainerBuilder RegisterInitializeService(
            this Reflex.Core.ContainerBuilder builder)
        {
            return builder.RegisterType(
                typeof(InitializeService), new[] { typeof(IInitializeService) },
                Reflex.Enums.Lifetime.Singleton, Reflex.Enums.Resolution.Lazy);
        }
    }
}
