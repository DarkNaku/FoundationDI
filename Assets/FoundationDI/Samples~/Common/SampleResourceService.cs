using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DarkNaku.FoundationDI.Samples
{
    /// 샘플용 IResourceService — 인스펙터 (key→prefab) 매핑으로 Addressables 없이 동작.
    public class SampleResourceService : MonoBehaviour, IResourceService
    {
        [System.Serializable]
        public struct Entry { public string key; public GameObject prefab; }

        [SerializeField] private Entry[] _entries;

        private Dictionary<string, GameObject> _map;
        private Dictionary<string, GameObject> Map
        {
            get
            {
                if (_map == null)
                {
                    _map = new Dictionary<string, GameObject>();
                    foreach (var e in _entries) _map[e.key] = e.prefab;
                }
                return _map;
            }
        }

        public Awaitable<T> LoadAsync<T>(string key) where T : Object
        {
            // 즉시 완료된 Awaitable 반환 (동기 매핑을 비동기 표면에 맞춤).
            var source = new AwaitableCompletionSource<T>();
            source.SetResult(Load<T>(key));
            return source.Awaitable;
        }

        public T Load<T>(string key) where T : Object
        {
            if (Map.TryGetValue(key, out var prefab)) return prefab as T;
            Debug.LogError($"[SampleResourceService] 매핑되지 않은 키: {key}");
            return null;
        }

        public void Release(string key) { }
        public void Dispose() { }
    }
}
