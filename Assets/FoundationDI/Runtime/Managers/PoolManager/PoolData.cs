using System.Collections.Generic;
using UnityEngine.Pool;

namespace DarkNaku.FoundationDI
{
    public class PoolData
    {
        private IObjectPool<IPoolItem> _pool;
        private HashSet<IPoolItem> _items;

        public PoolData(IObjectPool<IPoolItem> pool)
        {
            _pool = pool;
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
            // _pool은 Clear()에서만 null이 되고 Clear()는 Dispose()에서만 불린다.
            // 즉 null이면 이 풀은 이미 정리됐다는 뜻이다. IPoolItem.Release가 public이라
            // PoolManager를 거치지 않고 아이템이 직접 반환을 시도할 수 있으므로
            // (예약해 둔 지연 반환 포함) 정리된 상태는 여기서도 거부한다.
            if (_pool == null) return;

            _pool.Release(item);

            _items.Remove(item);
        }

        public void Clear()
        {
            // Pool items 정리 시 null check 추가
            foreach (var item in _items)
            {
                // IPoolItem(인터페이스) 참조로 == null 하면 Unity fake-null이 감지되지 않으므로
                // GO(UnityEngine.Object)를 경유해 이미 파괴된 아이템을 걸러낸다.
                if (item != null && item.GO != null)
                {
                    _pool.Release(item);
                }
            }

            _items.Clear();
            _items = null;

            _pool.Clear();
            _pool = null;
        }
    }
}
