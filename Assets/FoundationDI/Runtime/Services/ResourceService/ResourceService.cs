using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DarkNaku.FoundationDI
{
    public interface IResourceService : IDisposable
    {
        Awaitable<T> LoadAsync<T>(string key) where T : Object;
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

        // 진행 중인 로드의 대기자들. Awaitable은 단일 await만 허용하므로(하나의 completion source를
        // 여러 호출자가 공유할 수 없다) 호출자마다 completion source를 만들어 리스트에 담고,
        // 로드 완료 시 전부 같은 결과로 깨운다. → 같은 키 동시 로드는 provider를 1회만 호출.
        private readonly Dictionary<string, List<AwaitableCompletionSource<Object>>> _loading = new();

        public ResourceService(IResourceProvider provider)
        {
            _provider = provider;
        }

        public async Awaitable<T> LoadAsync<T>(string key) where T : Object
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                entry.RefCount++;
                return entry.Asset as T;
            }

            var waiter = new AwaitableCompletionSource<Object>();

            if (_loading.TryGetValue(key, out var waiters))
            {
                // 이미 진행 중인 로드에 편승한다(provider 재호출 없음).
                waiters.Add(waiter);
            }
            else
            {
                _loading[key] = new List<AwaitableCompletionSource<Object>> { waiter };
                _ = LoadAndCacheAsync<T>(key);
            }

            var asset = await waiter.Awaitable;

            if (asset == null)
            {
                // 실패한 로드는 캐시/참조 카운트에 반영하지 않는다 → 호출자는 Release 짝을 맞출 필요가 없다.
                return null;
            }

            _cache[key].RefCount++;
            return asset as T;
        }

        private async Awaitable LoadAndCacheAsync<T>(string key) where T : Object
        {
            var asset = await _provider.LoadAsync<T>(key);

            if (asset != null)
            {
                // 실패(null)는 캐시하지 않는다. 다음 로드가 다시 시도할 수 있게 둔다.
                if (!_cache.ContainsKey(key))
                {
                    _cache[key] = new Entry { Asset = asset, RefCount = 0 };
                }
            }
            else
            {
                // 실패: provider가 보유했을 수 있는 핸들(Addressables 등)을 정리한다.
                _provider.Release(key);
            }

            var waiters = _loading[key];
            _loading.Remove(key);

            // 대기 중이던 모든 호출자를 같은 결과로 깨운다.
            foreach (var waiter in waiters)
            {
                waiter.TrySetResult(asset);
            }
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
