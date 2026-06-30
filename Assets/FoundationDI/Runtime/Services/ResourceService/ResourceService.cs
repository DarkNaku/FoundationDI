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
        private readonly Dictionary<string, UniTaskCompletionSource<Object>> _loading = new();

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

            if (!_loading.TryGetValue(key, out var tcs))
            {
                tcs = new UniTaskCompletionSource<Object>();
                _loading[key] = tcs;
                LoadAndCacheAsync<T>(key, tcs).Forget();
            }

            var asset = await tcs.Task;
            _loading.Remove(key);

            if (asset == null)
            {
                // 실패한 로드는 캐시/참조 카운트에 반영하지 않는다 → 호출자는 Release 짝을 맞출 필요가 없다.
                _provider.Release(key);
                return null;
            }

            _cache[key].RefCount++;
            return asset as T;
        }

        private async UniTaskVoid LoadAndCacheAsync<T>(string key, UniTaskCompletionSource<Object> tcs) where T : Object
        {
            var asset = await _provider.LoadAsync<T>(key);

            // 실패(null)는 캐시하지 않는다. 다음 로드가 다시 시도할 수 있게 둔다.
            if (asset != null && !_cache.ContainsKey(key))
            {
                _cache[key] = new Entry { Asset = asset, RefCount = 0 };
            }

            tcs.TrySetResult(asset);
        }

        public T Load<T>(string key) where T : Object
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                entry.RefCount++;
                return entry.Asset as T;
            }

            var asset = _provider.Load<T>(key);

            if (asset == null)
            {
                // 실패한 로드는 캐시/참조 카운트에 반영하지 않는다 → 호출자는 Release 짝을 맞출 필요가 없다.
                // provider가 보유했을 수 있는 핸들(Addressables 등)은 정리한다.
                _provider.Release(key);
                return null;
            }

            _cache[key] = new Entry { Asset = asset, RefCount = 1 };
            return asset;
        }

        public void Release(string key)
        {
            if (!_cache.TryGetValue(key, out var entry)) return;

            entry.RefCount--;

            if (entry.RefCount <= 0)
            {
                _cache.Remove(key);
                _provider.Release(key);
            }
        }

        public void Dispose()
        {
            foreach (var key in new List<string>(_cache.Keys))
            {
                _provider.Release(key);
            }

            _cache.Clear();
        }
    }
}
