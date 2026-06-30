using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

namespace DarkNaku.FoundationDI
{
    public interface IPoolManager : IDisposable {
        GameObject Get(string key, Transform parent = null);
        T Get<T>(string key, Transform parent = null) where T : class;
        void Release(GameObject item, float delay = 0f);
    }

    public class PoolManager : IPoolManager
    {
        private readonly IResourceService _resourceService;
        private readonly Dictionary<string, PoolData> _table;
        private readonly Transform _root;

        public PoolManager(IResourceService resourceService, Transform parent = null)
        {
            _resourceService = resourceService;
            _table = new();

            // 풀 루트는 DontDestroyOnLoad로 두지 않는다.
            // parent(보통 씬 LifetimeScope의 transform)가 주어지면 그 아래에 둬서
            // 풀 루트가 활성 씬이 아니라 스코프가 속한 씬에 확실히 귀속되도록 한다.
            // 그러면 씬 언로드 시 풀도 함께 정리된다. parent가 없으면 활성 씬에 생성된다.
            var root = new GameObject("[PoolManager]");

            _root = root.transform;

            if (parent != null)
            {
                _root.SetParent(parent, false);
            }
        }

        public GameObject Get(string key, Transform parent = null)
        {
            if (!_table.TryGetValue(key, out var data))
            {
                data = Load(key);
            }

            if (data == null) return null;

            var item = data.Get();

            item.GO.transform.SetParent(parent == null ? _root : parent);

            return item.GO;

        }

        public T Get<T>(string key, Transform parent = null) where T : class
        {
            return Get(key, parent)?.GetComponent<T>();
        }

        public void Release(GameObject item, float delay = 0f)
        {
            if (item == null) return;

            item.GetComponent<IPoolItem>()?.Release(delay);
        }

        public void Dispose()
        {
            // Pool items 정리 (GameObject 파괴 전에 수행) 후 로드한 에셋을 ResourceService에 반환
            foreach (var pair in _table)
            {
                pair.Value.Clear();
                _resourceService.Release(pair.Key);
            }

            _table.Clear();

            // 씬 언로드/플레이모드 종료 시 Unity의 오브젝트 파괴와 Container.Dispose 순서가
            // 보장되지 않는다. _root가 먼저 파괴됐으면 fake-null 가드로 건너뛴다.
            if (_root != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(_root.gameObject);
                }
                else
                {
                    Object.DestroyImmediate(_root.gameObject);
                }
            }
        }

        private PoolData Register(string key, GameObject prefab)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError($"[PoolManager] Register : Key is wrong.");
                return null;
            }

            if (prefab == null)
            {
                Debug.LogError($"[PoolManager] Register : Prefab is null.");
                return null;
            }

            var pool = new ObjectPool<IPoolItem>(
                () =>
                {
                    var go = Object.Instantiate(prefab);

                    var item = go.GetComponent<IPoolItem>();

                    if (item == null)
                    {
                        item = go.AddComponent<PoolItem>();
                    }

                    item.OnCreateItem();

                    return item;
                },
                OnGetItem,
                OnReleaseItem,
                OnDestroyItem);

            var data = new PoolData(pool);

            _table.TryAdd(key, data);

            return data;
        }

        private PoolData Load(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError($"[PoolManager] Load : Key is wrong.");
                return null;
            }

            // 에셋 로딩은 ResourceService에 위임한다 (핸들/참조 카운팅을 한 곳에서 관리).
            var prefab = _resourceService.Load<GameObject>(key);

            if (prefab == null)
            {
                Debug.LogError($"[PoolManager] Load : Failed to load prefab. (key: {key})");
                // 실패한 로드는 ResourceService가 캐시/카운트하지 않으므로 Release로 보상할 필요가 없다.
                return null;
            }

            return Register(key, prefab);
        }

        private void OnGetItem(IPoolItem item)
        {
            item.OnGetItem();
        }

        private void OnReleaseItem(IPoolItem item)
        {
            item.OnReleaseItem();
        }

        private void OnDestroyItem(IPoolItem item)
        {
            if (item == null) return;

            item.OnDestroyItem();

            if (item.GO != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(item.GO);
                }
                else
                {
                    Object.DestroyImmediate(item.GO);
                }
            }
        }
    }
}
