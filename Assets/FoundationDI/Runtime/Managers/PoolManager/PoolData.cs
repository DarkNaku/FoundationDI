using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Pool;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace DarkNaku.FoundationDI
{
    public class PoolData
    {
        private IObjectPool<IPoolItem> _pool;
        private AsyncOperationHandle<GameObject> _handle;
        private HashSet<IPoolItem> _items;

        public PoolData(IObjectPool<IPoolItem> pool, AsyncOperationHandle<GameObject> handle)
        {
            _pool = pool;
            _handle = handle;
            _items = new HashSet<IPoolItem>();
        }

        public IPoolItem Get()
        {
            var item = _pool.Get();

            item.PD = this;

            _items.Add(item);

            return item;
        }

        public void Release(IPoolItem item)
        {
            _pool.Release(item);

            _items.Remove(item);
        }

        public void Clear()
        {
            // Pool items 정리 시 null check 추가
            foreach (var item in _items)
            {
                // GameObject가 이미 파괴되었을 수 있으니 체크
                if (item != null)
                {
                    _pool.Release(item);
                }
            }

            if (_handle.IsValid())
            {
                Addressables.Release(_handle);
            }

            _items.Clear();
            _items = null;

            _pool.Clear();
            _pool = null;
        }
    }
}