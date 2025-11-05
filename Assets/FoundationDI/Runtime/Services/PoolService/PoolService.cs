using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Pool;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace DarkNaku.FoundationDI
{
    public interface IPoolService : IDisposable {
        GameObject Get(string key, Transform parent = null);
        T Get<T>(string key, Transform parent = null) where T : class;
        void Release(GameObject item, float delay = 0f);
    }
    
    public class PoolService : IPoolService
    {
        private readonly Dictionary<string, PoolData> _table = new();
        private readonly Transform _root = new GameObject("[PoolService]").transform;
        
        public PoolService()
        {
            _table = new();
            _root = new GameObject("[PoolService]").transform;
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
            foreach (var data in _table.Values)
            {
                data.Clear();
            }

            _table.Clear();

            Resources.UnloadUnusedAssets();
        }

        private PoolData Register(string key, GameObject prefab, AsyncOperationHandle<GameObject> handle)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError($"[PoolService] Register : Key is wrong.");
                return null;
            }

            if (prefab == null)
            {
                Debug.LogError($"[PoolService] Register : Prefab is null.");
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

            var data = new PoolData(pool, handle);

            _table.TryAdd(key, data);

            return data;
        }

        private PoolData Load(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError($"[PoolService] Load : Key is wrong.");
                return null;
            }

            var go = Resources.Load<GameObject>(key);

            if (go == null)
            {
                try
                {
                    var handle = Addressables.LoadAssetAsync<GameObject>(key);
                    go = handle.WaitForCompletion();
                    return Register(key, go, handle);
                }
                catch (InvalidKeyException e)
                {
                    Debug.LogError($"[PoolService] Load : {e.Message}");
                    return null;
                }
            }

            return Register(key, go, default);
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
            item.OnDestroyItem();

            Object.Destroy(item.GO);
        }
    }
}