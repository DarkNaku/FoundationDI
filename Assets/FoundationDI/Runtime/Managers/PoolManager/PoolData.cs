using System;
using System.Collections.Generic;
using UnityEngine.Pool;

namespace DarkNaku.FoundationDI
{
    public class PoolData
    {
        private IObjectPool<IPoolItem> _pool;
        private HashSet<IPoolItem> _items;
        private readonly Func<IPoolItem> _create;

        public PoolData(IObjectPool<IPoolItem> pool, Func<IPoolItem> create = null)
        {
            _pool = pool;
            _create = create;
            _items = new HashSet<IPoolItem>();
        }

        public IPoolItem Get()
        {
            var item = _pool.Get();

            item.PD = this;

            _items.Add(item);

            return item;
        }

        /// <summary>
        /// 대기 인스턴스가 count개가 되도록 생성 함수만 불러 만든 뒤 바로 풀에 넣는다.
        /// ObjectPool에는 '만들어서 대기열에 넣는' API가 없고, Get→Release로 흉내 내면
        /// OnGetItem(활성화)이 불필요하게 돈다. 꺼낸 목록(_items)에는 넣지 않는다.
        /// </summary>
        public void Prewarm(int count)
        {
            if (_pool == null || _create == null) return;

            for (var i = _pool.CountInactive; i < count; i++)
            {
                var item = _create();

                item.PD = this;

                _pool.Release(item);
            }
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
