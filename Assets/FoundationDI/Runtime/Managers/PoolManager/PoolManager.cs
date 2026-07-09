using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;
using VContainer;

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
        private bool _disposed;

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
            if (_disposed) return null;

            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError($"[PoolManager] Get : Key is wrong.");
                return null;
            }

            if (!_table.TryGetValue(key, out var data))
            {
                data = Load(key);
            }

            if (data == null) return null;

            var item = data.Get();

            // worldPositionStays:false — 풀 아이템은 프리팹의 로컬 트랜스폼을 유지해야 한다.
            // 기본값(true)은 부모의 월드 위치를 상쇄해 RectTransform 레이아웃을 어긋나게 한다.
            item.GO.transform.SetParent(parent == null ? _root : parent, false);

            return item.GO;
        }

        public T Get<T>(string key, Transform parent = null) where T : class
        {
            var go = Get(key, parent);

            if (go == null) return null;

            var component = go.GetComponent<T>();

            if (component == null)
            {
                // 컴포넌트가 없으면 아이템이 활성 상태로 방치되지 않도록 즉시 반환한다.
                Debug.LogError($"[PoolManager] Get : Component '{typeof(T).Name}' not found. (key: {key})");
                Release(go);
                return null;
            }

            return component;
        }

        public void Release(GameObject item, float delay = 0f)
        {
            if (item == null) return;

            item.GetComponent<IPoolItem>()?.Release(delay);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

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

            if (!_table.TryAdd(key, data))
            {
                // 이미 등록돼 있으면(경쟁 등) 새로 만든 풀은 버리고 기존 것을 반환한다.
                pool.Clear();
                return _table[key];
            }

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

            // IPoolItem(인터페이스) 참조로 == null 비교하면 Unity의 fake-null이 감지되지 않는다.
            // GO 게터는 내부에서 UnityEngine.Object로 비교하므로 파괴된 경우 null을 돌려준다.
            var go = item.GO;

            if (go == null) return;

            item.OnDestroyItem();

            if (Application.isPlaying)
            {
                Object.Destroy(go);
            }
            else
            {
                Object.DestroyImmediate(go);
            }
        }
    }

    public static class PoolManagerVContainerExtensions
    {
        /// <summary>
        /// PoolManager를 컨테이너에 등록한다.
        /// 씬 LifetimeScope에서 호출하면 풀이 씬과 수명을 함께하여, 씬 언로드 시
        /// scope.Dispose()로 풀과 로드한 에셋(IResourceService.Release)이 자동 정리된다.
        /// <paramref name="root"/>(보통 씬 LifetimeScope의 transform)를 넘기면 풀 루트가
        /// 활성 씬이 아니라 그 transform이 속한 씬에 확실히 귀속된다(additive 로드 안전).
        /// 전제: 부모(루트) 스코프에 <see cref="IResourceService"/>가 이미 등록되어 있어야 한다
        /// (PoolManager가 프리팹 로드를 IResourceService에 위임함).
        /// </summary>
        public static void RegisterPoolManager(this IContainerBuilder builder, Transform root = null)
        {
            var registration = builder.Register<IPoolManager, PoolManager>(Lifetime.Singleton);

            if (root != null)
            {
                registration.WithParameter(root);
            }
        }
    }
}
