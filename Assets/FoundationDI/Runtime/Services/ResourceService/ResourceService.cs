using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Object = UnityEngine.Object;

namespace DarkNaku.FoundationDI
{
    public interface IResourceService : IDisposable
    {
        UniTask<T> LoadAsync<T>(string key) where T : Object;
        T Load<T>(string key) where T : Object;
        void Release(string key);
    }

    public class ResourceService : IResourceService
    {
        private sealed class Entry
        {
            public Object Asset;
            public int RefCount;
        }

        private readonly IResourceProvider _provider;
        private readonly Dictionary<string, Entry> _cache = new();

        public ResourceService() : this(new AddressableResourceProvider())
        {
        }

        public ResourceService(IResourceProvider provider)
        {
            _provider = provider;
        }

        public async UniTask<T> LoadAsync<T>(string key) where T : Object
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                entry.RefCount++;
                return entry.Asset as T;
            }

            var asset = await _provider.LoadAsync<T>(key);
            _cache[key] = new Entry { Asset = asset, RefCount = 1 };
            return asset;
        }

        public T Load<T>(string key) where T : Object
        {
            throw new NotImplementedException();
        }

        public void Release(string key)
        {
            var entry = _cache[key];
            entry.RefCount--;

            if (entry.RefCount <= 0)
            {
                _cache.Remove(key);
                _provider.Release(key);
            }
        }

        public void Dispose()
        {
        }
    }
}
